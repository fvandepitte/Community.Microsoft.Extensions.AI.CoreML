using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Extensions.AI;

namespace Community.Microsoft.Extensions.AI.CoreML.Tests;

public class AppleIntelligenceChatClientTests
{
    // -------------------------------------------------------------------------
    // Platform guard
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // GetService
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Not-yet-implemented bridge
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetResponseAsync_ThrowsNotImplementedException_WhenBridgeNotReady()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());

        await Assert.ThrowsAsync<NotImplementedException>(
            () => client.GetResponseAsync([]));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ThrowsNotImplementedException_WhenBridgeNotReady()
    {
        using var client = new AppleIntelligenceChatClient(new PassingPlatformValidator());

        await Assert.ThrowsAsync<NotImplementedException>(
            async () =>
            {
                await foreach (var _ in client.GetStreamingResponseAsync([])) { }
            });
    }
}
