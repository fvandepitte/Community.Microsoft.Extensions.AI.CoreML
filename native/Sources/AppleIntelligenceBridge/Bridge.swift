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

private struct Message: Decodable {
    let role: String
    let content: String
}

private let unsupportedMacOSMessage = "Apple Intelligence requires macOS 26.0 or newer."

// MARK: - Session builder

/// Builds a LanguageModelSession from a decoded message list and extracts the
/// final user prompt. The Transcript is pre-loaded with history so the model
/// has full context without re-inference.
///
/// Message role mapping:
///   "system"    → Transcript.Instructions
///   "user"      → Transcript.Prompt       (history entries)
///   "assistant" → Transcript.Response     (history entries)
///   last "user" → returned as finalPrompt (sent via respond / streamResponse)
@available(macOS 26.0, *)
private func buildSession(from messages: [Message]) throws -> (session: LanguageModelSession, prompt: String) {
    guard let last = messages.last, last.role == "user" else {
        throw BridgeError.invalidMessages("The last message must have role 'user'.")
    }

    let history = messages.dropLast()
    let model = SystemLanguageModel(guardrails: .default)
    var entries: [Transcript.Entry] = []

    // System message → Instructions (must be first entry if present)
    if let sys = messages.first(where: { $0.role == "system" }) {
        let instr = Transcript.Instructions(
            segments: [.text(Transcript.TextSegment(content: sys.content))],
            toolDefinitions: []
        )
        entries.append(.instructions(instr))
    }

    // Prior conversation turns
    for msg in history where msg.role != "system" {
        switch msg.role {
        case "user":
            let prompt = Transcript.Prompt(
                segments: [.text(Transcript.TextSegment(content: msg.content))],
                options: GenerationOptions()
            )
            entries.append(.prompt(prompt))

        case "assistant":
            let response = Transcript.Response(
                assetIDs: [],
                segments: [.text(Transcript.TextSegment(content: msg.content))]
            )
            entries.append(.response(response))

        default:
            break
        }
    }

    let session = entries.isEmpty
        ? LanguageModelSession(model: model)
        : LanguageModelSession(model: model, transcript: Transcript(entries: entries))

    return (session, last.content)
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
            let messages = try JSONDecoder().decode([Message].self, from: Data(json.utf8))
            let (session, prompt) = try buildSession(from: messages)
            let response = try await session.respond(to: prompt)
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
            let messages = try JSONDecoder().decode([Message].self, from: Data(json.utf8))
            let (session, prompt) = try buildSession(from: messages)

            // FoundationModels streams cumulative snapshots, not incremental deltas.
            // We track the previous content length and extract only the new suffix.
            var previousLength = 0
            for try await snapshot in session.streamResponse(to: prompt) {
                let content = snapshot.content
                guard content.count > previousLength else { continue }
                let startIndex = content.index(content.startIndex, offsetBy: previousLength)
                let delta = String(content[startIndex...])
                delta.withCString { ptr in
                    callback(ptr, false, nil)
                }
                previousLength = content.count
            }

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
