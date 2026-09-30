using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.ClientPackages;

/// <summary>The native client installers (Windows MSI, Android APK) the agent hands out.</summary>
public interface IClientPackageService
{
    /// <summary>Metadata for <paramref name="platform"/> (<c>windows</c> / <c>android</c>); throws <see cref="ArgumentException"/> for any other value.</summary>
    ClientPackageInfo Describe(string platform);

    /// <summary>Full path of the package file, or null when it is not on this agent.</summary>
    string? Locate(string platform);

    /// <summary>MIME type the package is served with.</summary>
    string MediaType(string platform);
}
