namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>POST /api/v1/images/build</c>. Empty strings and false mean "not given".</summary>
/// <param name="Path">The build context folder on the agent's machine.</param>
/// <param name="Tag">The built image's name, e.g. <c>myapp:latest</c>.</param>
/// <param name="Dockerfile">Relative to the context, or an absolute path.</param>
/// <param name="Target">The stage of a multi-stage build.</param>
/// <param name="BuildArgs"><c>KEY=VALUE</c>, one per line.</param>
/// <param name="Labels"><c>key=value</c>, one per line.</param>
/// <param name="Pull">Pull newer base images.</param>
public sealed record BuildImageRequest(
    string Path,
    string Tag = "",
    string Dockerfile = "",
    string Target = "",
    string BuildArgs = "",
    string Labels = "",
    bool NoCache = false,
    bool Pull = false);

/// <summary>A build the agent runs and the page follows.</summary>
/// <param name="Job">Opaque id to follow or cancel it with.</param>
/// <param name="State"><c>running</c>, <c>done</c>, <c>error</c> or <c>cancelled</c>.</param>
/// <param name="Output">The build's lines so far, the last 2000.</param>
/// <param name="Truncated">Earlier lines were dropped to keep the last 2000.</param>
/// <param name="Error">Why it failed; empty otherwise.</param>
public sealed record BuildJob(string Job, string State, string Tag, IReadOnlyList<string> Output, bool Truncated, string Error);

/// <summary>Answer of <c>POST /api/v1/images/load</c>.</summary>
/// <param name="Loaded">The image tags or ids the CLI reported loading.</param>
public sealed record LoadImagesResult(IReadOnlyList<string> Loaded, string Stdout, string Stderr);

/// <summary>A pull the agent runs; rows of <c>GET /api/v1/images/pulls</c>.</summary>
/// <param name="State"><c>running</c>, <c>success</c>, <c>error</c> or <c>cancelled</c>.</param>
/// <param name="Pct">How far, over every layer seen (downloading the first half, extracting the second).</param>
/// <param name="Status">The line to show: the layer step under way, or why it ended.</param>
/// <param name="Error">Why it failed or that it was cancelled; empty otherwise.</param>
public sealed record ImagePullState(string Image, string State, int Pct, string Status, string Error);

/// <summary>Body of <c>GET /api/v1/images/pulls/log</c>: the pull's console.</summary>
/// <param name="Log">The output as lines, terminal codes removed.</param>
/// <param name="Truncated">Only the last 200 000 characters are kept.</param>
/// <param name="Active">The pull is still running.</param>
public sealed record ImagePullLog(string Image, string Log, bool Truncated, int Pct, string Status, bool Active);

/// <summary>Answer of <c>POST /api/v1/images/pulls/cancel</c>: false when no pull of that image was running.</summary>
public sealed record CancelImagePullResult(bool Cancelled);
