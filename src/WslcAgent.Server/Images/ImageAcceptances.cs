using Microsoft.Extensions.Options;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Images;

/// <summary>
/// The vulnerabilities a person accepted (<see cref="ImageAcceptance"/>), kept in
/// <c>image-acceptances.json</c>: one nobody can mend today, in a part of the
/// image it does not use, which would otherwise colour the image and its
/// containers for good. An acceptance is its image's repository's, so the next
/// version built keeps it, and counts only while no version fixes the
/// vulnerability (<see cref="ImageRemedies.Acceptable"/>).
/// </summary>
public sealed class ImageAcceptanceStore(IOptions<WslcOptions> options)
    : SavedSettings<ImageAcceptances>(Path.Combine(options.Value.DataDirectory, "image-acceptances.json"), new ImageAcceptances())
{
    private readonly Lock _changing = new();

    /// <summary>The repository an acceptance is kept by: the image without its tag, in lower case.</summary>
    public static string RepositoryOf(string image) => ImageReference.RepositoryOf(ImageReference.Normalize(image));

    /// <summary>The acceptance that covers this vulnerability of the image, while it still has no fix; null otherwise.</summary>
    internal ImageAcceptance? Of(string image, ImageVulnerability vulnerability)
    {
        if (!ImageRemedies.Acceptable(vulnerability.FixedIn, vulnerability.UbuntuProFixedIn))
        {
            return null;
        }

        var repository = RepositoryOf(image);
        return Get().All.FirstOrDefault(accepted => Same(accepted, repository, vulnerability.Ecosystem, vulnerability.Package, vulnerability.Advisory));
    }

    /// <summary>Accepts the vulnerability, or gives an acceptance already there its new reason.</summary>
    public ImageAcceptance Accept(AcceptImageVulnerability request, string by)
    {
        var accepted = new ImageAcceptance(RepositoryOf(request.Image), request.Ecosystem.Trim(), request.Package.Trim(),
            request.Advisory.Trim(), request.Reason.Trim(), by, DateTimeOffset.UtcNow);
        lock (_changing)
        {
            Set(new ImageAcceptances([.. Others(accepted.Repository, accepted.Ecosystem, accepted.Package, accepted.Advisory), accepted]));
        }

        return accepted;
    }

    /// <summary>Withdraws the acceptance; false when there was none.</summary>
    public bool Withdraw(string image, string ecosystem, string package, string advisory)
    {
        var repository = RepositoryOf(image);
        lock (_changing)
        {
            var others = Others(repository, ecosystem, package, advisory);
            if (others.Count == Get().All.Count)
            {
                return false;
            }

            Set(new ImageAcceptances(others));
            return true;
        }
    }

    private List<ImageAcceptance> Others(string repository, string ecosystem, string package, string advisory) =>
        [.. Get().All.Where(accepted => !Same(accepted, repository, ecosystem, package, advisory))];

    // A package's name is compared as PyPI and the distributions do, without case; the rest exactly.
    private static bool Same(ImageAcceptance accepted, string repository, string ecosystem, string package, string advisory) =>
        accepted.Repository == repository
        && accepted.Ecosystem == ecosystem.Trim()
        && string.Equals(accepted.Package, package.Trim(), StringComparison.OrdinalIgnoreCase)
        && accepted.Advisory == advisory.Trim();
}
