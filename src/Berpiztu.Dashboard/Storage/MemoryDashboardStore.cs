namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// A dashboard kept for as long as it is shown and nowhere else: one laid
/// out by the application to be looked at — a card made already on a page of
/// its own — and never saved.
/// </summary>
public sealed class MemoryDashboardStore(string? text) : IDashboardStore
{
    private string? _text = text;

    public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_text);

    public Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        _text = text;
        return Task.CompletedTask;
    }
}
