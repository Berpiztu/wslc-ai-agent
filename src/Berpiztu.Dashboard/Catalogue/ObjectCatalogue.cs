using System.Reflection;
using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Sources;
using Berpiztu.Dashboard.Storage;

namespace Berpiztu.Dashboard.Catalogue;

/// <summary>One kind of object the toolbox offers: its descriptor, the component that draws it, and the parts it declares, in their order.</summary>
public sealed record ObjectDescriptor(DashboardObjectAttribute About, Type Component, IReadOnlyList<DashboardObjectPartAttribute> Parts,
    IReadOnlyList<DashboardObjectAlarmAttribute> Alarms)
{
    /// <summary>An object's alarm on one measure: as the user set it, or its kind's own until they do, at the default threshold.</summary>
    public ObjectAlarm AlarmOf(ObjectInstance instance, DashboardObjectAlarmAttribute measure) =>
        instance.Alarms?.FirstOrDefault(alarm => alarm.Measure == measure.Measure) ?? new ObjectAlarm(measure.Measure, Off: !measure.On);

    /// <summary>The object's alarms that are on.</summary>
    public IEnumerable<(DashboardObjectAlarmAttribute Measure, ObjectAlarm Alarm)> AlarmsOn(ObjectInstance instance) =>
        Alarms.Select(measure => (measure, AlarmOf(instance, measure))).Where(pair => !pair.Item2.Off);

    /// <summary>An object's alarm is up: one of the measures it watches is past its threshold; never while it is hidden.</summary>
    public bool Up(ObjectInstance instance, IMeasures measures) =>
        instance.Hidden != true && AlarmsOn(instance).Any(on => measures.Of(instance, on.Measure.Measure) is { } value && value > on.Alarm.Threshold);

    public string Type => About.Type;

    public string Label => About.Label;

    public bool HasSource => About.Source.Length > 0;

    /// <summary>What an object of this kind reads: its own source, or its kind's default until one is chosen; null while it has neither.</summary>
    public string? SourceOf(ObjectInstance instance) =>
        instance.Source ?? (About.DefaultSource.Length > 0 ? About.DefaultSource : null);

    /// <summary>A source chosen as it is kept: none when it is the kind's default.</summary>
    public string? Kept(string source) => source == About.DefaultSource ? null : source;

    /// <summary>Where an object of this kind stands across its cells: its own alignment, or its kind's until one is chosen.</summary>
    public HorizontalAlign HorizontalOf(ObjectInstance instance) => instance.Horizontal ?? About.Horizontal;

    /// <summary>Where an object of this kind stands down its cells: its own alignment, or its kind's until one is chosen.</summary>
    public VerticalAlign VerticalOf(ObjectInstance instance) => instance.Vertical ?? About.Vertical;

    /// <summary>Which of its card's edges an object of this kind keeps to as a fluid view widens it: its own anchor, or its kind's until one is chosen.</summary>
    public HorizontalAnchor AnchorOf(ObjectInstance instance) => instance.Anchor ?? About.Anchor;

    /// <summary>
    /// A new object of this kind as it is born in a view: as its default there
    /// has it where one was designed, at its descriptor's cells otherwise, in
    /// either view alike, no wider than <paramref name="columns"/>; at the
    /// top-left cell, reading nothing yet.
    /// </summary>
    public ObjectInstance Born(string id, IObjectDefaults? defaults, string view, int columns)
    {
        var born = new ObjectInstance(id, Type, 0, 0, About.Columns, About.Rows);
        born = defaults?.For(view, Type) is { } designed ? designed.On(born) : born;
        return born with { W = Math.Min(born.W, columns) };
    }
}

/// <summary>
/// Every kind of object there is, found by itself: each component that
/// inherits <see cref="DashboardObject"/> and carries a
/// <see cref="DashboardObjectAttribute"/>, in the assemblies the application
/// names. Nothing lists them by hand.
/// </summary>
public sealed class ObjectCatalogue
{
    private readonly Dictionary<string, ObjectDescriptor> _byType;

    public ObjectCatalogue(IEnumerable<Assembly> assemblies)
    {
        All = [.. assemblies.Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false } && typeof(DashboardObject).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<DashboardObjectAttribute>() is { } about
                ? new ObjectDescriptor(about, type, [.. type.GetCustomAttributes<DashboardObjectPartAttribute>()],
                    [.. type.GetCustomAttributes<DashboardObjectAlarmAttribute>()])
                : null)
            .OfType<ObjectDescriptor>()
            .OrderBy(descriptor => descriptor.About.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(descriptor => descriptor.Label, StringComparer.CurrentCultureIgnoreCase)];
        _byType = All.ToDictionary(descriptor => descriptor.Type, StringComparer.Ordinal);
    }

    /// <summary>Every kind, in the toolbox's order: by group, then by name.</summary>
    public IReadOnlyList<ObjectDescriptor> All { get; }

    /// <summary>Some object of this layout has its alarm up: a page's tab says so without being opened.</summary>
    public bool AnyUp(DashboardLayout layout, IMeasures measures) =>
        layout.Objects.Any(o => Find(o.Type) is { } kind && kind.Up(o, measures));

    /// <summary>The kind of this type id; null for one this build does not know, which is kept but not drawn.</summary>
    public ObjectDescriptor? Find(string type) => _byType.GetValueOrDefault(type);
}
