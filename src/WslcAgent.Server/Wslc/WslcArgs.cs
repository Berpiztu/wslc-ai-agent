namespace WslcAgent.Server.Wslc;

/// <summary>
/// Builds <c>wslc</c> argument lists from user input. Every value that ends
/// up on the command line passes through here, so nothing a user types can
/// turn into an option.
/// </summary>
public static class WslcArgs
{
    /// <summary>A required positional (name, id, reference). Empty or option-looking values are refused.</summary>
    public static string Require(string? value, string what)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text[0] == '-')
        {
            throw new ArgumentException($"A {what} is required.", what);
        }

        return text;
    }

    /// <summary><c>--flag VALUE</c> when the value is not empty; an option-looking value is refused.</summary>
    public static List<string> Option(this List<string> args, string flag, string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            return args;
        }

        if (text[0] == '-')
        {
            throw new ArgumentException($"Invalid value for {flag}.", flag);
        }

        args.Add(flag);
        args.Add(text);
        return args;
    }

    /// <summary><c>--flag VALUE</c> once per <c>KEY=value</c> pair in <paramref name="text"/> (<see cref="WslcAgent.ApiClient.ValueList.SplitPairs"/>).</summary>
    public static List<string> Pairs(this List<string> args, string flag, string? text)
    {
        foreach (var value in WslcAgent.ApiClient.ValueList.SplitPairs(text ?? ""))
        {
            args.Option(flag, value);
        }

        return args;
    }

    /// <summary><c>--flag</c> when <paramref name="on"/>.</summary>
    public static List<string> Flag(this List<string> args, string flag, bool on)
    {
        if (on)
        {
            args.Add(flag);
        }

        return args;
    }
}
