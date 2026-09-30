using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace WslcAgent.Server.ClientPackages;

/// <summary>
/// <c>ProductVersion</c> of an MSI, read from its Property table through the
/// Windows Installer API: the version the installer will install.
/// </summary>
public static class MsiProductVersion
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;

    /// <summary>The version string, or empty when the file cannot be read or the host is not Windows.</summary>
    public static string Read(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return "";
        }

        return ReadOnWindows(path);
    }

    [SupportedOSPlatform("windows")]
    private static string ReadOnWindows(string path)
    {
        uint database = 0, view = 0, record = 0;
        try
        {
            if (MsiOpenDatabaseW(path, IntPtr.Zero, out database) != ErrorSuccess
                || MsiDatabaseOpenViewW(database, "SELECT `Value` FROM `Property` WHERE `Property`='ProductVersion'", out view) != ErrorSuccess
                || MsiViewExecute(view, 0) != ErrorSuccess
                || MsiViewFetch(view, out record) != ErrorSuccess)
            {
                return "";
            }

            // The first call sizes the buffer (ERROR_MORE_DATA), the second reads it.
            uint length = 0;
            var probe = MsiRecordGetStringW(record, 1, new StringBuilder(1), ref length);
            if (probe is not ErrorSuccess and not ErrorMoreData)
            {
                return "";
            }

            var buffer = new StringBuilder((int)length + 1);
            length = (uint)buffer.Capacity;
            return MsiRecordGetStringW(record, 1, buffer, ref length) == ErrorSuccess ? buffer.ToString().Trim() : "";
        }
        finally
        {
            foreach (var handle in new[] { record, view, database })
            {
                if (handle != 0)
                {
                    MsiCloseHandle(handle);
                }
            }
        }
    }

    // szPersist = NULL opens the database read-only (MSIDBOPEN_READONLY).
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out uint database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(uint database, string query, out uint view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(uint view, uint record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(uint view, out uint record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(uint record, uint field, StringBuilder value, ref uint length);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(uint handle);
}
