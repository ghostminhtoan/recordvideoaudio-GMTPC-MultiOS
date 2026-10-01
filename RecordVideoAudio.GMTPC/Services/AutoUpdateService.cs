using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RecordVideoAudio.GMTPC.Services;

public class UpdateCheckResult
{
    public bool IsSuccess { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string TagName { get; set; } = "release";
    public DateTime? RemoteReleaseDateUtc { get; set; }
    public long RemoteFileSize { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;
}

public class AutoUpdateService
{
    public static AutoUpdateService Instance { get; } = new();

    public const string WindowsDownloadUrl = "https://github.com/ghostminhtoan/recordvideoaudio-GMTPC-MultiOS/releases/download/release/RecordVideoAudio.GMTPC.exe";
    public const string AndroidDownloadUrl = "https://github.com/ghostminhtoan/recordvideoaudio-GMTPC-MultiOS/releases/download/release/RecordVideoAudio.GMTPC.apk";
    public const string GitHubReleaseApiUrl = "https://api.github.com/repos/ghostminhtoan/recordvideoaudio-GMTPC-MultiOS/releases/tags/release";
    public const string GitHubReleaseWebUrl = "https://github.com/ghostminhtoan/recordvideoaudio-GMTPC-MultiOS/releases/tag/release";

    // Hooks for Android Native Implementation
    public static Action<string>? AndroidInstallHandler { get; set; }
    public static Action<string>? AndroidBrowserHandler { get; set; }

    public string CurrentPlatformName
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "Windows (x64)";
            if (OperatingSystem.IsAndroid()) return "Android (.apk)";
            if (OperatingSystem.IsLinux()) return "Linux (x64)";
            return RuntimeInformation.OSDescription;
        }
    }

    public string TargetDownloadUrl
    {
        get
        {
            if (OperatingSystem.IsAndroid()) return AndroidDownloadUrl;
            if (OperatingSystem.IsWindows()) return WindowsDownloadUrl;
            return GitHubReleaseWebUrl;
        }
    }

    public string TargetFileName
    {
        get
        {
            if (OperatingSystem.IsAndroid()) return "RecordVideoAudio.GMTPC.apk";
            if (OperatingSystem.IsWindows()) return "RecordVideoAudio.GMTPC.exe";
            return "RecordVideoAudio.GMTPC";
        }
    }

    /// <summary>
    /// Checks for updates via GitHub Release API, with automatic HTTP HEAD fallback.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var result = new UpdateCheckResult
        {
            TargetFileName = TargetFileName,
            DownloadUrl = TargetDownloadUrl
        };

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GMTPC-RecordVideoAudio-AutoUpdate/1.0");

            // 1. Try querying GitHub Release API
            try
            {
                using var apiResponse = await client.GetAsync(GitHubReleaseApiUrl, ct);
                if (apiResponse.IsSuccessStatusCode)
                {
                    string json = await apiResponse.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("tag_name", out var tagElem))
                    {
                        result.TagName = tagElem.GetString() ?? "release";
                    }

                    if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElem.EnumerateArray())
                        {
                            string assetName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            bool match = false;

                            if (OperatingSystem.IsWindows() && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                match = true;
                            else if (OperatingSystem.IsAndroid() && assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                                match = true;

                            if (match)
                            {
                                if (asset.TryGetProperty("size", out var s))
                                    result.RemoteFileSize = s.GetInt64();

                                if (asset.TryGetProperty("updated_at", out var u) && DateTime.TryParse(u.GetString(), out var dt))
                                    result.RemoteReleaseDateUtc = dt.ToUniversalTime();

                                if (asset.TryGetProperty("browser_download_url", out var dl))
                                    result.DownloadUrl = dl.GetString() ?? result.DownloadUrl;

                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to HTTP HEAD request on the direct download URL
            }

            // 2. If API didn't provide size/date or was throttled, use HTTP HEAD
            if (result.RemoteFileSize <= 0 || result.RemoteReleaseDateUtc == null)
            {
                try
                {
                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, result.DownloadUrl);
                    using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (headResponse.IsSuccessStatusCode)
                    {
                        if (headResponse.Content.Headers.ContentLength.HasValue)
                        {
                            result.RemoteFileSize = headResponse.Content.Headers.ContentLength.Value;
                        }

                        if (headResponse.Content.Headers.LastModified.HasValue)
                        {
                            result.RemoteReleaseDateUtc = headResponse.Content.Headers.LastModified.Value.UtcDateTime;
                        }
                    }
                }
                catch { }
            }

            // 3. Determine if update is newer than local build
            result.IsSuccess = true;
            result.IsUpdateAvailable = CompareWithLocalVersion(result.RemoteReleaseDateUtc, result.RemoteFileSize);

            result.StatusMessage = result.IsUpdateAvailable
                ? "⚡ Có bản cập nhật mới nhất trên GitHub!"
                : "✅ Bạn đang sử dụng phiên bản cập nhật mới nhất.";

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.IsUpdateAvailable = false;
            result.StatusMessage = $"Không thể kết nối đến máy chủ cập nhật: {ex.Message}";
            return result;
        }
    }

    private bool CompareWithLocalVersion(DateTime? remoteDateUtc, long remoteSize)
    {
        if (OperatingSystem.IsWindows())
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                var fileInfo = new FileInfo(processPath);
                DateTime localDate = fileInfo.LastWriteTimeUtc;
                long localSize = fileInfo.Length;

                // If remote release is newer by more than 2 minutes, or size differs significantly (> 100KB)
                if (remoteDateUtc.HasValue && remoteDateUtc.Value > localDate.AddMinutes(2))
                {
                    return true;
                }

                if (remoteSize > 0 && Math.Abs(remoteSize - localSize) > 1024 * 100)
                {
                    return true;
                }
            }
        }

        // Default to offering update option if remote release exists
        return remoteDateUtc.HasValue || remoteSize > 0;
    }

    /// <summary>
    /// Downloads the update file with real-time percentage and transfer rate reporting.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(string downloadUrl, Action<double, string> progressCallback, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GMTPC-RecordVideoAudio-AutoUpdate/1.0");

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        string tempDir = Path.Combine(Path.GetTempPath(), "GMTPC_AutoUpdate");
        Directory.CreateDirectory(tempDir);

        string destPath = Path.Combine(tempDir, TargetFileName);
        if (File.Exists(destPath))
        {
            try { File.Delete(destPath); } catch { }
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        var sw = Stopwatch.StartNew();
        long lastReportBytes = 0;
        long lastReportMs = 0;
        double speedMbPerSec = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            long elapsedMs = sw.ElapsedMilliseconds;
            if (elapsedMs - lastReportMs >= 200 || (totalBytes > 0 && totalRead == totalBytes))
            {
                long deltaBytes = totalRead - lastReportBytes;
                double deltaSec = (elapsedMs - lastReportMs) / 1000.0;
                if (deltaSec > 0.05)
                {
                    speedMbPerSec = (deltaBytes / (1024.0 * 1024.0)) / deltaSec;
                }

                double percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;
                string progressText = totalBytes > 0
                    ? $"{totalRead / (1024.0 * 1024.0):F1} MB / {totalBytes / (1024.0 * 1024.0):F1} MB ({speedMbPerSec:F1} MB/s)"
                    : $"{totalRead / (1024.0 * 1024.0):F1} MB ({speedMbPerSec:F1} MB/s)";

                progressCallback(percent, progressText);
                lastReportBytes = totalRead;
                lastReportMs = elapsedMs;
            }
        }

        return destPath;
    }

    /// <summary>
    /// Executes the platform-specific installer or restart procedure.
    /// </summary>
    public void ApplyUpdateAndRestart(string downloadedFilePath)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsUpdateAndRestart(downloadedFilePath);
        }
        else if (OperatingSystem.IsAndroid())
        {
            if (AndroidInstallHandler != null)
            {
                AndroidInstallHandler(downloadedFilePath);
            }
            else
            {
                OpenUrl(AndroidDownloadUrl);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            OpenUrl(GitHubReleaseWebUrl);
        }
    }

    private static void ApplyWindowsUpdateAndRestart(string downloadedFilePath)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(downloadedFilePath)) return;

        string tempDir = Path.GetDirectoryName(downloadedFilePath) ?? Path.GetTempPath();
        string batPath = Path.Combine(tempDir, "apply_update.bat");

        string script = $@"@echo off
chcp 65001 > nul
echo Dang cap nhat Record Video Audio GMTPC...
timeout /t 1 /nobreak > nul
:retry
move /y ""{downloadedFilePath}"" ""{currentExe}"" > nul
if errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto retry
)
start """" ""{currentExe}""
del ""%~f0"" > nul
exit
";

        File.WriteAllText(batPath, script, System.Text.Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            CreateNoWindow = true,
            UseShellExecute = true
        };
        Process.Start(psi);
        Environment.Exit(0);
    }

    /// <summary>
    /// Opens the specified URL in the system's default web browser across all platforms.
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsAndroid())
            {
                AndroidBrowserHandler?.Invoke(url);
            }
        }
        catch { }
    }
}
