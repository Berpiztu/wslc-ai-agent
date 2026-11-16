namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Answer of <c>GET /api/v1/images/check</c>: each image checked against the
/// session's own record of its layers.
/// </summary>
/// <param name="Images">Every image checked: the one asked for, or all of them.</param>
public sealed record ImageChecks(IReadOnlyList<ImageCheck> Images);

/// <summary>
/// One image checked. A layer is damaged when the files it holds do not add up
/// to the size the session recorded for it when it was pulled or built: files
/// left empty or missing by a pull cut short, a disk that filled up, a session
/// that went down before what it had written reached the disk. What is checked
/// is how much is there, not every byte of it.
/// </summary>
/// <param name="Image">Its reference; its id for an image with no name.</param>
/// <param name="Id">Its id, as the image list gives it.</param>
/// <param name="Layers">The layers it is made of.</param>
/// <param name="Damaged">The ones that lost file contents; none for a whole image.</param>
public sealed record ImageCheck(string Image, string Id, int Layers, IReadOnlyList<DamagedLayer> Damaged)
{
    public bool IsDamaged => Damaged.Count > 0;

    /// <summary>What to tell whoever asked, and what to do about it: the one wording, wherever a damaged image is said.</summary>
    public string Why() => IsDamaged
        ? $"Image {Image} is damaged: {Damaged.Count} of its {Layers} layers lost file contents ({Lost()} missing), as a pull cut short or a full disk leaves them. Remove the image and pull it again."
        : $"Image {Image} is whole: its {Layers} layers hold what was recorded for them.";

    private string Lost() => Bytes.Humanize(Damaged.Sum(layer => Math.Max(0, layer.ExpectedBytes - layer.FoundBytes)));
}

/// <summary>A layer whose files do not add up to what was recorded for it.</summary>
/// <param name="Folder">The layer's folder in the session's store.</param>
/// <param name="ExpectedBytes">What the session recorded for it.</param>
/// <param name="FoundBytes">What its files add up to now.</param>
public sealed record DamagedLayer(string Folder, long ExpectedBytes, long FoundBytes);

/// <summary>Body of <c>POST /api/v1/images/checks</c> and of its cancel: the image to check, by its reference or its id.</summary>
public sealed record CheckImageRequest(string Reference);
