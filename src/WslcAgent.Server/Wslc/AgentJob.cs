using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WslcAgent.Server.Wslc;

/// <summary>
/// What the agent starts for itself ends with it. The agent ends a command it
/// is waiting on when it stops in order, and only then: closed by an
/// installer, or gone in a crash, it left behind whatever was still running,
/// and the event stream (<c>wslc events</c>) always is. Each agent that was
/// replaced left one more of them listening to nobody.
/// <para>
/// A Windows job does it whichever way the agent goes: the processes put in
/// it are ended when its last handle closes, and the agent holds the only
/// one, which Windows closes with the process. Only what is put in it is
/// held (<see cref="Hold"/>), never the agent itself: what has to outlive it,
/// the installer that replaces it or a terminal window opened for the user,
/// is started as before and stays out. Containers are not in it either: they
/// run under WSL's own service, not under the <c>wslc</c> that asked for them.
/// </para>
/// </summary>
internal static class AgentJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x00002000;

    /// <summary>The job, made the first time a process is held and kept for as long as the agent runs; zero when Windows would not make it.</summary>
    private static readonly Lazy<nint> Job = new(Create);

    /// <summary>Windows's error when it would not make the job; every process is then answered with it.</summary>
    private static int _refused;

    /// <summary>
    /// Puts a process the agent has just started among those that end with
    /// it. Zero when it is held, or away from Windows, where there is nothing
    /// of the kind to do; else Windows's error. A command already over by now
    /// is refused, and has nothing left to end.
    /// </summary>
    public static int Hold(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            if (Job.Value == nint.Zero)
            {
                return _refused;
            }

            return AssignProcessToJobObject(Job.Value, process.Handle) ? 0 : Marshal.GetLastPInvokeError();
        }
        catch (InvalidOperationException)
        {
            // Over, and let go of, before it could be held.
            return 0;
        }
    }

    private static nint Create()
    {
        // No name and no security attributes: no other process can open it, and no child inherits
        // its handle, so the agent's is the last one whatever it has started.
        var job = CreateJobObjectW(nint.Zero, null);
        if (job == nint.Zero)
        {
            _refused = Marshal.GetLastPInvokeError();
            return nint.Zero;
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        if (SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            return job;
        }

        _refused = Marshal.GetLastPInvokeError();
        CloseHandle(job);
        return nint.Zero;
    }

    /// <summary><c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    /// <summary><c>IO_COUNTERS</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    /// <summary><c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, ref ExtendedLimits information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
