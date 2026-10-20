using System.Text.Json;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// This machine's <c>wslc</c> as it names itself, <c>version --format json</c>:
/// <c>{"Client":{"Version":"3.0.4.0"}}</c>. Read wherever the agent does
/// something differently before and after a release of it.
/// </summary>
public static class WslcVersion
{
    /// <summary>What the CLI says its version is; empty when it says nothing readable.</summary>
    public static async Task<string> ReadAsync(IWslcRunner wslc, CancellationToken cancellationToken)
    {
        var result = await wslc.RunAsync(["version", "--format", "json"], cancellationToken: cancellationToken);
        var row = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        return row.ValueKind == JsonValueKind.Object && row.TryGetProperty("Client", out var client) ? client.GetString("Version") : "";
    }

    /// <summary>The number in what the CLI said, any suffix left out; null when there is none.</summary>
    public static Version? Parse(string said) =>
        Version.TryParse(said.Split('-', '+', ' ')[0], out var version) ? version : null;
}
