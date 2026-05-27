using System.Text.Json;
using Community.Microsoft.Extensions.AI.CoreML;
using Community.Microsoft.Extensions.AI.CoreML.Interop;
using Microsoft.Extensions.AI;

namespace Community.Microsoft.Extensions.AI.CoreML.Tests;

public class AppleIntelligenceChatClientTests
{
    [Fact]
    public void Constructor_ThrowsPlatformNotSupportedException_OnUnsupportedPlatform()
    {
        Assert.Throws<PlatformNotSupportedException>(
            () => new AppleIntelligenceChatClient(new FailingPlatformValidator()));
    }

    [Fact]
    public void Constructor_DoesNotThrow_OnSupportedPlatform()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());
        Assert.NotNull(client);
    }

    [Fact]
    public void GetService_ReturnsSelf_WhenRequestedTypeMatches()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());

        var result = client.GetService(typeof(AppleIntelligenceChatClient));

        Assert.Same(client, result);
    }

    [Fact]
    public void GetService_ReturnsSelf_WhenRequestedAsIChatClient()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());

        var result = client.GetService(typeof(IChatClient));

        Assert.Same(client, result);
    }

    [Fact]
    public void GetService_ReturnsNull_ForUnrelatedType()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());

        var result = client.GetService(typeof(string));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetResponseAsync_ReturnsAssistantText_FromBridge()
    {
        var bridge = new FakeBridge { CompletionResult = "bridge answer" };
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator(), bridge);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "What is AI?")]);

        Assert.Equal("bridge answer", response.Text);
        Assert.Equal("bridge answer", Assert.Single(response.Messages).Text);
        Assert.Equal(1, bridge.CompletionCallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_YieldsIncrementalChunks_FromBridge()
    {
        var bridge = new FakeBridge { StreamingChunks = ["Hel", "lo", "!"] };
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator(), bridge);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Say hello")]))
        {
            chunks.Add(update.Text);
        }

        Assert.Equal(["Hel", "lo", "!"], chunks);
        Assert.Equal(1, bridge.StreamingCallCount);
    }

    [Fact]
    public async Task GetResponseAsync_MergesInstructions_IntoSerializedSystemPayload()
    {
        var bridge = new FakeBridge { CompletionResult = "ok" };
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator(), bridge);

        await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "existing guidance"),
                new ChatMessage(ChatRole.User, "Prompt"),
            ],
            new ChatOptions { Instructions = "extra guidance" });

        using var document = JsonDocument.Parse(bridge.LastMessagesJson!);
        var messages = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());

        var systemContent = messages[0].GetProperty("content").GetString();
        Assert.Contains("existing guidance", systemContent);
        Assert.Contains("extra guidance", systemContent);
    }

    [Fact]
    public async Task GetResponseAsync_SurfacesBridgeErrors()
    {
        var bridge = new FakeBridge { CompletionException = new NotSupportedException("unsupported bridge") };
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator(), bridge);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "Prompt")]));

        Assert.Equal("unsupported bridge", exception.Message);
    }

    [Fact]
    public async Task GetResponseAsync_ThrowsObjectDisposedException_AfterDispose()
    {
        var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.GetResponseAsync([]));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsObjectDisposedException_AfterDispose()
    {
        var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () =>
            {
                await foreach (var _ in client.GetStreamingResponseAsync([])) { }
            });
    }

    private sealed class FakeBridge : IAppleIntelligenceBridge
    {
        public string? LastMessagesJson { get; private set; }
        public string CompletionResult { get; init; } = string.Empty;
        public Exception? CompletionException { get; init; }
        public IReadOnlyList<string> StreamingChunks { get; init; } = [];
        public Exception? StreamingException { get; init; }
        public int CompletionCallCount { get; private set; }
        public int StreamingCallCount { get; private set; }

        public bool IsAvailable() => true;

        public string Complete(string messagesJson)
        {
            LastMessagesJson = messagesJson;
            CompletionCallCount++;

            if (CompletionException is not null)
            {
                throw CompletionException;
            }

            return CompletionResult;
        }

        public async IAsyncEnumerable<string> CompleteStreaming(string messagesJson)
        {
            LastMessagesJson = messagesJson;
            StreamingCallCount++;

            if (StreamingException is not null)
            {
                throw StreamingException;
            }

            foreach (var chunk in StreamingChunks)
            {
                yield return chunk;
                await Task.Yield();
            }
        }
    }
}
