using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;

namespace WslcAgent.Server.Images;

/// <summary>
/// Publishing a version: the local image tagged with its reference in the
/// registry, then pushed as a job the push console follows; with
/// <see cref="PublishImageRequest.Latest"/>, the repository's <c>latest</c> too,
/// so a machine that pulls without a version gets this one.
/// </summary>
public sealed class ImagePublisher(IImageService images, ImagePushes pushes)
{
    /// <summary>The pushes started: the version's first, then <c>latest</c>'s.</summary>
    public async Task<IReadOnlyList<ImagePullState>> PublishAsync(PublishImageRequest request, CancellationToken cancellationToken = default)
    {
        var target = ImageReference.Normalize(request.Target);
        if (!ImageReference.HasTag(target))
        {
            throw new ArgumentException("Name the version to publish: repository:version.", nameof(request));
        }

        var targets = new List<string> { target };
        var latest = $"{ImageReference.RepositoryOf(target)}:{ImageReference.Latest}";
        if (request.Latest && latest != target)
        {
            targets.Add(latest);
        }

        foreach (var reference in targets)
        {
            await images.TagAsync(request.Source, reference, cancellationToken);
        }

        return targets.Select(reference => pushes.Start(reference)).ToList();
    }
}
