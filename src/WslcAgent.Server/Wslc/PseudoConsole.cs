using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// A Windows pseudo console (ConPTY) around one child process. It is what
/// makes an exec session a terminal instead of a pair of pipes: the shell sees
/// a tty, so it draws its prompt, echoes what is typed, keeps job control, and
/// programs that ask whether they talk to a terminal (<c>ls</c> and its
/// columns, <c>vim</c>, <c>top</c>) behave as they do in a console window.
/// Output arrives as VT sequences, which is exactly what xterm.js renders.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PseudoConsole : IDisposable
{
    private const int ExtendedStartupInfoPresent = 0x00080000;
    private const nint PseudoConsoleAttribute = 0x00020016;
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const int StandardError = -12;

    private static readonly Lock StartGate = new();

    private readonly nint _handle;
    private readonly SafeFileHandle _process;
    private readonly nint _attributes;
    private bool _closed;

    private PseudoConsole(nint handle, SafeFileHandle process, nint attributes, int processId, Stream input, Stream output)
    {
        _handle = handle;
        _process = process;
        _attributes = attributes;
        ProcessId = processId;
        Input = input;
        Output = output;
    }

    public int ProcessId { get; }

    /// <summary>What the user types goes in here; the shell echoes it back through <see cref="Output"/>.</summary>
    public Stream Input { get; }

    /// <summary>Everything the terminal shows, VT sequences included. There is no separate error stream: a console has one screen.</summary>
    public Stream Output { get; }

    /// <summary>Starts <paramref name="commandLine"/> attached to a new pseudo console of the given size.</summary>
    public static PseudoConsole Start(string commandLine, int columns, int rows)
    {
        // Two anonymous pipes: the console reads one and writes the other, and
        // this process keeps the opposite ends.
        if (!CreatePipe(out var consoleReads, out var weWrite, nint.Zero, 0) ||
            !CreatePipe(out var weRead, out var consoleWrites, nint.Zero, 0))
        {
            throw new IOException($"Could not create the terminal pipes ({Marshal.GetLastWin32Error()}).");
        }

        var size = new Coord { X = (short)Math.Clamp(columns, 1, short.MaxValue), Y = (short)Math.Clamp(rows, 1, short.MaxValue) };
        var created = CreatePseudoConsole(size, consoleReads, consoleWrites, 0, out var handle);
        // The console owns its ends now; holding them open would keep the
        // output pipe alive after the child exits and the reader would hang.
        CloseHandle(consoleReads);
        CloseHandle(consoleWrites);
        if (created != 0)
        {
            CloseHandle(weWrite);
            CloseHandle(weRead);
            throw new IOException($"Could not create a pseudo console (0x{created:X8}).");
        }

        var attributes = nint.Zero;
        try
        {
            attributes = AttributeListWith(handle);
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>() },
                Attributes = attributes,
            };

            if (!StartDetachedFromOurHandles(commandLine, ref startup, out var information))
            {
                throw new IOException($"Could not start the terminal process ({Marshal.GetLastWin32Error()}).");
            }

            CloseHandle(information.Thread);
            return new PseudoConsole(
                handle,
                new SafeFileHandle(information.Process, ownsHandle: true),
                attributes,
                information.ProcessId,
                new FileStream(new SafeFileHandle(weWrite, ownsHandle: true), FileAccess.Write, bufferSize: 1, isAsync: false),
                new FileStream(new SafeFileHandle(weRead, ownsHandle: true), FileAccess.Read, bufferSize: 4096, isAsync: false));
        }
        catch
        {
            if (attributes != nint.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }

            ClosePseudoConsole(handle);
            CloseHandle(weWrite);
            CloseHandle(weRead);
            throw;
        }
    }

    /// <summary>Tells the shell the window changed, so full-screen programs redraw at the new size.</summary>
    public void Resize(int columns, int rows)
    {
        if (_closed)
        {
            return;
        }

        ResizePseudoConsole(_handle, new Coord { X = (short)Math.Clamp(columns, 1, short.MaxValue), Y = (short)Math.Clamp(rows, 1, short.MaxValue) });
    }

    public bool HasExited => _process.IsInvalid || WaitForSingleObject(_process.DangerousGetHandle(), 0) == 0;

    public int ExitCode => GetExitCodeProcess(_process.DangerousGetHandle(), out var code) ? code : 0;

    public void Kill()
    {
        if (!_closed && !_process.IsInvalid && !HasExited)
        {
            TerminateProcess(_process.DangerousGetHandle(), 1);
        }
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        // Closing the console ends the session: the child sees its terminal go.
        ClosePseudoConsole(_handle);
        if (_attributes != nint.Zero)
        {
            DeleteProcThreadAttributeList(_attributes);
            Marshal.FreeHGlobal(_attributes);
        }

        Input.Dispose();
        Output.Dispose();
        _process.Dispose();
    }

    /// <summary>
    /// Starts the child with this process's standard handles out of the way.
    /// A child that is given none of its own takes the parent's, and the
    /// agent's are redirected (a log file, a service pipe): the shell then
    /// wrote there instead of into the pseudo console — the terminal stayed
    /// empty and <c>wslc</c> answered <c>ERROR_INVALID_HANDLE</c>. With no
    /// handles to take, it attaches to the console it was given. The swap is
    /// only for the length of the call, and .NET's own <c>Console</c> keeps
    /// the stream it already opened, so nothing else notices; the lock keeps
    /// two sessions from starting inside each other's window.
    /// </summary>
    private static bool StartDetachedFromOurHandles(string commandLine, ref StartupInfoEx startup, out ProcessInformation information)
    {
        lock (StartGate)
        {
            var input = GetStdHandle(StandardInput);
            var output = GetStdHandle(StandardOutput);
            var error = GetStdHandle(StandardError);
            SetStdHandle(StandardInput, nint.Zero);
            SetStdHandle(StandardOutput, nint.Zero);
            SetStdHandle(StandardError, nint.Zero);
            try
            {
                return CreateProcessW(null, new string(commandLine), nint.Zero, nint.Zero, false, ExtendedStartupInfoPresent, nint.Zero, null, ref startup, out information);
            }
            finally
            {
                SetStdHandle(StandardInput, input);
                SetStdHandle(StandardOutput, output);
                SetStdHandle(StandardError, error);
            }
        }
    }

    /// <summary>The one attribute a ConPTY child needs: the console it is attached to.</summary>
    private static nint AttributeListWith(nint console)
    {
        var size = nint.Zero;
        InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(list, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(list);
            throw new IOException($"Could not prepare the terminal process attributes ({Marshal.GetLastWin32Error()}).");
        }

        if (!UpdateProcThreadAttribute(list, 0, PseudoConsoleAttribute, console, nint.Size, nint.Zero, nint.Zero))
        {
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            throw new IOException($"Could not attach the process to the pseudo console ({Marshal.GetLastWin32Error()}).");
        }

        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out nint readPipe, out nint writePipe, nint attributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int which);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int which, nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, nint input, nint output, uint flags, out nint console);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(nint console, Coord size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(nint console);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int attributeCount, int flags, ref nint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previousValue, nint returnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(nint list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? applicationName,
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        nint processAttributes,
        nint threadAttributes,
        bool inheritHandles,
        int creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint process, out int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(nint process, uint exitCode);
}
