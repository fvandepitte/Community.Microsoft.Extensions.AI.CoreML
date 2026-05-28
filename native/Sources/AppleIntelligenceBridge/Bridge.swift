// ============================================================================
// Bridge.swift — C-callable bridge from .NET to Apple Foundation Models
//
// Exported functions are called via P/Invoke from
// Community.Microsoft.Extensions.AI.CoreML (C#).
//
// Design principles:
//   - Each call is stateless: the C# side passes the full message history as
//     a JSON string. A fresh LanguageModelSession is created per call with the
//     pre-built Transcript, matching the IChatClient contract.
//   - Async Swift is bridged to synchronous C callbacks using DispatchSemaphore.
//   - Streaming uses cumulative snapshots from FoundationModels; deltas are
//     computed by comparing against the previous snapshot length.
//   - All errors are returned via the `error` callback parameter (never thrown
//     across the C boundary).
// ============================================================================

import Foundation
import FoundationModels

// MARK: - Message model (mirrors ChatMessage on the C# side)

private struct ToolCallDTO: Decodable {
    let id: String
    let name: String
    /// JSON-encoded string of the function arguments object.
    let arguments: String
}

private struct Message: Decodable {
    let role: String
    let content: String?
    let tool_calls: [ToolCallDTO]?
    let tool_call_id: String?
    let tool_name: String?
}

/// Tool definition forwarded from the .NET side.
/// `parameters` is the JSON Schema object for the tool's arguments.
private struct ToolDTO: Decodable {
    let name: String
    let description: String?
    let parameters: JSONValue?
}

/// Full request payload sent from .NET. Supports an optional `tools` array.
private struct Request: Decodable {
    let messages: [Message]
    let tools: [ToolDTO]?
}

/// Small recursive JSON value type — lets us inspect tool parameter schemas
/// without going through Foundation's untyped `Any` casts.
private indirect enum JSONValue: Decodable {
    case string(String)
    case number(Double)
    case integer(Int)
    case bool(Bool)
    case array([JSONValue])
    case object([String: JSONValue])
    case null

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() { self = .null; return }
        if let b = try? container.decode(Bool.self) { self = .bool(b); return }
        if let i = try? container.decode(Int.self) { self = .integer(i); return }
        if let d = try? container.decode(Double.self) { self = .number(d); return }
        if let s = try? container.decode(String.self) { self = .string(s); return }
        if let a = try? container.decode([JSONValue].self) { self = .array(a); return }
        if let o = try? container.decode([String: JSONValue].self) { self = .object(o); return }
        throw DecodingError.dataCorruptedError(
            in: container, debugDescription: "Unsupported JSON value")
    }

    var asString: String? { if case .string(let s) = self { return s } else { return nil } }
    var asBool: Bool? { if case .bool(let b) = self { return b } else { return nil } }
    var asObject: [String: JSONValue]? { if case .object(let o) = self { return o } else { return nil } }
    var asArray: [JSONValue]? { if case .array(let a) = self { return a } else { return nil } }
}

private let unsupportedMacOSMessage = "Apple Intelligence requires macOS 26.0 or newer."

// MARK: - Debug logging

private let debugEnabled: Bool = {
    guard let v = ProcessInfo.processInfo.environment["AIB_DEBUG"] else { return false }
    return !v.isEmpty && v != "0" && v.lowercased() != "false"
}()

private func dbg(_ tag: String, _ message: @autoclosure () -> String) {
    guard debugEnabled else { return }
    let line = "[aib:\(tag)] \(message())\n"
    if let data = line.data(using: .utf8) {
        FileHandle.standardError.write(data)
    }
}

// MARK: - Session builder

/// Builds a LanguageModelSession from a decoded message list and extracts the
/// final user prompt. The Transcript is pre-loaded with history so the model
/// has full context without re-inference.
///
/// Message role mapping:
///   "system"    → Transcript.Instructions
///   "user"      → Transcript.Prompt       (history entries)
///   "assistant" → Transcript.Response     (text) or Transcript.ToolCalls (tool_calls)
///   "tool"      → Transcript.ToolOutput
///   last user message → returned as finalPrompt (sent via respond / streamResponse).
///   When the conversation tail is .toolCalls + .toolOutput from a previous
///   round-trip, the most recent user question is still used as the prompt,
///   yielding a transcript that ends with .toolOutput — the natural shape Apple
///   expects when the model should generate a .response from a tool result.
@available(macOS 26.0, *)
private func buildSession(from request: Request) throws -> (session: LanguageModelSession, prompt: String) {
    let messages = request.messages

    dbg("req", "messages=\(messages.count) tools=\(request.tools?.count ?? 0)")
    for (i, m) in messages.enumerated() {
        let tc = m.tool_calls?.count ?? 0
        let preview = (m.content ?? "").prefix(120)
        dbg("msg", "[\(i)] role=\(m.role) tool_calls=\(tc) tool_call_id=\(m.tool_call_id ?? "-") content=\"\(preview)\"")
    }

    // Detect a tool-result follow-up: if any tool message is present, we keep
    // ALL user messages in the transcript and use a synthesized continuation
    // string as the active prompt. This gives the model the full
    // prompt → toolCalls → toolOutput chain that Apple expects before asking
    // for a final response.
    let hasToolOutput = messages.contains(where: { $0.role == "tool" })

    // Locate the most-recent user message; that becomes respond(to:) when
    // there is no tool follow-up. Otherwise we use a continuation directive.
    var promptIndex: Int? = nil
    for i in stride(from: messages.count - 1, through: 0, by: -1) {
        if messages[i].role == "user" {
            promptIndex = i
            break
        }
    }
    guard let promptIdx = promptIndex,
          let lastUserText = messages[promptIdx].content, !lastUserText.isEmpty else {
        throw BridgeError.invalidMessages("No user message found in request.")
    }

    let promptText = hasToolOutput
        ? "Using the tool results above, answer the user's most recent question concisely."
        : lastUserText

    let model = SystemLanguageModel(guardrails: .default)
    var entries: [Transcript.Entry] = []
    let toolDefinitions = buildToolDefinitions(from: request.tools ?? [])

    // System message → Instructions (must be first entry if present)
    if let sys = messages.first(where: { $0.role == "system" }) {
        let instr = Transcript.Instructions(
            segments: [.text(Transcript.TextSegment(content: sys.content ?? ""))],
            toolDefinitions: toolDefinitions
        )
        entries.append(.instructions(instr))
    } else if !toolDefinitions.isEmpty {
        let instr = Transcript.Instructions(
            segments: [.text(Transcript.TextSegment(content: ""))],
            toolDefinitions: toolDefinitions
        )
        entries.append(.instructions(instr))
    }

    // History: every message except system, and except the user prompt message
    // we will pass to respond(to:).
    for (i, msg) in messages.enumerated() {
        if msg.role == "system" { continue }
        // When there's no tool follow-up, the chosen user message is the active
        // prompt and should be excluded from the transcript history. When there
        // IS a tool follow-up, we want the user message in the transcript so
        // the model sees prompt → toolCalls → toolOutput.
        if !hasToolOutput && i == promptIdx { continue }

        switch msg.role {
        case "user":
            let prompt = Transcript.Prompt(
                segments: [.text(Transcript.TextSegment(content: msg.content ?? ""))],
                options: GenerationOptions()
            )
            entries.append(.prompt(prompt))

        case "assistant":
            if let toolCalls = msg.tool_calls, !toolCalls.isEmpty {
                var calls: [Transcript.ToolCall] = []
                for tc in toolCalls {
                    let args: GeneratedContent
                    if let parsed = try? GeneratedContent(json: tc.arguments) {
                        args = parsed
                    } else {
                        args = GeneratedContent(properties: [:])
                    }
                    calls.append(Transcript.ToolCall(
                        id: tc.id,
                        toolName: tc.name,
                        arguments: args
                    ))
                }
                if !calls.isEmpty {
                    entries.append(.toolCalls(Transcript.ToolCalls(
                        id: UUID().uuidString,
                        calls
                    )))
                }
            } else if let content = msg.content, !content.isEmpty {
                let response = Transcript.Response(
                    assetIDs: [],
                    segments: [.text(Transcript.TextSegment(content: content))]
                )
                entries.append(.response(response))
            }

        case "tool":
            let output = Transcript.ToolOutput(
                id: msg.tool_call_id ?? UUID().uuidString,
                toolName: msg.tool_name ?? "tool",
                segments: [.text(Transcript.TextSegment(content: msg.content ?? ""))]
            )
            entries.append(.toolOutput(output))

        default:
            break
        }
    }

    let session = entries.isEmpty
        ? LanguageModelSession(model: model)
        : LanguageModelSession(model: model, transcript: Transcript(entries: entries))

    dbg("session", "entries=\(entries.count) prompt=\"\(promptText.prefix(200))\"")
    for (i, e) in entries.enumerated() {
        let kind: String
        switch e {
        case .instructions: kind = "instructions"
        case .prompt: kind = "prompt"
        case .response: kind = "response"
        case .toolCalls: kind = "toolCalls"
        case .toolOutput: kind = "toolOutput"
        @unknown default: kind = "unknown"
        }
        dbg("entry", "[\(i)] \(kind)")
    }

    return (session, promptText)
}

// MARK: - Tool schema conversion

/// Convert tool DTOs into native FoundationModels `Transcript.ToolDefinition`s.
/// Tools whose schema cannot be converted are skipped silently — the .NET side
/// also injects a textual schema description so the model still has guidance.
@available(macOS 26.0, *)
private func buildToolDefinitions(from tools: [ToolDTO]) -> [Transcript.ToolDefinition] {
    var result: [Transcript.ToolDefinition] = []
    for tool in tools {
        guard let schema = buildGenerationSchema(name: tool.name, parameters: tool.parameters) else {
            continue
        }
        result.append(Transcript.ToolDefinition(
            name: tool.name,
            description: tool.description ?? tool.name,
            parameters: schema
        ))
    }
    return result
}

@available(macOS 26.0, *)
private func buildGenerationSchema(name: String, parameters: JSONValue?) -> GenerationSchema? {
    let root = buildDynamicSchema(name: name, value: parameters)
    return try? GenerationSchema(root: root, dependencies: [])
}

@available(macOS 26.0, *)
private func buildDynamicSchema(name: String, value: JSONValue?) -> DynamicGenerationSchema {
    guard let value, case .object(let obj) = value else {
        return DynamicGenerationSchema(name: name, description: nil, properties: [])
    }

    let typeValue = obj["type"]?.asString ?? "object"
    let description = obj["description"]?.asString

    switch typeValue {
    case "object":
        let props = obj["properties"]?.asObject ?? [:]
        let required = Set(obj["required"]?.asArray?.compactMap { $0.asString } ?? [])
        let dynProps: [DynamicGenerationSchema.Property] = props.map { (propName, propValue) in
            let child = buildDynamicSchema(name: propName, value: propValue)
            let propDescription = propValue.asObject?["description"]?.asString
            return DynamicGenerationSchema.Property(
                name: propName,
                description: propDescription,
                schema: child,
                isOptional: !required.contains(propName)
            )
        }
        return DynamicGenerationSchema(name: name, description: description, properties: dynProps)

    case "string":
        if let enumValues = obj["enum"]?.asArray?.compactMap({ $0.asString }), !enumValues.isEmpty {
            return DynamicGenerationSchema(name: name, description: description, anyOf: enumValues)
        }
        return DynamicGenerationSchema(name: name, description: description, properties: [])

    case "number", "integer", "boolean":
        return DynamicGenerationSchema(name: name, description: description, properties: [])

    case "array":
        let items = obj["items"]
        let inner = buildDynamicSchema(name: name + "Item", value: items)
        return DynamicGenerationSchema(arrayOf: inner)

    default:
        return DynamicGenerationSchema(name: name, description: description, properties: [])
    }
}

// MARK: - Errors

private enum BridgeError: Error {
    case invalidMessages(String)
    case jsonDecodeError(String)

    var localizedDescription: String {
        switch self {
        case .invalidMessages(let msg): return "Invalid messages: \(msg)"
        case .jsonDecodeError(let msg): return "JSON decode error: \(msg)"
        }
    }
}

/// Decode either the new wrapped `{ messages, tools }` payload or the legacy
/// bare `[Message]` array, so the bridge stays backward compatible with older
/// callers and tests.
private func decodeRequest(_ json: String) throws -> Request {
    let data = Data(json.utf8)
    if let request = try? JSONDecoder().decode(Request.self, from: data) {
        return request
    }
    if let messages = try? JSONDecoder().decode([Message].self, from: data) {
        return Request(messages: messages, tools: nil)
    }
    throw BridgeError.jsonDecodeError("Unable to decode request payload.")
}

// MARK: - C-exported functions

/// Returns true if Apple Intelligence on-device models are available on this device.
@_cdecl("aib_is_available")
public func aib_is_available() -> Bool {
    guard #available(macOS 26.0, *) else {
        return false
    }

    let model = SystemLanguageModel(guardrails: .default)
    return model.isAvailable
}

/// Performs a blocking (non-streaming) chat completion.
///
/// - Parameters:
///   - messagesJson: UTF-8 JSON array of `{"role": "...", "content": "..."}` objects.
///                  The last element must have role `"user"`.
///   - callback: Called exactly once. On success: `(result, nil)`.
///               On failure: `(nil, errorMessage)`.
///               The string pointers are only valid for the duration of the callback.
@_cdecl("aib_complete")
public func aib_complete(
    _ messagesJson: UnsafePointer<CChar>,
    _ callback: @Sendable @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
) {
    guard #available(macOS 26.0, *) else {
        unsupportedMacOSMessage.withCString { errPtr in
            callback(nil, errPtr)
        }
        return
    }

    let json = String(cString: messagesJson)
    let sema = DispatchSemaphore(value: 0)

    Task {
        defer { sema.signal() }
        do {
            let request = try decodeRequest(json)
            let (session, prompt) = try buildSession(from: request)
            let response = try await session.respond(to: prompt)
            dbg("resp", "non-streaming content=\"\(response.content)\"")
            response.content.withCString { ptr in
                callback(ptr, nil)
            }
        } catch {
            error.localizedDescription.withCString { errPtr in
                callback(nil, errPtr)
            }
        }
    }

    sema.wait()
}

/// Performs a streaming chat completion, invoking `callback` once per text delta.
///
/// - Parameters:
///   - messagesJson: UTF-8 JSON array of `{"role": "...", "content": "..."}` objects.
///                  The last element must have role `"user"`.
///   - callback: Called once per token chunk and once on completion.
///     - `chunk`:  The incremental text delta (nil when isDone is true or on error).
///     - `isDone`: True on the final call. After this the C# side should stop reading.
///     - `error`:  Non-nil only on the terminal call when an error occurred.
///     The string pointers are only valid for the duration of each callback invocation.
@_cdecl("aib_complete_streaming")
public func aib_complete_streaming(
    _ messagesJson: UnsafePointer<CChar>,
    _ callback: @Sendable @convention(c) (UnsafePointer<CChar>?, Bool, UnsafePointer<CChar>?) -> Void
) {
    guard #available(macOS 26.0, *) else {
        unsupportedMacOSMessage.withCString { errPtr in
            callback(nil, true, errPtr)
        }
        return
    }

    let json = String(cString: messagesJson)
    let sema = DispatchSemaphore(value: 0)

    Task {
        defer { sema.signal() }
        do {
            let request = try decodeRequest(json)
            let (session, prompt) = try buildSession(from: request)

            // FoundationModels streams cumulative snapshots, not incremental deltas.
            // We track the previous content length and extract only the new suffix.
            var previousLength = 0
            var fullContent = ""
            for try await snapshot in session.streamResponse(to: prompt) {
                let content = snapshot.content
                fullContent = content
                guard content.count > previousLength else { continue }
                let startIndex = content.index(content.startIndex, offsetBy: previousLength)
                let delta = String(content[startIndex...])
                delta.withCString { ptr in
                    callback(ptr, false, nil)
                }
                previousLength = content.count
            }
            dbg("resp", "streaming final content=\"\(fullContent)\"")

            // Terminal call — signals completion with no error
            callback(nil, true, nil)

        } catch {
            error.localizedDescription.withCString { errPtr in
                callback(nil, true, errPtr)
            }
        }
    }

    sema.wait()
}
