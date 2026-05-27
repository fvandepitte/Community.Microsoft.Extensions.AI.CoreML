namespace Community.Microsoft.Extensions.AI.CoreML.Interop;

/// <summary>
/// Abstraction over the Apple Intelligence native bridge.
/// </summary>
internal interface IAppleIntelligenceBridge
{
    /// <summary>Gets whether Apple Intelligence is currently available.</summary>
    bool IsAvailable();

    /// <summary>Performs a non-streaming chat completion.</summary>
    string Complete(string messagesJson);

    /// <summary>Performs a streaming chat completion.</summary>
    IAsyncEnumerable<string> CompleteStreaming(string messagesJson);
}
