namespace WslcAgent.UI.Components;

/// <summary>
/// Settings → Testing: behave as a client that is not at the agent's machine, so
/// Open with browser opens the host browser window instead of a new tab at
/// 127.0.0.1, and the remote path can be tried from the agent's own desktop. The
/// switch is kept on the agent (<c>/api/v1/testing</c>) and stays as it was
/// left; this holds the value the layout read at start and Settings last saved.
/// </summary>
public sealed class SimulatedRemote
{
    public bool Enabled { get; set; }
}
