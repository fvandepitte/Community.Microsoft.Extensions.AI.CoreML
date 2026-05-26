namespace Community.Microsoft.Extensions.AI.CoreML.Tests;

/// <summary>
/// Fake validator that always passes — simulates running on macOS arm64.
/// </summary>
internal sealed class PassingPlatformValidator : IPlatformValidator
{
    public void ThrowIfNotSupported() { }
}

/// <summary>
/// Fake validator that always throws — simulates running on an unsupported platform.
/// </summary>
internal sealed class FailingPlatformValidator : IPlatformValidator
{
    public void ThrowIfNotSupported() =>
        throw new PlatformNotSupportedException("Simulated unsupported platform.");
}
