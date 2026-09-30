using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// The image list, read for the dashboard's objects of type <c>image</c>. Not
/// asked while the session is stopped, which keeps the rows it had.
/// </summary>
public sealed class ImageRows(WslcAgentApi api, SessionState session)
    : RegistryRows<ImageListResponse, ImageSummary>(TimeSpan.FromSeconds(5))
{
    public override int UidOf(ImageSummary row) => row.Uid;

    public override string NameOf(ImageSummary row) => row.Reference;

    protected override IReadOnlyList<ImageSummary> RowsOf(ImageListResponse list) => list.Images;

    protected override bool CanRead => session.Active;

    protected override async Task<ImageListResponse?> ReadAsync(CancellationToken cancellationToken) =>
        await api.GetImagesAsync(cancellationToken);
}
