namespace WslcAgent.UI.Components;

/// <summary>A verb as a line about it reads: "Stopping" while it runs, "stopped" once it has.</summary>
public static class VerbWords
{
    /// <summary>What a verb is doing right now: Starting, Stopping, Removing.</summary>
    public static string Gerund(string verb) => verb switch
    {
        "stop" => "Stopping",
        _ when verb.EndsWith('e') => char.ToUpperInvariant(verb[0]) + verb[1..^1] + "ing",
        _ => char.ToUpperInvariant(verb[0]) + verb[1..] + "ing",
    };

    /// <summary>What a verb has done: started, stopped, removed.</summary>
    public static string PastTense(string verb) => verb switch
    {
        "stop" => "stopped",
        _ when verb.EndsWith('e') => verb + "d",
        _ => verb + "ed",
    };
}
