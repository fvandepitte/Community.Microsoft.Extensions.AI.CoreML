using Community.Microsoft.Extensions.AI.CoreML.Interop;

namespace Community.Microsoft.Extensions.AI.CoreML.Tests;

internal sealed class FakeAppleIntelligenceBridge : IAppleIntelligenceBridge
{
    public bool IsAvailableValue { get; set; } = true;

    public string CompletionResult { get; set; } = "fake completion";

    public string? LastMessagesJson { get; private set; }

    public IReadOnlyList<string> StreamingChunks { get; set; } = [];

    public bool CompleteCalled { get; private set; }

    public bool StreamingCalled { get; private set; }

    public bool IsAvailable() => IsAvailableValue;

    public string Complete(string messagesJson)
    {
        CompleteCalled = true;
        LastMessagesJson = messagesJson;
        return CompletionResult;
    }

    public async IAsyncEnumerable<string> CompleteStreaming(string messagesJson)
    {
        StreamingCalled = true;
        LastMessagesJson = messagesJson;

        foreach (var chunk in StreamingChunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
