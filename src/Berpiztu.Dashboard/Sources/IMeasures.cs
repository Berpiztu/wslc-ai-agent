using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Sources;

/// <summary>
/// The percents the objects' alarms watch, worked out by the application for
/// an object and one of its measures (<see cref="Catalogue.DashboardObjectAlarmAttribute"/>):
/// the host's CPU, a container's memory. The SDK compares them with the
/// thresholds and marks what is up; it knows nothing of what they are.
/// </summary>
public interface IMeasures
{
    /// <summary>The measure now, in percent; null while it is not known, which raises no alarm.</summary>
    double? Of(ObjectInstance o, string measure);

    /// <summary>A measure may have changed.</summary>
    event Action? Changed;
}
