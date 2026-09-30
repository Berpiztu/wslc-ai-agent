using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// What every list page (containers, images, volumes, networks) does the same
/// way: load, read again when the agent says this kind changed, filter by the
/// search box, keep a selection across refreshes, run bulk verbs, open
/// dialogs, confirm removals and report through the snackbar. A page provides
/// the data and the columns.
/// </summary>
public abstract class ListPageBase<TItem> : ComponentBase, IDisposable
    where TItem : class
{
    private const string Cards = "cards";

    private const string Table = "table";

    /// <summary>The clock, for as long as the agent is not telling us what changes.</summary>
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(5);

    /// <summary>
    /// And the clock once it is: a net under the events, not the way they are
    /// heard. `wslc events` says nothing about what died with a session it was
    /// listening to, so a slow reading of one's own is what keeps a screen from
    /// showing a container that is no longer there.
    /// </summary>
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(60);

    private PeriodicTimer? _timer;
    private CancellationTokenSource? _stop;
    private string _search = "";
    private SortDefinition<ListRow<TItem>>? _sort;

    private ViewMode _view = ViewMode.Table;
    private int _page;
    private IReadOnlyList<ListRow<TItem>> _rows = [];
    private (IReadOnlyList<TItem>? Items, string Search) _rowsOf;

    [Inject] protected WslcAgentApi Api { get; set; } = default!;

    [Inject] protected ISnackbar Snackbar { get; set; } = default!;

    [Inject] protected IDialogService Dialogs { get; set; } = default!;

    [Inject] private OpenPopups Popups { get; set; } = default!;

    [Inject] private SessionState Session { get; set; } = default!;

    [Inject] private AgentLink Link { get; set; } = default!;

    [Inject] private ViewPreference Preference { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private AgentChanges Changes { get; set; } = default!;

    [Inject] private ListOrder Order { get; set; } = default!;

    /// <summary>The searched text, from the query (<c>?q=</c>).</summary>
    [SupplyParameterFromQuery(Name = "q")] public string? SearchQuery { get; set; }

    /// <summary><c>?view=cards</c> or <c>?view=table</c>, always written: without it the list opens as it was last left, which a step Back must not depend on.</summary>
    [SupplyParameterFromQuery(Name = "view")] public string? ViewQuery { get; set; }

    /// <summary><c>?page=</c>, zero-based; absent is the first.</summary>
    [SupplyParameterFromQuery(Name = "page")] public int? PageQuery { get; set; }

    protected IReadOnlyList<TItem> Items { get; private set; } = [];

    /// <summary>The ticked rows; a row is its key, so a tick outlives the poll that replaces the item.</summary>
    protected HashSet<ListRow<TItem>> Selected { get; set; } = [];

    protected bool Loading { get; private set; }

    protected string? Error { get; private set; }

    /// <summary>
    /// Which list this is, for the view choice: the row type, not the address.
    /// The address is still the old page when the router sets the parameters of
    /// the new one, and a key read from it opened the list on the previous
    /// list's choice and corrected itself a second later, in front of the user.
    /// </summary>
    private static string Section => typeof(TItem).Name;

    /// <summary>
    /// The kind this page lists, as the agent's change notices name it:
    /// <c>ContainerSummary</c> is the <c>container</c> they talk about. A page
    /// whose rows are named otherwise says so by overriding this.
    /// </summary>
    protected virtual string Kind => Section.Replace("Summary", "", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>The WSLC session is not running: there is nothing to list, and the page says so.</summary>
    protected bool SessionStopped => !Session.Active;

    /// <summary>
    /// What the page is showing lives in the query, not in a field: Back then
    /// returns to the list exactly as it was left — the text searched, the
    /// table or cards choice, the page — and a link to it shows the same. The
    /// search only replaces the current entry (a keystroke is not a step);
    /// changing the view or the page adds one.
    /// </summary>
    protected string Search
    {
        get => _search;
        set
        {
            if (_search != value)
            {
                _search = value;
                _page = 0;  // The rows are other rows now; page 3 of them means nothing.
                KeepInUrl(replace: true);
            }
        }
    }

    protected ViewMode View
    {
        get => _view;
        set
        {
            if (_view != value)
            {
                _view = value;
                // Each list keeps its own choice: containers as cards, images as rows.
                Preference.Set(Section, value);
                KeepInUrl(replace: false);
            }
        }
    }

    /// <summary>The grid's current page, zero-based.</summary>
    protected int Page
    {
        get => _page;
        set
        {
            if (_page != value)
            {
                _page = value;
                KeepInUrl(replace: false);
            }
        }
    }

    /// <summary>Rows after the search box, in the order the table was left in.</summary>
    protected IReadOnlyList<TItem> Visible => InTableOrder(string.IsNullOrWhiteSpace(Search)
        ? Items
        : Items.Where(item => Matches(item, Search)).ToList());

    /// <summary>
    /// The sort the user set on the table, kept by the page so the cards show
    /// the same order: MudDataGrid sorts the rows it draws and a card is not a
    /// row, so without this the two views of one list disagreed. The definition
    /// the grid hands over carries the column's own accessor, which is what
    /// orders the items here.
    /// </summary>
    protected void SortChanged(SortDefinition<ListRow<TItem>>? sort) => _sort = sort;

    private IReadOnlyList<TItem> InTableOrder(IReadOnlyList<TItem> items)
    {
        if (_sort?.SortFunc is not { } by)
        {
            return items;
        }

        var keyed = items.Select(item => (Item: item, Key: by(new ListRow<TItem>(KeyOf(item), item))));
        var ordered = _sort.Descending
            ? keyed.OrderByDescending(pair => pair.Key, _sort.Comparer)
            : keyed.OrderBy(pair => pair.Key, _sort.Comparer);
        return [.. ordered.Select(pair => pair.Item)];
    }

    /// <summary>The same rows for the grid, each with its key (see <see cref="ListRow{TItem}"/>).</summary>
    protected IReadOnlyList<ListRow<TItem>> Rows
    {
        get
        {
            // Built once per load or search, not per render: the grid takes a new list as new data.
            if (!ReferenceEquals(_rowsOf.Items, Items) || _rowsOf.Search != Search)
            {
                _rows = Visible.Select(item => new ListRow<TItem>(KeyOf(item), item)).ToList();
                _rowsOf = (Items, Search);
            }

            return _rows;
        }
    }

    /// <summary>The rows, from the agent.</summary>
    protected abstract Task<IReadOnlyList<TItem>> LoadAsync();

    /// <summary>Whether the row matches the search text; use <see cref="Has"/> per field.</summary>
    protected abstract bool Matches(TItem item, string search);

    /// <summary>Identity that survives a refresh: the grid keys its rows and the selection on it.</summary>
    protected abstract string KeyOf(TItem item);

    /// <summary>
    /// Identity that keeps a row's place in the list (<see cref="ListOrder"/>):
    /// its key, unless something the user does gives it a new one — a page
    /// whose rows are recreated under a new key says what stays.
    /// </summary>
    protected virtual string PlaceOf(TItem item) => KeyOf(item);

    /// <summary>Whether a verb on the selection may act on this row; a row whose own verbs stand greyed is left out of the selection's too.</summary>
    protected virtual bool CanAct(TItem item) => true;

    /// <summary>What messages call the row.</summary>
    protected abstract string NameOf(TItem item);

    protected static string Dash(string value) => Readings.Dash(value);

    /// <summary>A verb a later slice brings: it stays a normal button and says so.</summary>
    protected void NotYet(string feature, string slice) => Slices.NotYet(Snackbar, feature, slice);

    protected static bool Has(string value, string search) => value.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>The query is the state: a Back, a Forward or a pasted link all arrive here.</summary>
    protected override void OnParametersSet()
    {
        _search = SearchQuery ?? "";
        // The address wins when it carries a view (Back, a shared link); with none,
        // the list opens the way this list was last left.
        _view = ViewQuery is { Length: > 0 }
            ? string.Equals(ViewQuery, Cards, StringComparison.OrdinalIgnoreCase) ? ViewMode.Cards : ViewMode.Table
            : Preference.For(Section);
        Preference.Set(Section, _view);
        _page = Math.Max(0, PageQuery ?? 0);
    }

    /// <summary>Writes the current state into the query; <paramref name="replace"/> keeps it out of the history.</summary>
    private void KeepInUrl(bool replace) =>
        Navigation.NavigateTo(
            Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
            {
                ["q"] = _search.Length > 0 ? _search : null,
                ["view"] = _view == ViewMode.Cards ? Cards : Table,
                ["page"] = _page > 0 ? _page : null,
            }),
            replace: replace);

    /// <summary>
    /// The page draws itself first and reads afterwards: waiting for the agent
    /// before the first render left the previous screen on show for as long as
    /// the CLI took — the list one was coming from, or a container's details —
    /// and it then changed under the user. Now the list appears at once, in the
    /// view it is remembered in, with its loading bar until the rows arrive.
    /// </summary>
    protected override void OnInitialized()
    {
        Session.Changed += OnSessionChanged;
        Link.Changed += OnLinkChanged;
        Changes.Changed += OnAgentChanged;
        Changes.LiveChanged += OnLiveChanged;
        _stop = new CancellationTokenSource();
        _timer = new PeriodicTimer(Interval);
        Changes.Start();
        _ = FirstLoadAsync();
        _ = PollAsync(_stop.Token);
    }

    /// <summary>
    /// The agent says something of this kind changed, so the list is read now
    /// rather than at the next tick. Everything else that arrives — another
    /// kind's notice — is somebody else's screen.
    /// </summary>
    private void OnAgentChanged(ChangeNotice notice)
    {
        if (!notice.Touches(Kind))
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            await RefreshAsync(polling: true);
            StateHasChanged();
        });
    }

    /// <summary>
    /// The clock follows the stream: five seconds while nothing is telling us
    /// what changes, a minute while something is. It is never switched off —
    /// what died with a session the stream was listening to is never announced.
    /// A kind <c>wslc</c> reports nothing about keeps its five seconds, because
    /// for that list the stream being up changes nothing at all.
    /// </summary>
    private TimeSpan Interval =>
        Changes.Live && ChangeNotice.IsReported(Kind) ? SafetyNet : RefreshEvery;

    private void OnLiveChanged()
    {
        if (_timer is { } timer)
        {
            timer.Period = Interval;
        }
    }

    /// <summary>
    /// What this device remembers, read before <see cref="OnParametersSet"/>
    /// picks the view: the list opens in the view it was left in, the first
    /// time the app is opened as well as every time after.
    /// </summary>
    protected override Task OnInitializedAsync() => Preference.ReadyAsync();

    private async Task FirstLoadAsync()
    {
        await RefreshAsync();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The session was started or stopped, or another one was chosen: what the
    /// page holds belongs to that session, so it is dropped and read again —
    /// or left empty until the session comes back, which the page says.
    /// </summary>
    private void OnSessionChanged() => _ = InvokeAsync(async () =>
    {
        Selected.Clear();
        await RefreshAsync();
        StateHasChanged();
    });

    /// <summary>
    /// The agent answers again: the page reads again at once, as it does when the
    /// session comes back. While it was gone the layout said so over the screen;
    /// the page had nothing to add.
    /// </summary>
    private void OnLinkChanged()
    {
        if (Link.Online)
        {
            _ = InvokeAsync(async () =>
            {
                await RefreshAsync();
                StateHasChanged();
            });
        }
    }

    /// <summary>
    /// Reads the sessions once after a failure and publishes what it finds:
    /// true when the session is gone, which is the page's answer instead of the
    /// error. Listing sessions does not open one, so asking is free of
    /// consequences. The agent being unreachable leaves the original error.
    /// </summary>
    private async Task<bool> SessionWentAwayAsync()
    {
        try
        {
            Session.Set(await Api.GetSessionsAsync());
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            return false;
        }

        if (SessionStopped)
        {
            Items = [];
            Selected.Clear();
        }

        return SessionStopped;
    }

    protected Task RefreshAsync() => RefreshAsync(polling: false);

    /// <summary>A read was asked for while one was under way: it happens as soon as that one ends.</summary>
    private bool _readAgain;

    /// <summary>
    /// Reads the list. A read asked for while another is under way is not
    /// dropped but done once that one ends: a
    /// recreate is announced at its start and at its end a third of a second
    /// apart, the end's read was thrown away because the start's was still
    /// running, and the row kept the id of a container that no longer existed.
    /// Several asks in the meantime are one more read, not several.
    /// </summary>
    private async Task RefreshAsync(bool polling)
    {
        if (Loading)
        {
            _readAgain = true;
            return;
        }

        do
        {
            _readAgain = false;
            await ReadAsync(polling);
        }
        while (_readAgain);
    }

    private async Task ReadAsync(bool polling)
    {

        // Nothing lives in a stopped session, and asking would open it again:
        // the page shows what it is instead of an error from the CLI.
        if (SessionStopped)
        {
            Items = [];
            Selected.Clear();
            Error = null;
            return;
        }

        Loading = true;
        try
        {
            var items = await LoadAsync();
            // A row menu opened while the poll was loading: the new rows would close it
            // under the pointer, so this poll is dropped as one skipped at its start.
            if (polling && Popups.AnyOpen)
            {
                return;
            }

            // Rows keep the places they were shown in: the agent lists what was
            // started or created last first, and a start is not a reason to move.
            Items = Order.Keep(Section, items, PlaceOf);
            // The ticks move onto the new rows by key, and a row that vanished drops
            // out.
            var selectedKeys = Selected.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
            Selected = Items.Where(item => selectedKeys.Contains(KeyOf(item))).Select(item => new ListRow<TItem>(KeyOf(item), item)).ToHashSet();
            Error = null;
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            // The agent not answering is said once, by the layout, over the
            // screen: the page adds nothing. Otherwise: the CLI cannot work
            // without a session, and another client — or a terminal on that
            // machine — may have stopped it a moment ago. Ask whose fault this
            // is before repeating the CLI's error: with the session down the
            // screen says that instead, and disables itself.
            Error = !Link.Online ? null : await SessionWentAwayAsync() ? null : ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>
    /// Runs one verb on every selected row:
    /// one toast for the whole batch, counting itself up in place ("Stopping 2
    /// of 5") instead of a new toast per row, and one summary at the end — the
    /// count that went through, or the failures, the first five of them named.
    /// </summary>
    protected async Task BulkAsync(string verb, Func<TItem, CancellationToken, Task> action)
    {
        var items = Selected.Select(row => row.Value).Where(CanAct).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var failures = new List<string>();
        var progress = new LiveProgress();
        progress.Set($"{Gerund(verb)} 1 of {items.Count}");
        var toast = Snackbar.Add(
            builder =>
            {
                builder.OpenComponent<LiveProgressToast>(0);
                builder.AddComponentParameter(1, nameof(LiveProgressToast.Progress), progress);
                builder.CloseComponent();
            },
            Severity.Info,
            options => options.RequireInteraction = true);

        for (var index = 0; index < items.Count; index++)
        {
            progress.Set($"{Gerund(verb)} {index + 1} of {items.Count}");
            try
            {
                await action(items[index], CancellationToken.None);
            }
            catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
            {
                failures.Add($"{NameOf(items[index])}: {ex.Message}");
            }
        }

        // The batch is over: its line goes, and the summary takes its place.
        toast?.ForceClose();
        if (failures.Count == 0)
        {
            Snackbar.Add($"{items.Count} item(s) {PastTense(verb)}", Severity.Success);
        }
        else
        {
            var head = failures.Count == items.Count
                ? $"All {items.Count} {verb} action(s) failed"
                : $"{failures.Count}/{items.Count} {verb} action(s) failed";
            Snackbar.Add($"{head}\n{string.Join("\n", failures.Take(5))}", Severity.Error);
        }

        Selected.Clear();
        await RefreshAsync();
    }

    private static string PastTense(string verb) => verb switch
    {
        "stop" => "stopped",
        _ when verb.EndsWith('e') => verb + "d",
        _ => verb + "ed",
    };

    /// <summary>What the batch is doing right now: Starting, Stopping, Removing.</summary>
    private static string Gerund(string verb) => verb switch
    {
        "stop" => "Stopping",
        _ when verb.EndsWith('e') => char.ToUpperInvariant(verb[0]) + verb[1..^1] + "ing",
        _ => char.ToUpperInvariant(verb[0]) + verb[1..] + "ing",
    };

    /// <summary>Removes the selection after one confirmation.</summary>
    protected async Task BulkRemoveAsync(string kind, Func<TItem, CancellationToken, Task> remove)
    {
        if (await DialogFlow.ConfirmRemoveAsync(Dialogs, $"{kind}s", $"{Selected.Count} selected {kind}(s)"))
        {
            await BulkAsync("remove", remove);
        }
    }

    /// <summary>Cancelled when the page goes away: for a page's own polling next to the list's.</summary>
    protected CancellationToken PageClosing => _stop?.Token ?? CancellationToken.None;

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        Link.Changed -= OnLinkChanged;
        Changes.Changed -= OnAgentChanged;
        Changes.LiveChanged -= OnLiveChanged;
        _stop?.Cancel();
        _timer?.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken))
            {
                // A refresh rebuilds the rows and would close a row menu the
                // user has open; the next tick catches up. With the agent gone
                // the layout's probe is the only call made, and the page reads
                // again the moment it answers.
                if (Popups.AnyOpen || !Link.Online)
                {
                    continue;
                }

                await RefreshAsync(polling: true);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
