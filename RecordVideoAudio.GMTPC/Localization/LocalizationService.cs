using System;
using System.Collections.Generic;

namespace RecordVideoAudio.GMTPC.Localization;

public enum LanguageMode
{
    Vietnamese,
    English
}

public class LocalizationService
{
    public static LocalizationService Instance { get; } = new();

    public LanguageMode CurrentLanguage { get; set; } = LanguageMode.Vietnamese;

    public event Action? LanguageChanged;

    public void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == LanguageMode.Vietnamese ? LanguageMode.English : LanguageMode.Vietnamese;
        LanguageChanged?.Invoke();
    }

    public string GetText(string key)
    {
        if (_translations.TryGetValue(key, out var dict))
        {
            if (dict.TryGetValue(CurrentLanguage, out var val))
                return val;
        }
        return key;
    }

    private readonly Dictionary<string, Dictionary<LanguageMode, string>> _translations = new()
    {
        ["AppTitle"] = new()
        {
            [LanguageMode.Vietnamese] = "RECORD VIDEO AUDIO - MULTIOS - GMTPC",
            [LanguageMode.English] = "RECORD VIDEO AUDIO - MULTIOS - GMTPC"
        },
        ["Subtitle"] = new()
        {
            [LanguageMode.Vietnamese] = "Quay phim & Thu âm đa nền tảng (Windows, Linux) • Avalonia UI",
            [LanguageMode.English] = "Multi-OS Screen & Audio Recorder (Windows, Linux) • Avalonia UI"
        },
        ["ContainerFormat"] = new()
        {
            [LanguageMode.Vietnamese] = "Định dạng xuất (Container)",
            [LanguageMode.English] = "Container Format"
        },
        ["VideoCodec"] = new()
        {
            [LanguageMode.Vietnamese] = "Bộ mã hóa Video",
            [LanguageMode.English] = "Video Codec"
        },
        ["AudioCodec"] = new()
        {
            [LanguageMode.Vietnamese] = "Bộ mã hóa Âm thanh",
            [LanguageMode.English] = "Audio Codec"
        },
        ["RateControl"] = new()
        {
            [LanguageMode.Vietnamese] = "Cơ chế kiểm soát tốc độ bit (Rate Control)",
            [LanguageMode.English] = "Bitrate Control Mode (Rate Control)"
        },
        ["Fps"] = new()
        {
            [LanguageMode.Vietnamese] = "Khung hình / giây (FPS)",
            [LanguageMode.English] = "Frames Per Second (FPS)"
        },
        ["Preset"] = new()
        {
            [LanguageMode.Vietnamese] = "Tốc độ mã hóa (Preset)",
            [LanguageMode.English] = "Encoder Preset"
        },
        ["HwAcceleration"] = new()
        {
            [LanguageMode.Vietnamese] = "Tăng tốc phần cứng (GPU/NPU)",
            [LanguageMode.English] = "Hardware Acceleration (GPU/NPU)"
        },
        ["CaptureSource"] = new()
        {
            [LanguageMode.Vietnamese] = "Nguồn thu hình",
            [LanguageMode.English] = "Capture Source"
        },
        ["AudioMixer"] = new()
        {
            [LanguageMode.Vietnamese] = "Bàn trộn âm thanh kép (Dual Audio Mixer)",
            [LanguageMode.English] = "Dual Audio Channel Mixer"
        },
        ["SystemAudio"] = new()
        {
            [LanguageMode.Vietnamese] = "Âm thanh máy / Loa (System Loopback)",
            [LanguageMode.English] = "System / Speaker Audio (Loopback)"
        },
        ["Microphone"] = new()
        {
            [LanguageMode.Vietnamese] = "Microphone đàm thoại (Mic Input)",
            [LanguageMode.English] = "Microphone Voice (Mic Input)"
        },
        ["LiveCommandPreview"] = new()
        {
            [LanguageMode.Vietnamese] = "Lệnh Pipeline thời gian thực (FFmpeg / Hardware)",
            [LanguageMode.English] = "Real-time Pipeline Command (FFmpeg / Hardware)"
        },
        ["BtnStart"] = new()
        {
            [LanguageMode.Vietnamese] = "BẮT ĐẦU QUAY",
            [LanguageMode.English] = "START RECORDING"
        },
        ["BtnStop"] = new()
        {
            [LanguageMode.Vietnamese] = "DỪNG LẠI & LƯU",
            [LanguageMode.English] = "STOP & SAVE"
        },
        ["BtnPause"] = new()
        {
            [LanguageMode.Vietnamese] = "TẠM DỪNG",
            [LanguageMode.English] = "PAUSE"
        },
        ["BtnResume"] = new()
        {
            [LanguageMode.Vietnamese] = "TIẾP TỤC",
            [LanguageMode.English] = "RESUME"
        },
        ["BtnOpenFolder"] = new()
        {
            [LanguageMode.Vietnamese] = "Mở thư mục video",
            [LanguageMode.English] = "Open Output Folder"
        },
        ["BtnCopyCommand"] = new()
        {
            [LanguageMode.Vietnamese] = "Sao chép lệnh Pipeline",
            [LanguageMode.English] = "Copy Pipeline Command"
        },
        ["StateIdle"] = new()
        {
            [LanguageMode.Vietnamese] = "SẴN SÀNG",
            [LanguageMode.English] = "IDLE / READY"
        },
        ["StateRecording"] = new()
        {
            [LanguageMode.Vietnamese] = "ĐANG GHI HÌNH",
            [LanguageMode.English] = "RECORDING"
        },
        ["StatePaused"] = new()
        {
            [LanguageMode.Vietnamese] = "ĐANG TẠM DỪNG",
            [LanguageMode.English] = "PAUSED"
        },
        ["StateFinalizing"] = new()
        {
            [LanguageMode.Vietnamese] = "ĐANG ĐÓNG GÓI VIDEO...",
            [LanguageMode.English] = "FINALIZING CONTAINER..."
        },
        ["SavedTo"] = new()
        {
            [LanguageMode.Vietnamese] = "Đã lưu video thành công tại:",
            [LanguageMode.English] = "Video saved successfully to:"
        },
        ["AutoUpdateTitle"] = new()
        {
            [LanguageMode.Vietnamese] = "HỆ THỐNG CẬP NHẬT TỰ ĐỘNG (AUTO-UPDATE)",
            [LanguageMode.English] = "AUTO-UPDATE SYSTEM"
        },
        ["CheckUpdate"] = new()
        {
            [LanguageMode.Vietnamese] = "Kiểm tra cập nhật",
            [LanguageMode.English] = "Check for Updates"
        },
        ["DownloadAndInstall"] = new()
        {
            [LanguageMode.Vietnamese] = "Tải & Cập nhật ngay",
            [LanguageMode.English] = "Download & Install Update"
        },
        ["OpenBrowserDownload"] = new()
        {
            [LanguageMode.Vietnamese] = "Mở link tải trình duyệt",
            [LanguageMode.English] = "Open Download Link in Browser"
        }
    };
}
