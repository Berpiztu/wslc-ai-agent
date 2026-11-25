using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Images;

/// <summary>
/// The tags a repository has in its registry (<c>/v2/REPO/tags/list</c>),
/// asked as wslc's pull asks (<see cref="RegistryClient"/>): what Pull and
/// Update image offer under their tag. <c>latest</c> first, then the others
/// with the highest numbers first. An answer is kept a few minutes, by the
/// repository.
/// </summary>
public sealed partial class RegistryTags(RegistryClient registry, ILogger<RegistryTags> logger)
{
    /// <summary>The most pages of tags read from one repository: a registry hands them out a thousand at a time.</summary>
    private const int MaxPages = 10;

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, RegistryImageTags Answer)> _kept = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tags the repository of <paramref name="image"/> has in its registry, its own tag left aside.</summary>
    public async Task<RegistryImageTags> OfAsync(string image, CancellationToken cancellationToken = default)
    {
        var repository = ImageReference.RepositoryOf(ImageReference.Normalize(image));
        if (_kept.TryGetValue(repository, out var kept) && DateTimeOffset.UtcNow - kept.At < RegistryClient.KeptFor)
        {
            return kept.Answer;
        }

        var answer = await AskAsync(repository, cancellationToken);
        _kept[repository] = (DateTimeOffset.UtcNow, answer);
        return answer;
    }

    private async Task<RegistryImageTags> AskAsync(string reference, CancellationToken cancellationToken)
    {
        RegistryImageTags Said(string note) => new(reference, [], note);
        if (reference.Length == 0)
        {
            return Said("no repository named");
        }

        var (host, repository, _) = RegistryClient.Where(reference);
        try
        {
            var tags = new List<string>();
            var (first, token, login) = await registry.GetPullingAsync(host, $"https://{host}/v2/{repository}/tags/list?n=1000", ["application/json"], repository, cancellationToken);
            HttpResponseMessage? page = first;
            for (var read = 1; page is not null; read++)
            {
                using var answer = page;
                if (answer.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return Said(login ? RegistryClient.RefusedLogin : "the registry asks for a login (wslc login)");
                }

                if (answer.StatusCode == HttpStatusCode.NotFound)
                {
                    return Said("the registry has no such repository");
                }

                answer.EnsureSuccessStatusCode();
                var body = await RegistryClient.ReadJsonAsync(answer, cancellationToken);
                if (body.TryGetProperty("tags", out var listed) && listed.ValueKind == JsonValueKind.Array)
                {
                    tags.AddRange(listed.EnumerateArray().Select(tag => tag.GetString() ?? "").Where(tag => tag.Length > 0));
                }

                page = read < MaxPages && NextPage(answer) is { } next
                    ? await registry.GetAsync(new Uri(new Uri($"https://{host}"), next).ToString(), ["application/json"], token, cancellationToken)
                    : null;
            }

            return new RegistryImageTags(reference, [.. tags.Distinct(StringComparer.Ordinal).Order(TagOrder.Instance)], "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException)
        {
            logger.LogInformation("the registry of {Repository} could not list its tags: {Message}", reference, ex.Message);
            return Said("the registry could not be asked");
        }
    }

    /// <summary>The next page a registry's answer points to (<c>Link: &lt;/v2/…/tags/list?last=…&gt;; rel="next"</c>); null on the last.</summary>
    private static string? NextPage(HttpResponseMessage page) =>
        page.Headers.TryGetValues("Link", out var links)
            ? links.Select(link => NextLink().Match(link)).FirstOrDefault(match => match.Success)?.Groups["url"].Value
            : null;

    [GeneratedRegex(@"<(?<url>[^>]+)>\s*;\s*rel=""?next""?", RegexOptions.IgnoreCase)]
    private static partial Regex NextLink();

    /// <summary>
    /// <c>latest</c> first, then the tags with the highest numbers first, a run of digits read as a number
    /// (<c>3.20</c> before <c>3.9</c>, <c>1.10-alpine</c> before <c>1.9-alpine</c>); words in their order after numbers.
    /// </summary>
    internal sealed partial class TagOrder : IComparer<string>
    {
        public static readonly TagOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= "";
            y ??= "";
            if (x == y)
            {
                return 0;
            }

            if (x == ImageReference.Latest || y == ImageReference.Latest)
            {
                return x == ImageReference.Latest ? -1 : 1;
            }

            var xs = Piece().Matches(x);
            var ys = Piece().Matches(y);
            for (var i = 0; i < Math.Min(xs.Count, ys.Count); i++)
            {
                var (a, b) = (xs[i].Value, ys[i].Value);
                var (aNumber, bNumber) = (char.IsAsciiDigit(a[0]), char.IsAsciiDigit(b[0]));
                var order = aNumber && bNumber ? CompareNumbers(b, a)
                    : aNumber != bNumber ? (aNumber ? -1 : 1)
                    : string.CompareOrdinal(a, b);
                if (order != 0)
                {
                    return order;
                }
            }

            var shorter = xs.Count.CompareTo(ys.Count);
            return shorter != 0 ? shorter : string.CompareOrdinal(x, y);
        }

        /// <summary>Two runs of digits as numbers, however long.</summary>
        private static int CompareNumbers(string a, string b)
        {
            var (x, y) = (a.TrimStart('0'), b.TrimStart('0'));
            return x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
        }

        [GeneratedRegex(@"\d+|\D+")]
        private static partial Regex Piece();
    }
}
