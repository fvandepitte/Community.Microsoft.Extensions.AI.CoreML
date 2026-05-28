using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Community.Microsoft.Extensions.AI.CoreML.Interop;

namespace Community.Microsoft.Extensions.AI.CoreML;

/// <summary>
/// An <see cref="IChatClient"/> that uses Apple Intelligence on-device Foundation Models
/// via P/Invoke. Requires macOS 26+ on Apple Silicon (arm64).
/// </summary>
public sealed class AppleIntelligenceChatClient : IChatClient
{
    private const string ModelIdValue = "apple-intelligence";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAppleIntelligenceBridge _bridge;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AppleIntelligenceChatClient"/>.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when not running on macOS arm64.
    /// </exception>
    public AppleIntelligenceChatClient()
        : this(PlatformGuard.Default) { }

    /// <summary>Internal constructor that accepts a validator seam for testing.</summary>
    internal AppleIntelligenceChatClient(IPlatformValidator validator)
        : this(validator, new NativeAppleIntelligenceBridge())
    {
    }

    /// <summary>Internal constructor that accepts bridge and platform seams for testing.</summary>
    internal AppleIntelligenceChatClient(IPlatformValidator validator, IAppleIntelligenceBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(bridge);

        validator.ThrowIfNotSupported();
        _bridge = bridge;
    }

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var responseText = _bridge.Complete(SerializeMessages(messages, options));
                return BuildChatResponse(responseText, options);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        var hasTools = options?.Tools is { Count: > 0 };
        var buffer = hasTools ? new StringBuilder() : null;

        await foreach (var chunk in _bridge
            .CompleteStreaming(SerializeMessages(messages, options))
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            if (buffer is not null)
            {
                // Buffer when tools are configured so we don't leak raw tool-call
                // JSON to the caller before we know whether it is a tool call.
                buffer.Append(chunk);
                continue;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { ModelId = ModelIdValue };
        }

        if (buffer is null)
        {
            yield break;
        }

        var responseText = buffer.ToString();
        if (TryParseToolCall(responseText, options, out var call) && call is not null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { call })
            {
                ModelId = ModelIdValue,
                FinishReason = ChatFinishReason.ToolCalls,
            };
        }
        else if (!string.IsNullOrEmpty(responseText))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText) { ModelId = ModelIdValue };
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? key = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }

    private static string SerializeMessages(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        var serializedMessages = new List<BridgeMessage>();
        var systemParts = new List<string>();

        AddInstructions(systemParts, options?.Instructions);
        AddToolInstructions(systemParts, options);

        foreach (var message in messageList)
        {
            if (message.Role == ChatRole.System)
            {
                AddText(systemParts, message.Text);
                continue;
            }

            if (message.Role == ChatRole.Assistant)
            {
                var toolCalls = message.Contents.OfType<FunctionCallContent>().ToList();
                if (toolCalls.Count > 0)
                {
                    var calls = new List<BridgeToolCall>(toolCalls.Count);
                    foreach (var fc in toolCalls)
                    {
                        calls.Add(new BridgeToolCall(
                            fc.CallId ?? Guid.NewGuid().ToString("N"),
                            fc.Name,
                            SerializeFunctionArguments(fc.Arguments)));
                    }
                    serializedMessages.Add(new BridgeMessage(
                        Role: ChatRole.Assistant.Value,
                        ToolCalls: calls));
                    continue;
                }

                var text = message.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    serializedMessages.Add(new BridgeMessage(ChatRole.Assistant.Value, Content: text));
                }
                continue;
            }

            if (message.Role == ChatRole.Tool)
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionResultContent result)
                    {
                        serializedMessages.Add(new BridgeMessage(
                            Role: ChatRole.Tool.Value,
                            Content: result.Result?.ToString() ?? string.Empty,
                            ToolCallId: result.CallId,
                            ToolName: ResolveToolName(messageList, result.CallId)));
                    }
                }
                continue;
            }

            if (message.Role == ChatRole.User)
            {
                var text = message.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    serializedMessages.Add(new BridgeMessage(ChatRole.User.Value, Content: text));
                }
            }
        }

        if (systemParts.Count > 0)
        {
            serializedMessages.Insert(0, new BridgeMessage(ChatRole.System.Value, Content: string.Join("\n\n", systemParts)));
        }

        var toolPayload = BuildToolPayload(options);
        var payload = new BridgeRequest(serializedMessages, toolPayload);
        return JsonSerializer.Serialize(payload, s_jsonOptions);
    }

    private static string SerializeFunctionArguments(IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        if (arguments is null)
        {
            return "{}";
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in arguments)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static string? ResolveToolName(IReadOnlyList<ChatMessage> messages, string? callId)
    {
        if (string.IsNullOrEmpty(callId))
        {
            return null;
        }
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent call &&
                    string.Equals(call.CallId, callId, StringComparison.Ordinal))
                {
                    return call.Name;
                }
            }
        }
        return null;
    }

    private static List<BridgeTool>? BuildToolPayload(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return null;
        }

        var result = new List<BridgeTool>();
        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunction function)
            {
                continue;
            }

            JsonElement? parameters = null;
            try
            {
                if (function.JsonSchema.ValueKind != JsonValueKind.Undefined)
                {
                    parameters = function.JsonSchema.Clone();
                }
            }
            catch
            {
                parameters = null;
            }

            result.Add(new BridgeTool(function.Name, function.Description, parameters));
        }

        return result.Count == 0 ? null : result;
    }

    private static void AddInstructions(List<string> systemParts, object? instructions)
    {
        switch (instructions)
        {
            case null:
                return;

            case string instruction:
                AddText(systemParts, instruction);
                return;

            case IEnumerable<string> enumerable:
                foreach (var instruction in enumerable)
                {
                    AddText(systemParts, instruction);
                }
                return;

            case IEnumerable<object?> enumerable:
                foreach (var instruction in enumerable)
                {
                    if (instruction is string text)
                    {
                        AddText(systemParts, text);
                    }
                }
                return;
        }
    }

    private static void AddText(List<string> parts, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(text);
        }
    }

    private static void AddToolInstructions(List<string> systemParts, ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("You have access to the following tools. When you need to call one, respond ONLY with the JSON shown below (no other text before or after).");
        builder.AppendLine();
        builder.AppendLine("Tool call format:");
        builder.AppendLine("{\"tool_calls\":[{\"id\":\"call_<unique>\",\"type\":\"function\",\"function\":{\"name\":\"<tool_name>\",\"arguments\":\"<json_string_of_args>\"}}]}");
        builder.AppendLine();
        builder.AppendLine("Replace <unique> with a short identifier, <tool_name> with the tool name, and <json_string_of_args> with the arguments as a JSON-encoded string.");
        builder.AppendLine();

        if (options.ToolMode is not null)
        {
            builder.Append("Tool mode: ");
            builder.AppendLine(options.ToolMode.ToString());
        }

        builder.AppendLine("Available tools:");
        foreach (var tool in options.Tools)
        {
            builder.Append("- ");
            builder.Append(GetToolName(tool));

            var description = GetStringProperty(tool, "Description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append(": ");
                builder.Append(description);
            }

            builder.AppendLine();

            AppendParameterSummary(builder, tool);
        }

        AddText(systemParts, builder.ToString().TrimEnd());
    }

    private static void AppendParameterSummary(StringBuilder builder, AITool tool)
    {
        if (tool is not AIFunction function)
        {
            return;
        }

        try
        {
            var schema = function.JsonSchema;
            if (schema.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("properties", out var props) ||
                props.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var requiredSet = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in required.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                    {
                        requiredSet.Add(name);
                    }
                }
            }

            foreach (var prop in props.EnumerateObject())
            {
                builder.Append("    - ");
                builder.Append(prop.Name);

                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("type", out var typeEl) &&
                    typeEl.ValueKind == JsonValueKind.String)
                {
                    builder.Append(" (");
                    builder.Append(typeEl.GetString());
                    builder.Append(requiredSet.Contains(prop.Name) ? ", required" : ", optional");
                    builder.Append(')');
                }
                else if (requiredSet.Contains(prop.Name))
                {
                    builder.Append(" (required)");
                }

                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("description", out var descEl) &&
                    descEl.ValueKind == JsonValueKind.String)
                {
                    builder.Append(": ");
                    builder.Append(descEl.GetString());
                }

                builder.AppendLine();
            }

            builder.Append("    Use these EXACT parameter names in arguments. Do not invent or rename them.");
            builder.AppendLine();
        }
        catch
        {
            // Best-effort enrichment; ignore schema introspection failures.
        }
    }

    private static string GetToolName(AITool tool)
        => GetStringProperty(tool, "Name")
            ?? GetStringProperty(tool, "FunctionName")
            ?? tool.GetType().Name;

    private static string? GetStringProperty(object instance, string propertyName)
        => GetPropertyValue(instance, propertyName)?.ToString();

    private static object? GetPropertyValue(object instance, string propertyName)
        => instance.GetType().GetProperty(propertyName)?.GetValue(instance);

    private static ChatResponse BuildChatResponse(string responseText, ChatOptions? options)
    {
        if (TryParseToolCall(responseText, options, out var call) && call is not null)
        {
            // Suppress the raw tool-call JSON text — only return the structured
            // FunctionCallContent so the agent loop doesn't leak JSON to the user.
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { call }))
            {
                ModelId = ModelIdValue,
                FinishReason = ChatFinishReason.ToolCalls,
            };
        }

        // If the model emitted text that *looks* like a tool-call attempt but
        // failed validation (e.g. missing required args, malformed JSON), don't
        // leak the raw JSON to the user. Return a benign placeholder instead.
        if (LooksLikeToolCallAttempt(responseText, options))
        {
            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                "I'm not able to answer that with the information I have."))
            {
                ModelId = ModelIdValue,
            };
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
        {
            ModelId = ModelIdValue,
        };
    }

    private static bool LooksLikeToolCallAttempt(string responseText, ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return false;
        }

        return responseText.Contains("\"tool_calls\"", StringComparison.Ordinal)
            || responseText.Contains("```json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseToolCall(string responseText, ChatOptions? options, out FunctionCallContent? call)
    {
        call = null;

        if (string.IsNullOrWhiteSpace(responseText) || options?.Tools is not { Count: > 0 } tools)
        {
            return false;
        }

        // 1. Preferred: OpenAI-style {"tool_calls":[...]} JSON emitted by the model.
        if (TryParseToolCallsJson(responseText, options, out var jsonCall) && jsonCall is not null)
        {
            call = jsonCall;
            return true;
        }

        // 2. Fallback: `name(args)` textual call form.
        foreach (var tool in tools)
        {
            if (tool is not AIFunction function)
            {
                continue;
            }

            var pattern = Regex.Escape(function.Name) + @"\s*\((?<args>[^)]*)\)";
            var match = Regex.Match(responseText, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var arguments = ParseArguments(match.Groups["args"].Value, function);
            call = new FunctionCallContent(
                callId: Guid.NewGuid().ToString("N"),
                name: function.Name,
                arguments: arguments);
            return true;
        }

        return false;
    }

    private static bool TryParseToolCallsJson(string responseText, ChatOptions? options, out FunctionCallContent? call)
    {
        call = null;

        foreach (var candidate in ExtractJsonCandidates(responseText))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                if (!document.RootElement.TryGetProperty("tool_calls", out var toolCallsElement)) continue;
                if (toolCallsElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var entry in toolCallsElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;

                    string? name = null;
                    IDictionary<string, object?> arguments;

                    if (entry.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                    {
                        // OpenAI canonical shape: { function: { name, arguments } }
                        if (!fn.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String) continue;
                        name = nameEl.GetString();
                        arguments = ParseToolCallArguments(fn);
                    }
                    else if (entry.TryGetProperty("function", out var fnStr) && fnStr.ValueKind == JsonValueKind.String)
                    {
                        // Common model shorthand: { function: "Name", arguments: "{...}" }
                        name = fnStr.GetString();
                        arguments = ParseToolCallArguments(entry);
                    }
                    else if (entry.TryGetProperty("name", out var topName) && topName.ValueKind == JsonValueKind.String)
                    {
                        // Flat shape: { name, arguments }
                        name = topName.GetString();
                        arguments = ParseToolCallArguments(entry);
                    }
                    else
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(name)) continue;

                    var id = entry.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()!
                        : Guid.NewGuid().ToString("N");

                    if (!ToolCallHasRequiredArguments(name!, arguments, options))
                    {
                        // Model emitted a tool call missing required parameters.
                        // Treat as a parse failure so we don't crash the agent loop.
                        continue;
                    }

                    call = new FunctionCallContent(id, name!, arguments);
                    return true;
                }
            }
            catch (JsonException)
            {
                // Try next candidate.
            }
        }

        // Last-resort: response clearly attempted a tool call but emitted malformed
        // JSON. Recover the function name and arguments with a regex so we never
        // leak the raw JSON to the caller.
        if (responseText.Contains("\"tool_calls\"", StringComparison.Ordinal))
        {
            var nameMatch = Regex.Match(
                responseText,
                "\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"",
                RegexOptions.CultureInvariant);
            if (nameMatch.Success)
            {
                var argsMatch = Regex.Match(
                    responseText,
                    "\"arguments\"\\s*:\\s*\"(?<args>(?:\\\\.|[^\"\\\\])*)\"",
                    RegexOptions.CultureInvariant);

                var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (argsMatch.Success)
                {
                    var rawArgs = Regex.Unescape(argsMatch.Groups["args"].Value);
                    PopulateArgumentsFromMaybeJson(rawArgs, arguments);
                }

                // If we still have nothing useful, try extracting known parameter
                // values directly from the response text using the tool's schema.
                if (arguments.Count == 0 || (arguments.Count == 1 && arguments.ContainsKey("value")))
                {
                    ExtractArgumentsBySchema(responseText, nameMatch.Groups["name"].Value, options, arguments);
                }

                if (!ToolCallHasRequiredArguments(nameMatch.Groups["name"].Value, arguments, options))
                {
                    return false;
                }

                call = new FunctionCallContent(
                    Guid.NewGuid().ToString("N"),
                    nameMatch.Groups["name"].Value,
                    arguments);
                return true;
            }
        }

        return false;
    }

    private static void PopulateArgumentsFromMaybeJson(string rawArgs, IDictionary<string, object?> arguments)
    {
        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            return;
        }

        if (TryParseJsonObjectInto(rawArgs, arguments))
        {
            return;
        }

        // Model frequently truncates trailing '}' characters. Append a few and retry.
        for (var i = 1; i <= 4; i++)
        {
            if (TryParseJsonObjectInto(rawArgs + new string('}', i), arguments))
            {
                return;
            }
        }

        arguments["value"] = rawArgs;
    }

    private static bool TryParseJsonObjectInto(string json, IDictionary<string, object?> arguments)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                arguments[property.Name] = ConvertJsonElement(property.Value);
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ExtractArgumentsBySchema(
        string responseText,
        string toolName,
        ChatOptions? options,
        IDictionary<string, object?> arguments)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return;
        }

        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunction function ||
                !string.Equals(function.Name, toolName, StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement schema;
            try
            {
                schema = function.JsonSchema;
            }
            catch
            {
                return;
            }

            if (schema.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("properties", out var props) ||
                props.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var prop in props.EnumerateObject())
            {
                if (arguments.ContainsKey(prop.Name))
                {
                    continue;
                }

                // Look for "<param>":"<value>" anywhere in the text — escaped
                // quotes (\") permitted to handle JSON-in-JSON-string forms.
                var pattern = "\\\\?\"" + Regex.Escape(prop.Name) + "\\\\?\"\\s*:\\s*\\\\?\"(?<v>(?:\\\\.|[^\"\\\\])*)\\\\?\"";
                var match = Regex.Match(responseText, pattern, RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    var value = Regex.Unescape(match.Groups["v"].Value);
                    arguments[prop.Name] = value;
                    arguments.Remove("value");
                }
            }

            return;
        }
    }

    private static bool ToolCallHasRequiredArguments(
        string toolName,
        IDictionary<string, object?> arguments,
        ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
        {
            return true;
        }

        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunction function ||
                !string.Equals(function.Name, toolName, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var schema = function.JsonSchema;
                if (schema.ValueKind != JsonValueKind.Object ||
                    !schema.TryGetProperty("required", out var required) ||
                    required.ValueKind != JsonValueKind.Array)
                {
                    return true;
                }

                foreach (var item in required.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        item.GetString() is { Length: > 0 } name &&
                        !arguments.ContainsKey(name))
                    {
                        return false;
                    }
                }
            }
            catch
            {
                // If schema inspection fails, allow the call through.
            }

            return true;
        }

        return true;
    }

    private static IEnumerable<string> ExtractJsonCandidates(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0)
        {
            yield return trimmed;
        }

        // Markdown code blocks ```json ... ``` or ``` ... ```
        var search = 0;
        while (search < text.Length)
        {
            var start = text.IndexOf("```", search, StringComparison.Ordinal);
            if (start < 0) break;
            var end = text.IndexOf("```", start + 3, StringComparison.Ordinal);
            if (end < 0) break;
            var block = text[(start + 3)..end].Trim();
            if (block.StartsWith("json\n", StringComparison.Ordinal))
            {
                block = block[5..];
            }
            else if (block.StartsWith("json\r\n", StringComparison.Ordinal))
            {
                block = block[6..];
            }
            yield return block;
            search = end + 3;
        }

        // Balanced JSON object starting at {"tool_calls"
        var anchor = text.IndexOf("{\"tool_calls\"", StringComparison.Ordinal);
        if (anchor >= 0)
        {
            var depth = 0;
            for (var i = anchor; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[anchor..(i + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static IDictionary<string, object?> ParseToolCallArguments(JsonElement functionElement)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!functionElement.TryGetProperty("arguments", out var argsEl))
        {
            return result;
        }

        JsonElement argsObject;
        if (argsEl.ValueKind == JsonValueKind.String)
        {
            var raw = argsEl.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return result;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                argsObject = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                result["value"] = raw;
                return result;
            }
        }
        else
        {
            argsObject = argsEl;
        }

        if (argsObject.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in argsObject.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }
        return result;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText(),
    };

    private static IDictionary<string, object?> ParseArguments(string rawArguments, AIFunction function)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var parameterNames = GetParameterNames(function);

        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return result;
        }

        var parts = SplitArguments(rawArguments);
        var positionalIndex = 0;

        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            var eq = token.IndexOf('=');
            var colon = token.IndexOf(':');
            var separator = eq >= 0 && (colon < 0 || eq < colon) ? eq : colon;

            if (separator > 0)
            {
                var name = token[..separator].Trim().Trim('"');
                var value = UnquoteValue(token[(separator + 1)..].Trim());
                if (name.Length > 0)
                {
                    result[name] = value;
                    continue;
                }
            }

            var positionalName = positionalIndex < parameterNames.Count
                ? parameterNames[positionalIndex]
                : $"arg{positionalIndex}";
            result[positionalName] = UnquoteValue(token);
            positionalIndex++;
        }

        return result;
    }

    private static IReadOnlyList<string> GetParameterNames(AIFunction function)
    {
        try
        {
            var schema = function.JsonSchema;
            if (schema.ValueKind == JsonValueKind.Object &&
                schema.TryGetProperty("properties", out var props) &&
                props.ValueKind == JsonValueKind.Object)
            {
                var names = new List<string>();
                foreach (var prop in props.EnumerateObject())
                {
                    names.Add(prop.Name);
                }
                return names;
            }
        }
        catch
        {
            // Fall through to empty list.
        }

        return Array.Empty<string>();
    }

    private static List<string> SplitArguments(string raw)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static string UnquoteValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }
        return value;
    }

    private sealed record BridgeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content = null,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<BridgeToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
        [property: JsonPropertyName("tool_name")] string? ToolName = null);

    private sealed record BridgeToolCall(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    private sealed record BridgeTool(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("parameters")] JsonElement? Parameters);

    private sealed record BridgeRequest(
        [property: JsonPropertyName("messages")] IReadOnlyList<BridgeMessage> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<BridgeTool>? Tools);
}
