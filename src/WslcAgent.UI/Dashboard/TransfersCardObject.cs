using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What the pieces of the File transfers card share (its header, the header
/// of its lines and its lines each want properties of their own): every file
/// travelling in or out of any container, the agent's and this client's queue,
/// and the container the card is filtered to — chosen in its header, obeyed by
/// its lines, per card and for as long as it is shown, not kept. The pieces
/// live only in their card, and open the Containers page.
/// </summary>
public abstract class TransfersCardObject : WslcObject
{
    [Inject] private TransferRows Transfers { get; set; } = default!;

    [Inject] private ContainerRows Containers { get; set; } = default!;

    [Inject] protected FileTransfers Mine { get; set; } = default!;

    [Inject] private TransferCardFilters Filters { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] protected WslcAgentApi Api { get; set; } = default!;

    [Inject] protected IDialogService Dialogs { get; set; } = default!;

    [Inject] protected ISnackbar Snackbar { get; set; } = default!;

    protected override void OnInitialized()
    {
        Follow(Transfers);
        Follow(Containers);
        Hear(handler => Mine.QueueChanged += handler, handler => Mine.QueueChanged -= handler);
        Hear(handler => Filters.Changed += handler, handler => Filters.Changed -= handler);
    }

    public override bool Opens => true;

    public override Task OpenAsync()
    {
        Navigation.NavigateTo("containers");
        return Task.CompletedTask;
    }

    /// <summary>The card the piece stands in, which the filter belongs to: the piece itself, were it ever alone.</summary>
    private string Card => Instance.Group ?? Instance.Id;

    private IReadOnlyList<ContainerTransfer> All => Transfers.Value ?? [];

    /// <summary>
    /// The containers the card can be filtered to: the ones that have something
    /// of their own right now, moving or waiting. Not every container there is —
    /// a filter that lists forty names to say that one of them is busy is a
    /// worse list than no filter.
    /// </summary>
    protected IReadOnlyList<string> Named =>
        [.. TransferFlow.Showing(All).Select(transfer => transfer.Container)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(container => container, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The filter in force: the container chosen while it still has files of
    /// its own, and all of them once it has none, so a finished container does
    /// not leave the list looking empty for good.
    /// </summary>
    protected string Filter => Filters.Of(Card) is { Length: > 0 } of && Named.Contains(of, StringComparer.Ordinal) ? of : "";

    /// <summary>The header's choice, which the card's lines follow.</summary>
    protected void Choose(string container) => Filters.Choose(Card, container);

    /// <summary>What the card lists: everything worth showing, or one container's when the filter names one.</summary>
    protected IReadOnlyList<ContainerTransfer> Shown =>
        TransferFlow.Showing(All.Where(transfer => Filter.Length == 0 || transfer.Container == Filter));

    /// <summary>What a container is called: its own name, and its id when nothing here knows better.</summary>
    protected string NameOf(string container) =>
        Containers.Rows?.FirstOrDefault(row => row.Id == container || row.Name == container)?.Name is { Length: > 0 } name ? name : container;
}

/// <summary>The container each File transfers card on screen is filtered to, by card: its header chooses, its lines follow.</summary>
public sealed class TransferCardFilters
{
    private readonly Dictionary<string, string> _of = new(StringComparer.Ordinal);

    public event Action? Changed;

    public string Of(string card) => _of.GetValueOrDefault(card, "");

    public void Choose(string card, string container)
    {
        _of[card] = container;
        Changed?.Invoke();
    }
}
