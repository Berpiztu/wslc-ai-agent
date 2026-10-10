using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WslcAgent.Server.Host;

/// <summary>
/// Windows' own Open dialog, put on the agent's desktop for whoever sits at
/// it: the one way a file is chosen with its path, which a browser never
/// hands over. The dialog is owned by the window in front when it is asked
/// for, the browser the user has just clicked in, so it opens over that
/// window and not behind it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HostFileDialog
{
    /// <summary>The longest path the dialog can hand back, in characters.</summary>
    private const int PathChars = 4096;

    // OPENFILENAME flags (commdlg.h).
    private const int HideReadOnly = 0x00000004;
    private const int KeepWorkingFolder = 0x00000008;
    private const int PathMustExist = 0x00000800;
    private const int FileMustExist = 0x00001000;
    private const int ExplorerStyle = 0x00080000;

    /// <summary>
    /// The path of the file chosen, or null when the dialog was cancelled.
    /// <paramref name="kinds"/> is the list of kinds as Windows writes it,
    /// name and pattern in turn: <c>Compose files|*.yaml;*.yml|All files|*.*</c>.
    /// </summary>
    public static Task<string?> OpenAsync(string title, string kinds, string folder = "")
    {
        var owner = GetForegroundWindow();
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The dialog runs its own message loop, so it has a thread of its own, single-threaded as the shell asks.
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Show(owner, title, kinds, folder));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "host-open-file",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static string? Show(IntPtr owner, string title, string kinds, string folder)
    {
        var buffer = Marshal.AllocHGlobal(PathChars * sizeof(char));
        try
        {
            // An empty name: the dialog opens with nothing typed in it.
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = kinds.Replace('|', '\0') + "\0",
                File = buffer,
                MaxFile = PathChars,
                InitialDir = folder.Length > 0 && Directory.Exists(folder) ? folder : null,
                Title = title,

                // The working folder is the agent's own: choosing a file must not move it.
                Flags = ExplorerStyle | FileMustExist | PathMustExist | KeepWorkingFolder | HideReadOnly,
            };
            if (GetOpenFileName(ref dialog))
            {
                return Marshal.PtrToStringUni(buffer);
            }

            // False is Cancel, unless the dialog says it could not be shown at all.
            var error = CommDlgExtendedError();
            return error == 0 ? null : throw new InvalidOperationException($"Windows could not show its Open dialog (error 0x{error:X4}).");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>OPENFILENAMEW, field for field (commdlg.h).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public string? Filter;
        public string? CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public string? FileTitle;
        public int MaxFileTitle;
        public string? InitialDir;
        public string? Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public string? DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public string? TemplateName;
        public IntPtr Reserved;
        public int ReservedFlags;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern int CommDlgExtendedError();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();
}
