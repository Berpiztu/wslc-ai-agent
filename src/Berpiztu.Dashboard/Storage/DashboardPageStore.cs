using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// One page of a dashboard kept with its others (<see cref="DashboardPages"/>):
/// the page's text read out of the whole and written back into it, the other
/// pages as the store holds them at that moment. A page never laid out starts
/// empty on the main page's cells, so every page's cells are one size.
/// </summary>
/// <param name="page">The page's name; null for the main page.</param>
public sealed class DashboardPageStore(IDashboardStore whole, string? page) : IDashboardStore
{
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await whole.LoadAsync(cancellationToken);
        var text = DashboardPages.Read(stored, page);
        if (text is not null || page is null)
        {
            return text;
        }

        var main = DashboardLayout.Read(DashboardPages.Read(stored, null));
        return new DashboardLayout(main.Columns, []).Write();
    }

    public async Task SaveAsync(string text, CancellationToken cancellationToken = default) =>
        await whole.SaveAsync(DashboardPages.Write(await whole.LoadAsync(cancellationToken), page, text), cancellationToken);
}
