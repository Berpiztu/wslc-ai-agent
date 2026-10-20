namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// One of the events the agent heard from <c>wslc events</c>, in
/// <c>GET /api/v1/events/recent</c>: what happened, to what, and when.
/// </summary>
/// <param name="Time">When the CLI says it happened.</param>
/// <param name="Type">The object's kind: <c>container</c>, <c>image</c>, <c>network</c>, <c>volume</c> (wslc 3.0.4 on).</param>
/// <param name="Action">What happened to it: <c>start</c>, <c>stop</c>, <c>die</c>, <c>kill</c>, <c>connect</c>, <c>disconnect</c>, <c>health_status: unhealthy</c>, a volume's <c>create</c>, <c>mount</c>, <c>unmount</c>, <c>destroy</c>, <c>prune</c>, …</param>
/// <param name="Id">The object it is about, by its full id; empty on an aggregate <c>prune</c>.</param>
/// <param name="Name">Its name, when the event carries one, and a volume's, which is its id; null otherwise.</param>
/// <param name="ExitCode">A container's exit code, on its <c>die</c> or <c>stop</c>; null on anything else.</param>
public sealed record WslcEventEntry(DateTimeOffset Time, string Type, string Action, string Id, string? Name, int? ExitCode);
