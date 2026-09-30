using System.Text.Json;
using System.Text.Json.Nodes;

namespace WslcAgent.Server.Overview;

/// <summary>
/// A file of defaults the agent ships, view by view and kind by kind — how
/// each kind of dashboard object is born (<see cref="ObjectDefaultsStore"/>) —
/// designed on the development agent and written back to the repository
/// (<see cref="ShippedFile"/>), so the installer ships it.
/// <para>
/// One kind is saved at a time and merged here, under its view and its kind,
/// with nothing else touched: two clients open on the development agent each
/// sent their whole copy of the table, and the one that saved last wrote over
/// what the other had just saved. The agent reads the file only as far as its
/// views and kinds; what each kind holds is the client's, kept as it came.
/// </para>
/// </summary>
public abstract class ShippedDefaults(ShippedFile file, ILogger logger)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly Lock _gate = new();

    /// <summary>Whether this agent writes them back to where it is built from (a development build).</summary>
    public bool Writable => file.Writable;

    /// <summary>The defaults as the clients wrote them; empty when there are none.</summary>
    public string Get() => file.Read() ?? "";

    /// <summary>
    /// One kind's default in one view, merged into the file and written back to
    /// the repository; false on a release build, which ships them and writes
    /// none, and on a body that is not a kind's default.
    /// </summary>
    public bool Set(string view, string kind, string value)
    {
        if (!file.Writable)
        {
            return false;
        }

        JsonNode? entry;
        try
        {
            entry = JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return false;
        }

        if (entry is not JsonObject)
        {
            return false;
        }

        lock (_gate)
        {
            var defaults = Parse(Get());
            if (defaults[view] is not JsonObject kinds)
            {
                defaults[view] = kinds = [];
            }

            kinds[kind] = entry;
            if (!file.Write(defaults.ToJsonString(Indented)))
            {
                return false;
            }
        }

        logger.LogInformation("default saved: {View}/{Kind}", view, kind);
        return true;
    }

    /// <summary>The file as an object of views; a file that is none starts over rather than being written on.</summary>
    private static JsonObject Parse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
