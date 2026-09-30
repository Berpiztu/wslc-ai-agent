namespace WslcAgent.UI.Components;

/// <summary>
/// The lines of the host's four charts, as their legends name them: the
/// dashboard's chart objects draw the same ones.
/// </summary>
public static class HostSeries
{
    public static readonly LineChart.Line[] Cpu = [new("Aggregate CPU %")];

    public static readonly LineChart.Line[] Memory = [new("Aggregate memory used")];

    public static readonly LineChart.Line[] Disk = [new("Read"), new("Write", Secondary: true)];

    public static readonly LineChart.Line[] Network = [new("RX"), new("TX", Secondary: true)];
}
