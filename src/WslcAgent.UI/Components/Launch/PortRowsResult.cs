namespace WslcAgent.UI.Components.Launch;

/// <summary>What the Ports popup hands back: the <c>--publish</c> values and the agent's public names, <c>containerPort:prefix</c>, one per ticked row.</summary>
public sealed record PortRowsResult(IReadOnlyList<string> Publish, IReadOnlyList<string> PublicNames);
