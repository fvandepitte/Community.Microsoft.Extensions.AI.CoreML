using Microsoft.Extensions.AI;

namespace Community.Microsoft.Extensions.AI.CoreML;

/// <summary>
/// An <see cref="IChatClient"/> that uses Apple Intelligence on-device Foundation Models
/// via P/Invoke. Requires macOS 26+ on Apple Silicon (arm64).
/// </summary>
public sealed class AppleIntelligenceChatClient : IChatClient
{
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
    {
        validator.ThrowIfNotSupported();
    }

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException("Native bridge not yet implemented.");
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotImplementedException("Native bridge not yet implemented.");
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? key = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
    }
}
