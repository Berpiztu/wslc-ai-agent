namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// What the agent sends over <c>/api/v1/events/stream</c>: the kinds of thing
/// that changed since the last notice, so a screen listing one of them reads it
/// again. Never the objects themselves — an event says a list is stale, and the
/// list is where the data comes from.
/// </summary>
/// <param name="Kinds">
/// The kinds, as <c>wslc</c> names them: <c>container</c>, <c>network</c>,
/// <c>image</c>, <c>volume</c> — and <c>session</c>, the agent's own: a
/// session was started, stopped or chosen, from any client, or its event
/// stream died with it — and <c>agent-update</c>:
/// the agent's own update changed state (announced, cancelled, installing),
/// so every client reads it and shows or takes down its countdown — and
/// <c>notification</c>: the agent raised a notification, and whoever shows
/// them reads <c>GET /api/v1/notifications</c> after the last it saw.
/// </param>
/// <param name="Complete">
/// Everything is stale, not only the kinds named: the agent has just started
/// listening, or it lost the stream and what happened in between happened in
/// silence. A stopped session takes its containers down without one word of
/// warning on the way out, so a lost stream is never a quiet moment.
/// </param>
public sealed record ChangeNotice(IReadOnlyList<string> Kinds, bool Complete = false)
{
    /// <summary>Every kind at once: what a listener is told when it cannot trust what it has.</summary>
    public static readonly ChangeNotice Everything = new([Container, Network, Image, Volume, Session], Complete: true);

    /// <summary>The session alone changed: every client reads it again, so a stop made on one reaches the others without any of them polling.</summary>
    public static readonly ChangeNotice SessionChanged = new([Session]);

    public const string Container = "container";
    public const string Network = "network";
    public const string Image = "image";
    public const string Volume = "volume";
    public const string Session = "session";
    public const string AgentUpdate = "agent-update";
    public const string Notification = "notification";

    /// <summary>True when a screen listing <paramref name="kind"/> has to read it again.</summary>
    public bool Touches(string kind) => Complete || Kinds.Contains(kind);

    /// <summary>
    /// Whether <c>wslc</c> reports this kind at all. Its event store knows
    /// containers, images and networks and nothing else, so a list of volumes
    /// can never be told and keeps reading on its own clock. Read from
    /// <c>EventStore.cpp</c> at tag 2.9.13, not from what has been seen happen.
    /// </summary>
    public static bool IsReported(string kind) => kind is Container or Network or Image;
}
