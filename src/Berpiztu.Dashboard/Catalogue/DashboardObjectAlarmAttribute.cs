namespace Berpiztu.Dashboard.Catalogue;

/// <summary>
/// A measure an object can raise an alarm on: a percent the application works out for it
/// (<see cref="Sources.IMeasures"/>), past whose threshold the object — or
/// the card it stands in — turns red and blinks. Written on the object's
/// component beside its descriptor, one per measure, in the order the
/// properties window shows them.
/// </summary>
/// <param name="measure">The measure's name in the stored dashboard and to the application (<c>cpu</c>).</param>
/// <param name="label">What the properties window and the status bar call it (CPU).</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DashboardObjectAlarmAttribute(string measure, string label) : Attribute
{
    public string Measure { get; } = measure;

    public string Label { get; } = label;

    /// <summary>
    /// The alarm is on until the user switches it off: an object that is on the
    /// dashboard to be watched (the host's CPU); off until it is switched on
    /// otherwise (a container's).
    /// </summary>
    public bool On { get; init; }
}
