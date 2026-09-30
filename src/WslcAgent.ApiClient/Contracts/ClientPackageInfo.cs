namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>GET /api/v1/clients/{platform}</c>: the native client installer
/// the agent can hand out and the version it installs.
/// </summary>
/// <param name="Platform"><c>windows</c> or <c>android</c>.</param>
/// <param name="Filename">Name of the package file (<c>wslc-ai-client.msi</c> / <c>.apk</c>).</param>
/// <param name="Available">True when the package file is on the agent.</param>
/// <param name="Version">Display version the package installs (MSI ProductVersion / APK versionName); empty when unavailable.</param>
/// <param name="Build">Integer build (APK versionCode; 0 for an MSI, whose ProductVersion is the whole identity).</param>
/// <param name="Error">Why the package is unavailable, for the user.</param>
public sealed record ClientPackageInfo(
    string Platform,
    string Filename,
    bool Available,
    string Version,
    int Build,
    string Error)
{
    /// <summary>
    /// True when this package is newer than the installed client: the display
    /// version decides, and only equal versions fall back to the build number.
    /// </summary>
    public bool IsNewerThan(string? installedVersion, int installedBuild)
    {
        if (string.IsNullOrWhiteSpace(Version) && Build <= 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Version))
        {
            var comparison = CompareDisplayVersion(Version, installedVersion);
            if (comparison != 0)
            {
                return comparison > 0;
            }
        }

        return Build > Math.Max(installedBuild, 0);
    }

    /// <summary>Numeric, part by part comparison of dotted versions; missing parts count as zero.</summary>
    public static int CompareDisplayVersion(string? left, string? right)
    {
        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var av = i < a.Count ? a[i] : 0;
            var bv = i < b.Count ? b[i] : 0;
            if (av != bv)
            {
                return av.CompareTo(bv);
            }
        }

        return 0;
    }

    private static List<int> Parts(string? value)
    {
        var parts = new List<int>();
        foreach (var piece in (value ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(piece.TakeWhile(char.IsDigit).ToArray());
            parts.Add(int.TryParse(digits, out var n) ? n : 0);
        }

        return parts;
    }
}
