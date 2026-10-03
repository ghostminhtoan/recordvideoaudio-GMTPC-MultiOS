using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;

namespace RecordVideoAudio.GMTPC.Android;

public static class CrashHandler
{
    private static bool _initialized = false;
    private static Context? _appContext;

    public static void Initialize(Context context)
    {
        if (_initialized) return;
        _initialized = true;
        _appContext = context.ApplicationContext ?? context;

        // 1. Mono/Android environment unhandled exceptions
        AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;

        // 2. .NET AppDomain unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        // 3. Task scheduler unobserved exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnAndroidUnhandledException(object? sender, RaiseThrowableEventArgs e)
    {
        Log.Error("GMTPC_CRASH", $"[AndroidEnvironment] Unhandled Exception: {e.Exception}");
        ReportCrash(e.Exception);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Error("GMTPC_CRASH", $"[AppDomain] Unhandled Exception: {ex}");
        ReportCrash(ex ?? new Exception($"Unknown error: {e.ExceptionObject}"));
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("GMTPC_CRASH", $"[TaskScheduler] Unobserved Exception: {e.Exception}");
        ReportCrash(e.Exception);
        e.SetObserved();
    }

    public static void ReportCrash(Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== GMTPC RECORD VIDEO AUDIO - THÔNG TIN SỰ CỐ ===");
            sb.AppendLine($"Thời gian: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Loại lỗi (Exception Type): {ex.GetType().FullName}");
            sb.AppendLine($"Thông điệp (Message): {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("--- THIẾT BỊ & NỀN TẢNG ---");
            sb.AppendLine($"Hãng: {Build.Manufacturer}");
            sb.AppendLine($"Model: {Build.Model}");
            sb.AppendLine($"Phiên bản Android: {Build.VERSION.Release} (API {Build.VERSION.SdkInt})");
            sb.AppendLine($"Cấu trúc CPU (ABI): {string.Join(", ", Build.SupportedAbis ?? Array.Empty<string>())}");
            sb.AppendLine();
            sb.AppendLine("--- CHI TIẾT NGUYÊN NHÂN (STACK TRACE) ---");
            sb.AppendLine(ex.ToString());

            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- INNER EXCEPTION ---");
                sb.AppendLine(ex.InnerException.ToString());
            }

            string fullError = sb.ToString();

            // Lưu log ra file trong bộ nhớ nội bộ của ứng dụng
            if (_appContext != null)
            {
                try
                {
                    var logDir = _appContext.GetExternalFilesDir(null) ?? _appContext.FilesDir;
                    if (logDir != null)
                    {
                        var logFile = Path.Combine(logDir.AbsolutePath, "crash_log.txt");
                        File.WriteAllText(logFile, fullError, Encoding.UTF8);
                    }
                }
                catch { }
            }

            // Khởi chạy CrashReportActivity
            if (_appContext != null)
            {
                var intent = new Intent(_appContext, typeof(CrashReportActivity));
                intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                intent.PutExtra(CrashReportActivity.ExtraErrorDetails, fullError);
                _appContext.StartActivity(intent);
            }
        }
        catch (Exception reportEx)
        {
            Log.Error("GMTPC_CRASH", $"Lỗi khi hiển thị CrashReport: {reportEx}");
        }
    }
}
