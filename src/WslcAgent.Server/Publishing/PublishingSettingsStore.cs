using System.Text.Json;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// Settings → Publishing, kept in <c>publishing.json</c> in the agent's data
/// folder. Until it is saved, the defaults are examples to be replaced.
/// </summary>
public sealed class PublishingSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly PublishingSettings Defaults = new(
        "example.com", "-home", "wslc-published", "published", @"C:\wslc\published");

    private readonly string _path;
    private readonly Lock _gate = new();
    private PublishingSettings _settings;

    public PublishingSettingsStore(IOptions<WslcOptions> options)
    {
        _path = Path.Combine(options.Value.DataDirectory, "publishing.json");
        _settings = Read() ?? Defaults;
        // A file of the time the agent wrote nginx's map has a map file and no data folder.
        if (string.IsNullOrWhiteSpace(_settings.DataFolder))
        {
            _settings = _settings with { DataFolder = Defaults.DataFolder };
        }
    }

    public PublishingSettings Get()
    {
        lock (_gate)
        {
            return _settings;
        }
    }

    public PublishingSettings Set(PublishingSettings settings)
    {
        var cleaned = new PublishingSettings(
            Require(settings.Domain, "domain").TrimStart('.').ToLowerInvariant(),
            settings.NameSuffix.Trim().ToLowerInvariant(),
            Require(settings.ProxyContainer, "proxy container"),
            Require(settings.Network, "network"),
            Require(settings.DataFolder, "data folder"));
        lock (_gate)
        {
            _settings = cleaned;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_settings, JsonOptions));
            return _settings;
        }
    }

    private static string Require(string value, string what)
    {
        var text = value.Trim();
        return text.Length > 0 && !text.StartsWith('-') ? text : throw new ArgumentException($"The {what} is required.");
    }

    /// <summary>A file written by hand, or half-written, must not stop the agent: the defaults stand.</summary>
    private PublishingSettings? Read()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<PublishingSettings>(File.ReadAllText(_path), JsonOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
