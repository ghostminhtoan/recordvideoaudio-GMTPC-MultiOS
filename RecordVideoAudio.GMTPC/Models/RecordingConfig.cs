using System;

namespace RecordVideoAudio.GMTPC.Models;

public class RecordingConfig
{
    public ContainerFormat Format { get; set; } = ContainerFormat.MP4;
    public VideoCodecType VideoCodec { get; set; } = VideoCodecType.H264;
    public AudioCodecType AudioCodec { get; set; } = AudioCodecType.AAC;
    public RateControlMode RateControl { get; set; } = RateControlMode.CRF;

    // Rate Control Parameters
    public int CrfValue { get; set; } = 23;        // 0 (lossless) - 51 (worst)
    public int CqpValue { get; set; } = 20;        // 0 - 51
    public int BitrateKbps { get; set; } = 6000;    // Target bitrate for CBR / VBR
    public int MaxBitrateKbps { get; set; } = 9000; // Max bitrate for VBR

    // Video settings
    public int Fps { get; set; } = 60;
    public PresetSpeed Preset { get; set; } = PresetSpeed.Veryfast;
    public HwAccelType HwAcceleration { get; set; } = HwAccelType.Auto;
    // Capture Source Specific Settings
    public CaptureSourceType CaptureSource { get; set; } = CaptureSourceType.FullScreen;
    public int AreaX { get; set; } = 0;
    public int AreaY { get; set; } = 0;
    public int AreaWidth { get; set; } = 1920;
    public int AreaHeight { get; set; } = 1080;
    public string SelectedWindowTitle { get; set; } = string.Empty;
    public string WebcamDeviceName { get; set; } = string.Empty;
    public PipPosition CameraPipPosition { get; set; } = PipPosition.BottomRight;
    public PipSize CameraPipSize { get; set; } = PipSize.Small;
    public bool DrawMouse { get; set; } = true;
    public int AutoStopMinutes { get; set; } = 0; // 0 = unlimited

    // Audio Mixer settings
    public bool RecordSystemAudio { get; set; } = true;
    public int SystemAudioVolume { get; set; } = 100; // 0 - 100%
    public bool RecordMicrophone { get; set; } = true;
    public int MicrophoneVolume { get; set; } = 90;   // 0 - 100%
    public AudioTrackMode AudioTrackMode { get; set; } = AudioTrackMode.MixToSingleTrack;

    // OBS-Style Multi-Track Audio Matrix
    // Speaker Routing to Track 1, 2, 3
    public bool SpeakerTrack1 { get; set; } = true;
    public bool SpeakerTrack2 { get; set; } = false;
    public bool SpeakerTrack3 { get; set; } = true;

    // Microphone Routing to Track 1, 2, 3
    public bool MicTrack1 { get; set; } = true;
    public bool MicTrack2 { get; set; } = true;
    public bool MicTrack3 { get; set; } = false;

    // Custom Hotkey Settings
    public bool RecordCtrl { get; set; } = true;
    public bool RecordAlt { get; set; } = true;
    public bool RecordShift { get; set; } = true;
    public bool RecordWin { get; set; } = false;
    public int RecordVkCode { get; set; } = 0x35; // D5
    public string RecordKeyName { get; set; } = "D5";

    public bool PauseCtrl { get; set; } = true;
    public bool PauseAlt { get; set; } = true;
    public bool PauseShift { get; set; } = true;
    public bool PauseWin { get; set; } = false;
    public int PauseVkCode { get; set; } = 0x38; // D8
    public string PauseKeyName { get; set; } = "D8";

    // Output Directory
    public string OutputDirectory { get; set; } = string.Empty;
    public string CustomFileName { get; set; } = string.Empty;
}

public class RecordingStats
{
    public TimeSpan ElapsedTime { get; set; } = TimeSpan.Zero;
    public long RecordedFrames { get; set; } = 0;
    public long DroppedFrames { get; set; } = 0;
    public double CurrentFps { get; set; } = 0;
    public double CurrentBitrateKbps { get; set; } = 0;
    public long EstimatedSizeBytes { get; set; } = 0;
    public string OutputFilePath { get; set; } = string.Empty;

    public string FormattedTime => ElapsedTime.ToString(@"hh\:mm\:ss");
    public string FormattedSize => $"{EstimatedSizeBytes / (1024.0 * 1024.0):F2} MB";
}

public class QualityProfile
{
    public string Id { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameVi { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string DescriptionVi { get; set; } = string.Empty;
    public string DisplayName => Localization.LocalizationService.Instance.CurrentLanguage == Localization.LanguageMode.Vietnamese ? NameVi : NameEn;
    public override string ToString() => DisplayName;
    public RecordingConfig Config { get; set; } = new();

    public static QualityProfile[] GetBuiltInProfiles() =>
    [
        new QualityProfile
        {
            Id = "gaming_high",
            NameEn = "🎮 High-FPS Gaming",
            NameVi = "🎮 Game Hiệu Năng Cao",
            DescriptionEn = "60 FPS, CQP 18 (GPU), MKV Crash-Proof, Dual Audio Tracks",
            DescriptionVi = "60 FPS, CQP 18 (Card rời), MKV chống hỏng file, 2 Track âm thanh",
            Config = new RecordingConfig
            {
                Format = ContainerFormat.MKV,
                VideoCodec = VideoCodecType.H264,
                AudioCodec = AudioCodecType.AAC,
                RateControl = RateControlMode.CQP,
                CqpValue = 18,
                Fps = 60,
                Preset = PresetSpeed.Fast,
                RecordSystemAudio = true,
                RecordMicrophone = true
            }
        },
        new QualityProfile
        {
            Id = "lecture_meeting",
            NameEn = "📚 Lecture & Meeting",
            NameVi = "📚 Bài Giảng & Họp Online",
            DescriptionEn = "30 FPS, CRF 26, MP4 Web-Ready, Compact File Size",
            DescriptionVi = "30 FPS, CRF 26, MP4 xem ngay, Dung lượng siêu nhẹ",
            Config = new RecordingConfig
            {
                Format = ContainerFormat.MP4,
                VideoCodec = VideoCodecType.H264,
                AudioCodec = AudioCodecType.AAC,
                RateControl = RateControlMode.CRF,
                CrfValue = 26,
                Fps = 30,
                Preset = PresetSpeed.Veryfast,
                RecordSystemAudio = true,
                RecordMicrophone = true
            }
        },
        new QualityProfile
        {
            Id = "hevc_ultra_quality",
            NameEn = "💎 HEVC 4K Ultra",
            NameVi = "💎 HEVC 4K Siêu Nét",
            DescriptionEn = "HEVC (H.265) 60 FPS, CRF 20, MP4 FastStart, Maximum Efficiency",
            DescriptionVi = "HEVC (H.265) 60 FPS, CRF 20, MP4 FastStart, Nén tối đa chất lượng",
            Config = new RecordingConfig
            {
                Format = ContainerFormat.MP4,
                VideoCodec = VideoCodecType.HEVC,
                AudioCodec = AudioCodecType.AAC,
                RateControl = RateControlMode.CRF,
                CrfValue = 20,
                Fps = 60,
                Preset = PresetSpeed.Medium,
                RecordSystemAudio = true,
                RecordMicrophone = false
            }
        },
        new QualityProfile
        {
            Id = "streaming_cbr",
            NameEn = "📡 Streaming Broadcast",
            NameVi = "📡 Phát Trực Tuyến CBR",
            DescriptionEn = "60 FPS, CBR 6000 kbps, Constant Bandwidth Limit",
            DescriptionVi = "60 FPS, CBR 6000 kbps, Giữ chặt trần băng thông cố định",
            Config = new RecordingConfig
            {
                Format = ContainerFormat.MP4,
                VideoCodec = VideoCodecType.H264,
                AudioCodec = AudioCodecType.AAC,
                RateControl = RateControlMode.CBR,
                BitrateKbps = 6000,
                Fps = 60,
                Preset = PresetSpeed.Veryfast,
                RecordSystemAudio = true,
                RecordMicrophone = true
            }
        }
    ];
}
