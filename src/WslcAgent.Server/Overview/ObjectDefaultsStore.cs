namespace WslcAgent.Server.Overview;

/// <summary>
/// How each kind of dashboard object is born, view by view — its cells, its
/// type size, its alignment and margins, its colours and its parts:
/// what an object dropped from the
/// toolbox takes and what Reset brings back. Designed on the development
/// agent on the board of every object, and
/// written back to the repository (object-defaults.json).
/// </summary>
public sealed class ObjectDefaultsStore(ILogger<ObjectDefaultsStore> logger)
    : ShippedDefaults(new ShippedFile("object-defaults.json", "ObjectDefaultsSource"), logger);
