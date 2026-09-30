using Microsoft.Extensions.Options;
using WslcAgent.Server.Updates;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.ClientPackages;

/// <summary>
/// Where the installers the agent hands out live: the clients' MSI and APK for
/// the clients to update themselves and to be downloaded, and the agent's own
/// MSI for the agent to update itself. One folder for all of them, so one copy
/// puts all three where they are looked for.
/// </summary>
public sealed class PackageFolders(IOptions<WslcOptions> options, AgentUpdateSettingsStore settings, IHostEnvironment environment, ILogger<PackageFolders> logger)
{
    /// <summary>The package folder when neither Settings nor the installer names one.</summary>
    public const string DefaultFolder = @"C:\Berpiztu\wslc-ai-agent";

    /// <summary>
    /// The package folder with nothing chosen in Settings:
    /// <c>Wslc:ClientPackagesPath</c>, which the installer writes with the
    /// folder its wizard asked for (<c>wslc-ai-agent.ini</c>), else
    /// <see cref="DefaultFolder"/>.
    /// </summary>
    public string Automatic { get; } = string.IsNullOrWhiteSpace(options.Value.ClientPackagesPath)
        ? DefaultFolder
        : Environment.ExpandEnvironmentVariables(options.Value.ClientPackagesPath);

    /// <summary>The package folder: the one chosen in Settings → Update, else <see cref="Automatic"/>.</summary>
    public string Folder => settings.Get().PackageFolder is { Length: > 0 } chosen
        ? Environment.ExpandEnvironmentVariables(chosen)
        : Automatic;

    /// <summary>The file by that name in the first folder that has it, or null.</summary>
    public string? Locate(string filename) =>
        Folders().Select(folder => Path.Combine(folder, filename)).FirstOrDefault(File.Exists);

    /// <summary>
    /// Creates the package folder when it is missing, so whoever installed the
    /// agent finds it waiting for the installers. A folder that cannot be
    /// created is reported, not fatal: the agent runs, and offers no update.
    /// </summary>
    public void EnsureExists()
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("could not create the package folder {Folder}: {Message}", Folder, ex.Message);
        }
    }

    /// <summary>
    /// Where a package may live, first match wins: the package folder, then the
    /// agent's own <c>dist</c>, which only an agent run from its checkout has
    /// and which the build scripts write to.
    /// </summary>
    private IEnumerable<string> Folders()
    {
        yield return Folder;
        yield return Path.Combine(environment.ContentRootPath, "dist");
    }
}
