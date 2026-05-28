using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Community.Microsoft.Extensions.AI.CoreML;

/// <summary>
/// Dependency-injection helpers for registering the Apple Intelligence
/// <see cref="IChatClient"/> implementation.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AppleIntelligenceChatClient"/> as a singleton
    /// <see cref="IChatClient"/> and returns a <see cref="ChatClientBuilder"/>
    /// so additional middleware (function invocation, telemetry, caching, …)
    /// can be layered on top.
    /// </summary>
    /// <param name="services">The service collection to add the registration to.</param>
    /// <returns>A <see cref="ChatClientBuilder"/> for further configuration.</returns>
    /// <remarks>
    /// The underlying client is only functional on macOS 26+ on Apple Silicon.
    /// Construction will throw <see cref="PlatformNotSupportedException"/> on
    /// any other platform — defer registration to a macOS-only code path if you
    /// need to compose a multi-platform host.
    /// </remarks>
    public static ChatClientBuilder AddAppleIntelligenceChatClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddChatClient(static _ => (IChatClient)new AppleIntelligenceChatClient());
    }

    /// <summary>
    /// Registers a keyed <see cref="AppleIntelligenceChatClient"/> as an
    /// <see cref="IChatClient"/> singleton and returns a
    /// <see cref="ChatClientBuilder"/> for middleware layering.
    /// </summary>
    /// <param name="services">The service collection to add the registration to.</param>
    /// <param name="serviceKey">The DI service key.</param>
    public static ChatClientBuilder AddKeyedAppleIntelligenceChatClient(
        this IServiceCollection services,
        object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddKeyedChatClient(serviceKey, static _ => (IChatClient)new AppleIntelligenceChatClient());
    }
}
