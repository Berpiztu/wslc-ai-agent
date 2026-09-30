namespace WslcAgent.Mcp;

/// <summary>What a finished <c>wslc</c> command printed, for a caller that reports it (a prune's reclaimed space).</summary>
public sealed record CommandOutput(string Stdout, string Stderr);
