using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using RecordVideoAudio.GMTPC.Services;

namespace RecordVideoAudio.GMTPC.Android;

[Activity(
    Label = "RecordVideoAudio.GMTPC.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        CrashHandler.Initialize(this);
        try
        {
            base.OnCreate(savedInstanceState);
        }
        catch (Exception ex)
        {
            CrashHandler.ReportCrash(ex);
            return;
        }

        AutoUpdateService.AndroidInstallHandler = (apkPath) =>
        {
            try
            {
                var file = new Java.IO.File(apkPath);
                var intent = new Intent(Intent.ActionView);
                intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

                var builder = new StrictMode.VmPolicy.Builder();
                StrictMode.SetVmPolicy(builder.Build());

                var uri = global::Android.Net.Uri.FromFile(file);
                intent.SetDataAndType(uri, "application/vnd.android.package-archive");
                StartActivity(intent);
            }
            catch
            {
                AutoUpdateService.AndroidBrowserHandler?.Invoke(AutoUpdateService.AndroidDownloadUrl);
            }
        };

        AutoUpdateService.AndroidBrowserHandler = (url) =>
        {
            try
            {
                var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
                intent.SetFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
            catch { }
        };
    }
}
