namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Body of <c>GET /api/v1/images/defaults?reference=</c>: what an image sets
/// for every container made from it — its variables, command, entrypoint,
/// working folder, user and health check — as launch fields, so the Run form
/// shows them, and lets them be changed, before the container exists.
/// </summary>
/// <param name="Local">The image is on this machine. False, nothing can be read before a pull and <paramref name="Form"/> carries the reference alone.</param>
/// <param name="Form">The fields the image sets; every other field as a form left empty.</param>
public sealed record ImageDefaults(bool Local, ContainerLaunchRequest Form);
