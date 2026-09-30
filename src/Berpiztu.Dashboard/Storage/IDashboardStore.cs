namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// Where the application keeps the dashboard: the SDK hands it the text of
/// the whole dashboard after every change, and reads it back when the page
/// opens. What the text says is the SDK's (<see cref="Model.DashboardLayout"/>);
/// where it lives is the application's.
/// </summary>
public interface IDashboardStore
{
    /// <summary>The text last saved; empty or null when there is none yet.</summary>
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string text, CancellationToken cancellationToken = default);
}
