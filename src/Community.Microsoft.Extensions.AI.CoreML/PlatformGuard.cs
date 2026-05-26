using System.Runtime.InteropServices;

namespace Community.Microsoft.Extensions.AI.CoreML;

internal sealed class PlatformGuard : IPlatformValidator
{
    /// <summary>Shared instance used by the default constructor.</summary>
    internal static readonly IPlatformValidator Default = new PlatformGuard();

    /// <inheritdoc/>
    public void ThrowIfNotSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.OSArchitecture != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException(
                $"{nameof(AppleIntelligenceChatClient)} requires macOS on Apple Silicon (arm64). " +
                $"Current platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}).");
        }
    }
}
