namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/health</c>.</summary>
/// <param name="Status">Always <c>"ok"</c> when the agent answers.</param>
/// <param name="Version">Version of the running agent.</param>
/// <param name="Build">
/// The id of the agent's own assembly, which is new with every compilation —
/// the version is not: it stays <c>0.1.59</c> through a hundred builds. A
/// client compares it with the one it started against and knows that what it
/// is talking to is no longer what it was served by. Empty from an agent too
/// old to say.
/// </param>
/// <param name="Development">A development build, whose UI is rebuilt under the user all day; a client may take a new one without asking.</param>
/// <param name="ApiLevel">The level of the API the agent speaks (<see cref="ApiCompatibility.Level"/>); 0 from an agent too old to say.</param>
/// <param name="MinimumClientApiLevel">The oldest client level the agent still serves (<see cref="ApiCompatibility.MinimumClientLevel"/>).</param>
public sealed record HealthResponse(string Status, string Version, string Build = "", bool Development = false, int ApiLevel = 0, int MinimumClientApiLevel = 0);
