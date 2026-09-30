namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>GET</c> and <c>PUT /api/v1/testing</c>: switches for trying the product
/// from the agent's own machine.
/// </summary>
/// <param name="SimulateRemote">Clients behave as if they were on another machine: Open with browser opens the host browser window instead of a new tab. Stays until switched off.</param>
public sealed record TestingSettings(bool SimulateRemote);
