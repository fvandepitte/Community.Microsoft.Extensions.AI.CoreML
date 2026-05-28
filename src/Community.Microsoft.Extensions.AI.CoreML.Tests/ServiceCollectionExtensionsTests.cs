using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Community.Microsoft.Extensions.AI.CoreML.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAppleIntelligenceChatClient_ThrowsOnNullServices()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddAppleIntelligenceChatClient(null!));
    }

    [Fact]
    public void AddKeyedAppleIntelligenceChatClient_ThrowsOnNullServices()
    {
        Assert.Throws<ArgumentNullException>(
            () => ServiceCollectionExtensions.AddKeyedAppleIntelligenceChatClient(null!, "key"));
    }

    [Fact]
    public void AddAppleIntelligenceChatClient_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddAppleIntelligenceChatClient();

        Assert.NotNull(builder);
    }

    [Fact]
    public void AddAppleIntelligenceChatClient_RegistersIChatClient()
    {
        var services = new ServiceCollection();

        services.AddAppleIntelligenceChatClient();

        Assert.Contains(services, sd => sd.ServiceType == typeof(IChatClient));
    }

    [Fact]
    public void AddKeyedAppleIntelligenceChatClient_RegistersKeyedIChatClient()
    {
        var services = new ServiceCollection();

        services.AddKeyedAppleIntelligenceChatClient("apple");

        Assert.Contains(
            services,
            sd => sd.ServiceType == typeof(IChatClient)
                && sd.IsKeyedService
                && Equals(sd.ServiceKey, "apple"));
    }
}
