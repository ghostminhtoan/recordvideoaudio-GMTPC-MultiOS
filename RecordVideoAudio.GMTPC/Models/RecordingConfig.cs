using System;

namespace RecordVideoAudio.GMTPC.Models;

public class RecordingConfig
{
    public ContainerFormat Format { get; set; } = ContainerFormat.MP4;
    public VideoCodecType VideoCodec { get; set; } = VideoCodecType.H264;
    public AudioCodecType AudioCodec { get; set; } = AudioCodecType.AAC;
    public RateControlMode RateControl { get; set; } = RateControlMode.CRF;

    // Portable Output Directory
    public string OutputDirectory { get; set; } = string.Empty;

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
    public string SelectedSpeakerDeviceId { get; set; } = string.Empty; // Empty or "default" = System Default
    public bool RecordMicrophone { get; set; } = true;
    public int MicrophoneVolume { get; set; } = 90;   // 0 - 100%
    public string SelectedMicrophoneDeviceId { get; set; } = string.Empty; // Empty or "default" = System Default
    public AudioTrackMode AudioTrackMode { get; set; } = AudioTrackMode.MixToSingleTrack;

    // Audio Processing: Sync Offset (Delay in ms) & Gain (dB)
    public int SpeakerSyncOffsetMs { get; set; } = 0;   // -1000 to +1000 ms
    public double SpeakerGainDb { get; set; } = 0.0;    // -50.0 to +50.0 dB
    public bool SpeakerAutoDucking { get; set; } = false; // Tự động giảm âm lượng loa khi micro đang thu tiếng nói
    public int MicSyncOffsetMs { get; set; } = 0;       // -1000 to +1000 ms
    public double MicGainDb { get; set; } = 0.0;        // -50.0 to +50.0 dB

    // Studio & AI Noise Suppression (RNNoise / Noise Gate / High-Pass)
    public bool MicNoiseSuppression { get; set; } = true;
    public NoiseSuppressionEngine MicNoiseEngine { get; set; } = NoiseSuppressionEngine.RNNoise;
    public bool MicNoiseGate { get; set; } = false;
    public double MicNoiseGateThresholdDb { get; set; } = -36.0; // -60.0 to -20.0 dB
    public bool MicHighPassFilter { get; set; } = true;

    // Studio Vocal Polish (Compressor, EQ & De-Esser)
    public bool MicCompressor { get; set; } = true;
    public double MicCompressorThresholdDb { get; set; } = -18.0; // -36.0 to -6.0 dB
    public double MicCompressorRatio { get; set; } = 4.0;         // 1.0 to 10.0
    public VocalProfile MicVocalProfile { get; set; } = VocalProfile.BroadcastWarmth;
    public bool MicDeEsser { get; set; } = true;

    // Auto-Tune & Voice FX (Pitch Correction & Voice Changer)
    public bool MicAutoTune { get; set; } = false;
    public MusicalKey MicAutoTuneKey { get; set; } = MusicalKey.C;
    public AutoTuneScale MicAutoTuneScale { get; set; } = AutoTuneScale.Chromatic;
    public int MicAutoTuneSpeed { get; set; } = 20; // 0ms (Hard Robot) - 100ms (Natural)
    public int MicPitchShiftSemitones { get; set; } = 0; // -12 to +12 semitones

    // Karaoke & Spatial Effects (Stereo Echo & Reverb)
    public bool MicEcho { get; set; } = false;
    public int MicEchoDelayMs { get; set; } = 220; // 50ms - 600ms
    public double MicEchoFeedback { get; set; } = 35.0; // 0% - 80%
    public double MicEchoWetMix { get; set; } = 30.0; // 0% - 100%

    public bool MicReverb { get; set; } = false;
    public double MicReverbRoomSize { get; set; } = 50.0; // 10% - 95%
    public double MicReverbDamping { get; set; } = 40.0; // 0% - 100%
    public double MicReverbWetMix { get; set; } = 25.0; // 0% - 100%

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
