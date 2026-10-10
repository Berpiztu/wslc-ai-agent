using System.Runtime.InteropServices;
using System.Text;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// What is printed, and how it looks. On a terminal: colour, and a mark
/// before what matters (a step, something done, a warning, a failure), so a
/// long run is read at a glance. Sent to a file or to another program, or
/// asked not to (<c>--ansi never</c>, NO_COLOR): the same words with no
/// colour and no mark, a warning said as "warning:", since what reads it then
/// is a script or a log. What a command says of how it went goes to the
/// error stream, so the output stays what was asked for and nothing else.
/// </summary>
internal static class Terminal
{
    public const string Bold = "\e[1m";
    public const string Gray = "\e[90m";
    /// <summary>A soft blue, of the 256 colours: the console's own blue is too dark to read on a dark window.</summary>
    public const string Blue = "\e[38;5;111m";
    public const string Red = "\e[91m";
    public const string Green = "\e[92m";
    public const string Yellow = "\e[93m";
    public const string Cyan = "\e[96m";
    public const string Magenta = "\e[95m";

    private const string Reset = "\e[0m";
    private const string EraseRight = "\e[K";
    private const string EraseBelow = "\e[J";

    private const int StandardOutput = -11;
    private const int StandardError = -12;
    private const uint VirtualTerminal = 0x0004;

    /// <summary>Whether each stream takes colour: one may be a terminal while the other is a file.</summary>
    private static bool _out;
    private static bool _err;

    /// <summary>What has been printed since <see cref="Hold"/> and is not on the screen yet; null when lines go straight to it.</summary>
    private static StringBuilder? _held;

    /// <summary><paramref name="ansi"/> is <c>auto</c> (or nothing), <c>always</c> or <c>never</c>, as Compose's own option.</summary>
    public static void Setup(string ansi)
    {
        if (ansi is not ("" or "auto" or "always" or "never"))
        {
            throw new UsageException($"--ansi takes auto, always or never, not '{ansi}'.");
        }

        var always = ansi == "always";
        var never = ansi == "never" || (!always && Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 });
        _out = !never && (always || !Console.IsOutputRedirected) && Understands(StandardOutput, always);
        _err = !never && (always || !Console.IsErrorRedirected) && Understands(StandardError, always);
    }

    /// <summary>
    /// The output is a terminal that takes colour, so it also takes lines
    /// drawn again in their place: work under way can be shown as it moves,
    /// and not only as a line a step.
    /// </summary>
    public static bool IsLive => _out;

    /// <summary>
    /// How many characters a line may have and stay one line. One less than
    /// the window's: a line that reaches its last column is wrapped by some
    /// consoles, and a wrapped line cannot be drawn again in its place.
    /// </summary>
    public static int Width
    {
        get
        {
            try
            {
                return Math.Max(40, Console.WindowWidth - 1);
            }
            catch (IOException)
            {
                return 100;
            }
        }
    }

    /// <summary>How many lines the window shows.</summary>
    public static int Height
    {
        get
        {
            try
            {
                return Console.WindowHeight;
            }
            catch (IOException)
            {
                return 24;
            }
        }
    }

    /// <summary>
    /// From now to <see cref="Release"/>, what is printed is kept and reaches
    /// the screen in one piece. It is how lines are drawn again in their
    /// place without the screen being seen half done: nothing is erased
    /// first, each line is written over the one that was there and what is
    /// left of the old one is erased after it, and whatever stood below the
    /// last line goes when the piece ends. Erasing first and writing after
    /// showed as a flash at every drawing. Only on a terminal: elsewhere
    /// lines go as they come.
    /// </summary>
    public static void Hold()
    {
        if (_out)
        {
            _held ??= new StringBuilder();
        }
    }

    /// <summary>What was kept goes to the screen, and what stood below it is erased.</summary>
    public static void Release()
    {
        if (_held is { } held)
        {
            _held = null;
            Console.Out.Write(held.Append(EraseBelow).ToString());
        }
    }

    /// <summary>The cursor back up to the first of the last <paramref name="lines"/> lines printed, to write over them. Within a <see cref="Hold"/>.</summary>
    public static void Rewind(int lines)
    {
        if (lines > 0)
        {
            _held?.Append($"\e[{lines}A\r");
        }
    }

    /// <summary>The cursor shown or not: it is hidden while lines are being drawn again, where it would blink all over them.</summary>
    public static void Cursor(bool shown)
    {
        if (_out)
        {
            Console.Out.Write(shown ? "\e[?25h" : "\e[?25l");
        }
    }

    public static void Line(string text)
    {
        if (_held is { } held)
        {
            held.Append(text).Append(EraseRight).Append("\r\n");
        }
        else
        {
            Console.WriteLine(text);
        }
    }

    /// <summary>What is being done now.</summary>
    public static void Step(string text) => Line(_out ? $"{Cyan}▸{Reset} {Bold}{text}{Reset}" : text);

    /// <summary>Something that went well.</summary>
    public static void Done(string text) => Line(_out ? $"{Green}✔{Reset} {text}" : text);

    /// <summary>What is worth having and not worth the eye: the command a step ran.</summary>
    public static void Faint(string text) => Line(_out ? $"{Gray}{text}{Reset}" : text);

    public static void Warning(string text) => ErrorLine(_err ? $"{Yellow}⚠ {text}{Reset}" : $"warning: {text}");

    /// <summary>What the file asks for and will not get.</summary>
    public static void Lost(string text) => ErrorLine(_err ? $"{Red}✖ not supported:{Reset} {text}" : $"not supported: {text}");

    public static void Error(string text) => ErrorLine(_err ? $"{Red}✖ {text}{Reset}" : text);

    /// <summary>What follows a failure or a summary, in a lower voice.</summary>
    public static void Aside(string text) => ErrorLine(_err ? $"{Gray}{text}{Reset}" : text);

    /// <summary>
    /// A line for the error stream. While the output is being kept, what is
    /// kept goes first, so the two streams reach the screen in the order
    /// they were written; and on a terminal the line erases what is left of
    /// the one it is written over, as the kept ones do.
    /// </summary>
    private static void ErrorLine(string text)
    {
        if (_held is { Length: > 0 } held)
        {
            Console.Out.Write(held.ToString());
            held.Clear();
        }

        Console.Error.WriteLine(_held is not null && _err ? text + EraseRight : text);
    }

    /// <summary>A piece of a line in a colour, for the output or (<paramref name="onError"/>) for the error stream; as it is where that stream takes none.</summary>
    public static string Tint(string text, string style, bool onError = false) =>
        style.Length > 0 && (onError ? _err : _out) ? $"{style}{text}{Reset}" : text;

    /// <summary>
    /// The stream reads the colour codes instead of printing them. Windows'
    /// console does once it is told to, which a new terminal already is and
    /// an old console window is not. A stream that is no console at all (a
    /// file, a pipe) takes them only when they were insisted on.
    /// </summary>
    private static bool Understands(int stream, bool insisted)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var handle = GetStdHandle(stream);
        if (!GetConsoleMode(handle, out var mode))
        {
            return insisted;
        }

        return (mode & VirtualTerminal) != 0 || SetConsoleMode(handle, mode | VirtualTerminal);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int stream);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(nint handle, uint mode);
}
