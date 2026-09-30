using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>An object of type <c>image</c>: its source is an image, and it reads the image list.</summary>
public abstract class ImageObject : RegistryObject<ImageListResponse, ImageSummary>
{
    [Inject] protected ImageRows Images { get; set; } = default!;

    protected override RegistryRows<ImageListResponse, ImageSummary> Rows => Images;

    /// <summary>The image's row; null until the list is read, or while its image is not in it.</summary>
    protected ImageSummary? Image => Resource;
}
