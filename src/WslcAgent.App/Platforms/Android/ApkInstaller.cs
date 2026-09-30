using Android.Content;

namespace WslcAgent.App;

/// <summary>Hands a downloaded APK to the system package installer.</summary>
internal static class ApkInstaller
{
    /// <summary>
    /// False with a message for the user when the app may not install
    /// packages yet (the system settings page for that permission is opened)
    /// or the file is missing.
    /// </summary>
    public static bool TryLaunch(string path, out string reason)
    {
        reason = "";
        var context = Android.App.Application.Context;
        if (context is null)
        {
            reason = "Android context is not ready.";
            return false;
        }

        // Android 8 (API 26) made installing packages a permission of its own;
        // before it the install simply asks. The check the analyser reads.
        if (OperatingSystem.IsAndroidVersionAtLeast(26)
            && context.PackageManager is not null
            && !context.PackageManager.CanRequestPackageInstalls())
        {
            var settings = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources);
            settings.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
            settings.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settings);
            reason = "Allow WSLC AI Client to install unknown apps, then download again.";
            return false;
        }

        var file = new Java.IO.File(path);
        if (!file.Exists())
        {
            reason = "The downloaded APK is missing.";
            return false;
        }

        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileprovider", file);
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
        return true;
    }
}
