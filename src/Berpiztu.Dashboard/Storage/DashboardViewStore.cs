using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// One view of a dashboard kept with its other (<see cref="DashboardViews"/>),
/// as the designer reads and writes a dashboard: the view's layout is read
/// out of the whole text and written back into it, the other view's as the
/// store holds it at that moment, so a view written never takes the other
/// back to what it was. A portrait view never laid out is empty,
/// as many columns as a phone holds.
/// </summary>
public sealed class DashboardViewStore(IDashboardStore whole, string view) : IDashboardStore
{
    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await whole.LoadAsync(cancellationToken);
        var layout = DashboardViews.Read(stored, view);
        if (layout is not null || view != DashboardView.Portrait)
        {
            return layout;
        }

        return new DashboardLayout(DashboardView.PhoneColumns, []).Write();
    }

    public async Task SaveAsync(string text, CancellationToken cancellationToken = default) =>
        await whole.SaveAsync(DashboardViews.Write(await whole.LoadAsync(cancellationToken), view, text), cancellationToken);
}
