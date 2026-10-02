using System;
using System.IO;
using System.Text.Json;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

/// <summary>
/// Dịch vụ quản lý cấu hình Portable tự động cho Windows và Linux.
/// Lưu trữ toàn bộ cài đặt ứng dụng vào file 'gmtpc_config.json' ngay tại thư mục chứa file thực thi.
/// </summary>
public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigFilePath
    {
        get
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "gmtpc_config.json");
        }
    }

    public static string DefaultRecordingsDirectory
    {
        get
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Recordings");
        }
    }

    public static RecordingConfig LoadConfig()
    {
        try
        {
            string path = ConfigFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<RecordingConfig>(json, JsonOptions);
                if (config != null)
                {
                    // Đảm bảo OutputDirectory hợp lệ
                    if (string.IsNullOrWhiteSpace(config.OutputDirectory) || !Directory.Exists(config.OutputDirectory))
                    {
                        config.OutputDirectory = DefaultRecordingsDirectory;
                    }

                    EnsureDirectoryExists(config.OutputDirectory);
                    return config;
                }
            }
        }
        catch { }

        // Cấu hình mặc định nếu chưa có file hoặc lỗi nạp
        var defaultConfig = new RecordingConfig
        {
            OutputDirectory = DefaultRecordingsDirectory
        };
        EnsureDirectoryExists(defaultConfig.OutputDirectory);
        SaveConfig(defaultConfig);
        return defaultConfig;
    }

    public static bool SaveConfig(RecordingConfig config)
    {
        try
        {
            if (config == null) return false;

            // Đảm bảo đường dẫn lưu video không rỗng
            if (string.IsNullOrWhiteSpace(config.OutputDirectory))
            {
                config.OutputDirectory = DefaultRecordingsDirectory;
            }

            EnsureDirectoryExists(config.OutputDirectory);

            string json = JsonSerializer.Serialize(config, JsonOptions);
            string path = ConfigFilePath;
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureDirectoryExists(string dirPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dirPath) && !Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
        }
        catch { }
    }
}
