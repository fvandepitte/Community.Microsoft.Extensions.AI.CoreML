namespace Community.Microsoft.Extensions.AI.CoreML;

/// <summary>
/// Abstracts the platform check so it can be replaced in tests.
/// </summary>
internal interface IPlatformValidator
{
    /// <summary>
    /// Throws <see cref="PlatformNotSupportedException"/> if the current platform
    /// is not supported.
    /// </summary>
    void ThrowIfNotSupported();
}
