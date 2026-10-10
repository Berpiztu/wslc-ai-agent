namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// One of the events the agent heard from <c>wslc events</c>, in
/// <c>GET /api/v1/events/recent</c>: what happened, to what, and when.
/// </summary>
/// <param name="Time">When the CLI says it happened.</param>
/// <param name="Type">The object's kind: <c>container</c>, <c>image</c>, <c>network</c> (wslc reports no volume events).</param>
/// <param name="Action">What happened to it: <c>start</c>, <c>stop</c>, <c>die</c>, <c>kill</c>, <c>connect</c>, <c>pull</c>, <c>health_status: unhealthy</c>, …</param>
/// <param name="Id">The object it is about, by its full id.</param>
/// <param name="Name">Its name, when the event carries one; null otherwise.</param>
/// <param name="ExitCode">A container's exit code, on its <c>die</c> or <c>stop</c>; null on anything else.</param>
public sealed record WslcEventEntry(DateTimeOffset Time, string Type, string Action, string Id, string? Name, int? ExitCode);
