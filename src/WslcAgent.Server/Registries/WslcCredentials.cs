using System.Runtime.InteropServices;
using System.Text;

namespace WslcAgent.Server.Registries;

/// <summary>
/// The login <c>wslc login</c> kept for a registry, read where wslc keeps it:
/// Windows Credential Manager, a generic credential named
/// <c>wslc-credential/HOST</c> (Docker Hub's <c>wslc-credential/https://index.docker.io/v1/</c>),
/// its user and its secret, the password or, when the user is <c>&lt;token&gt;</c>,
/// the identity token the registry handed out. The agent reads it, as wslc's
/// own pull does, only to ask that registry what a pull may read (a
/// repository's tags, an image's version); it is never written, kept,
/// logged or sent anywhere else. A login wslc keeps in a file
/// (its <c>credentialStore</c> setting) is not read; nor is any off Windows.
/// </summary>
public static class WslcCredentials
{
    private const string Prefix = "wslc-credential/";

    /// <summary>The user wslc keeps for an identity token, as Docker does.</summary>
    public const string TokenUser = "<token>";

    /// <summary>The name wslc keeps Docker Hub's login under.</summary>
    private const string DockerHubKey = "https://index.docker.io/v1/";

    private const int GenericCredential = 1;

    /// <summary>The login kept for the registry at <paramref name="host"/>; null when there is none, or it cannot be read.</summary>
    public static (string User, string Secret)? For(string host)
    {
        if (!OperatingSystem.IsWindows() || host.Length == 0)
        {
            return null;
        }

        var key = host is "registry-1.docker.io" or "docker.io" or "index.docker.io" ? DockerHubKey : host.ToLowerInvariant();
        if (!CredRead(Prefix + key, GenericCredential, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.BlobSize == 0 || credential.Blob == IntPtr.Zero)
            {
                return null;
            }

            var secret = new byte[credential.BlobSize];
            Marshal.Copy(credential.Blob, secret, 0, secret.Length);
            return (credential.UserName ?? "", Encoding.UTF8.GetString(secret));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public int BlobSize;
        public IntPtr Blob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
