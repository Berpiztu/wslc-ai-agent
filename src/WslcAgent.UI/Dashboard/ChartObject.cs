using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What a chart, its legend and its summary share: their subject, the host
/// by default or one of the user's containers
/// (<see cref="SubjectSourceFamily"/>), and the read that subject's chart
/// draws — the host's runtime or I/O, or the container's stats — followed
/// while it is shown and let go of when the subject changes. A container
/// that is gone takes the object off the dashboard, as any container
/// object's does. The three are the pieces of a chart card and live only
/// in it.
/// </summary>
public abstract class ChartObject : WslcObject
{
    private string? _subject;
    private IDisposable? _following;
    private IDisposable? _rows;
    private string? _statsOf;

    [Inject] private HostRuntimeRead Runtime { get; set; } = default!;

    [Inject] private HostIoRead Io { get; set; } = default!;

    [Inject] private ContainerRows Containers { get; set; } = default!;

    [Inject] private ContainerStatsReads Stats { get; set; } = default!;

    [Inject] private IDialogService Dialogs { get; set; } = default!;

    /// <summary>A chart, its legend and its summary open the full chart, with its legend and zoom.</summary>
    public override bool Opens => Data is not null;

    public override Task OpenAsync() => Data is { } data
        ? ChartDialog.ShowAsync(Dialogs, data.Title, data.Value, data.Format, data.YMax, data.Series, data.Times, data.Values, data.ForceYMax)
        : Task.CompletedTask;

    /// <summary>Which of the four charts it is: <see cref="HostCharts.Cpu"/>, memory, disk or network.</summary>
    protected abstract string Chart { get; }

    /// <summary>The container shown; null for the host, or while the container list is not read.</summary>
    protected ContainerSummary? Container => SubjectUid is { } uid ? Containers.Find(uid) : null;

    /// <summary>What the chart of its subject draws; null while its container is not known yet.</summary>
    protected HostChartData? Data => SubjectUid is null ? HostCharts.Of(Chart, Runtime, Io)
        : Container is { } container ? HostCharts.Of(Chart, Stats.For(container.Id)) : null;

    /// <summary>A reading has come for its subject.</summary>
    protected bool Read => SubjectUid is null
        ? (HostCharts.FromRuntime(Chart) ? Runtime.Value is not null : Io.Value is not null)
        : Container is { } container && Stats.For(container.Id).Value is not null;

    /// <summary>The host's last reading came without its measure (the CLI's stats failed).</summary>
    protected bool Failed => SubjectUid is null
        && (HostCharts.FromRuntime(Chart) ? Runtime.Value?.Error : Io.Value?.Error) == true;

    /// <summary>The container's registry uid; null for the host.</summary>
    private int? SubjectUid => Source == SubjectSourceFamily.Host ? null : RegistrySource.Uid(Source);

    protected override void OnParametersSet()
    {
        if (Source == _subject)
        {
            return;
        }

        _subject = Source;
        _following?.Dispose();
        _rows?.Dispose();
        _following = null;
        _rows = null;
        _statsOf = null;
        if (SubjectUid is null)
        {
            _following = HostCharts.FromRuntime(Chart) ? Follow(Runtime) : Follow(Io);
        }
        else
        {
            _rows = Follow(Containers, OnContainers);
            OnContainers();
        }
    }

    /// <summary>The container list read: the container's stats followed once it is known, the object kept without it once it is not here.</summary>
    private void OnContainers() => _ = InvokeAsync(() =>
    {
        if (SubjectUid is { } uid && Containers.Gone(uid))
        {
            return SourceGoneAsync();
        }

        if (Container is { } container && container.Id != _statsOf)
        {
            _following?.Dispose();
            _statsOf = container.Id;
            _following = Follow(Stats.For(container.Id));
        }

        StateHasChanged();
        return Task.CompletedTask;
    });
}
