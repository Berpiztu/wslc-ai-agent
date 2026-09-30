namespace WslcAgent.ApiClient.Contracts;

/// <summary>Body of <c>GET /api/v1/events/status</c>.</summary>
/// <param name="Live">True while the agent has <c>wslc events</c> open and is told what changes; false while the screens read on their own clock.</param>
public sealed record EventStatus(bool Live);
