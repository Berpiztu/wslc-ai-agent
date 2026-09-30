using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Dashboard;

/// <summary>The images, as a family objects take their source from; a dangling image, which the registry does not number, is not offered.</summary>
public sealed class ImageSourceFamily(ImageRows rows) : RegistrySourceFamily<ImageListResponse, ImageSummary>(rows)
{
    /// <summary>The key objects of type image name in their descriptor.</summary>
    public const string FamilyKey = "image";

    public override string Key => FamilyKey;

    public override string Label => "Image";

    public override string Noun => "image";
}
