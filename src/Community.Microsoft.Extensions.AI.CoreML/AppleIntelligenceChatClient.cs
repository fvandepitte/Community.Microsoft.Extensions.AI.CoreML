using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Community.Microsoft.Extensions.AI.CoreML.Interop;

namespace Community.Microsoft.Extensions.AI.CoreML;

/// <summary>
/// An <see cref="IChatClient"/> that uses Apple Intelligence on-device Foundation Models
/// via P/Invoke. Requires macOS 26+ on Apple Silicon (arm64).
/// </summary>
public sealed class AppleIntelligenceChatClient : IChatClient
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = null,
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
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
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

        await foreach (var chunk in _bridge
            .CompleteStreaming(SerializeMessages(messages, options))
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
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
        var serializedMessages = new List<BridgeMessage>();
        var systemParts = new List<string>();

        AddInstructions(systemParts, options?.Instructions);

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                AddText(systemParts, message.Text);
                continue;
            }

            if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant)
            {
                continue;
            }

            serializedMessages.Add(new BridgeMessage(message.Role.Value, message.Text ?? string.Empty));
        }

        if (systemParts.Count > 0)
        {
            serializedMessages.Insert(0, new BridgeMessage(ChatRole.System.Value, string.Join("\n\n", systemParts)));
        }

        return JsonSerializer.Serialize(serializedMessages, s_jsonOptions);
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

    private sealed record BridgeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
