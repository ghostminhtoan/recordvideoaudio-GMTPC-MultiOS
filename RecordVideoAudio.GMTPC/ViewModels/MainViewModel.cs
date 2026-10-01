using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordVideoAudio.GMTPC.Localization;
using RecordVideoAudio.GMTPC.Models;
using RecordVideoAudio.GMTPC.Services;
using RecordVideoAudio.GMTPC.Views;

namespace RecordVideoAudio.GMTPC.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IRecordingEngine _engine;
    private readonly IEncoderPipelineService _pipeline;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly GlobalHotKeyService _hotKeyService;
    private FloatingMiniBarWindow? _miniBarWindow;

    public MainViewModel()
    {
        _pipeline = new FFmpegPipelineService();
        _engine = new RecordingEngine(_pipeline);

        _engine.StateChanged += Engine_StateChanged;
        _engine.StatsUpdated += Engine_StatsUpdated;
        _engine.AudioLevelsUpdated += Engine_AudioLevelsUpdated;
        _engine.AutoStopped += Engine_AutoStopped;

        _loc.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(CurrentLanguageText));
            OnPropertyChanged(nameof(AppTitleText));
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(StatusBadgeText));
            UpdateTranslations();
        };

        // Determine current OS platform badge
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            PlatformBadge = "WINDOWS 10/11";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            PlatformBadge = "LINUX (PIPEWIRE/X11)";
        else
            PlatformBadge = "ANDROID";

        // Initialize lists
        FormatList = new ObservableCollection<ContainerFormat> { ContainerFormat.MP4, ContainerFormat.MKV };
        VideoCodecList = new ObservableCollection<VideoCodecType> { VideoCodecType.H264, VideoCodecType.HEVC };
        AudioCodecList = new ObservableCollection<AudioCodecType> { AudioCodecType.AAC, AudioCodecType.MP3 };
        RateControlList = new ObservableCollection<RateControlMode> { RateControlMode.CRF, RateControlMode.CQP, RateControlMode.CBR, RateControlMode.VBR };
        FpsList = new ObservableCollection<int> { 24, 30, 60, 120 };
        PresetList = new ObservableCollection<PresetSpeed> { PresetSpeed.Ultrafast, PresetSpeed.Veryfast, PresetSpeed.Fast, PresetSpeed.Medium, PresetSpeed.Slow };
        HwAccelList = new ObservableCollection<HwAccelType> { HwAccelType.Auto, HwAccelType.NVENC, HwAccelType.QSV, HwAccelType.AMF, HwAccelType.VAAPI, HwAccelType.MediaCodec, HwAccelType.SoftwareCPU };
        CaptureSourceList = new ObservableCollection<CaptureSourceType> { CaptureSourceType.FullScreen, CaptureSourceType.CustomArea, CaptureSourceType.ActiveWindow, CaptureSourceType.CameraPiP };
        PipPositionList = new ObservableCollection<PipPosition> { PipPosition.BottomRight, PipPosition.BottomLeft, PipPosition.TopRight, PipPosition.TopLeft };
        PipSizeList = new ObservableCollection<PipSize> { PipSize.Small, PipSize.Medium, PipSize.Large };
        AudioTrackModeList = new ObservableCollection<AudioTrackMode> { AudioTrackMode.MixToSingleTrack, AudioTrackMode.SeparateTracks };
        AutoStopPresetList = new ObservableCollection<int> { 0, 5, 10, 15, 30, 60, 120 };
        VocalProfileList = new ObservableCollection<VocalProfile> { VocalProfile.Natural, VocalProfile.BroadcastWarmth, VocalProfile.CrystalClear, VocalProfile.PodcastStudio };
        AutoTuneScaleList = new ObservableCollection<AutoTuneScale> { AutoTuneScale.Chromatic, AutoTuneScale.Major, AutoTuneScale.Minor };
        MusicalKeyList = new ObservableCollection<MusicalKey> { MusicalKey.C, MusicalKey.Db, MusicalKey.D, MusicalKey.Eb, MusicalKey.E, MusicalKey.F, MusicalKey.Gb, MusicalKey.G, MusicalKey.Ab, MusicalKey.A, MusicalKey.Bb, MusicalKey.B };

        Profiles = new ObservableCollection<QualityProfile>(QualityProfile.GetBuiltInProfiles());
        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }

        // Default folder (Bảo vệ an toàn cho cả Windows, Linux và Android)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            OutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");
        }
        else
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(basePath))
                basePath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (string.IsNullOrEmpty(basePath))
                basePath = AppDomain.CurrentDomain.BaseDirectory;
            OutputDirectory = Path.Combine(basePath, "Recordings");
        }

        // Sync initial audio monitoring state with hardware meter
        UpdateAudioMonitoring();

        // Global hotkey hook (Default: Ctrl+Alt+Shift+D5 for Record, Ctrl+Alt+Shift+D8 for Pause)
        _hotKeyService = new GlobalHotKeyService();
        _hotKeyService.RecordTogglePressed += () => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (CurrentState == RecordingState.Idle)
            {
                await StartRecordingAsync();
            }
            else
            {
                await StopRecordingAsync();
            }
        });

        _hotKeyService.PauseTogglePressed += () => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await TogglePauseResumeAsync();
        });

        UpdateTranslations();
        UpdateHotKeys();
        RefreshCommandPreview();
    }

    #region Properties

    [ObservableProperty]
    private string platformBadge = "DESKTOP";

    [ObservableProperty]
    private ContainerFormat selectedFormat = ContainerFormat.MP4;

    [ObservableProperty]
    private VideoCodecType selectedVideoCodec = VideoCodecType.H264;

    [ObservableProperty]
    private AudioCodecType selectedAudioCodec = AudioCodecType.AAC;

    [ObservableProperty]
    private RateControlMode selectedRateControl = RateControlMode.CRF;

    [ObservableProperty]
    private int crfValue = 23;

    [ObservableProperty]
    private int cqpValue = 20;

    [ObservableProperty]
    private int bitrateKbps = 6000;

    [ObservableProperty]
    private int maxBitrateKbps = 9000;

    [ObservableProperty]
    private int selectedFps = 60;

    [ObservableProperty]
    private PresetSpeed selectedPreset = PresetSpeed.Veryfast;

    [ObservableProperty]
    private HwAccelType selectedHwAccel = HwAccelType.Auto;

    [ObservableProperty]
    private CaptureSourceType selectedCaptureSource = CaptureSourceType.FullScreen;

    // Custom Area Properties
    [ObservableProperty]
    private int areaX = 0;

    [ObservableProperty]
    private int areaY = 0;

    [ObservableProperty]
    private int areaWidth = 1920;

    [ObservableProperty]
    private int areaHeight = 1080;

    // Active Window Properties
    [ObservableProperty]
    private ObservableCollection<string> openWindowsList = new();

    [ObservableProperty]
    private string selectedWindowTitle = string.Empty;

    // Camera PiP Properties
    [ObservableProperty]
    private ObservableCollection<string> webcamList = new();

    [ObservableProperty]
    private string selectedWebcam = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PipPosition> pipPositionList;

    [ObservableProperty]
    private PipPosition selectedPipPosition = PipPosition.BottomRight;

    [ObservableProperty]
    private ObservableCollection<PipSize> pipSizeList;

    [ObservableProperty]
    private PipSize selectedPipSize = PipSize.Small;

    // OBS-Style Multi-Track Audio Matrix
    [ObservableProperty]
    private bool speakerTrack1 = true;

    [ObservableProperty]
    private bool speakerTrack2 = false;

    [ObservableProperty]
    private bool speakerTrack3 = true;

    [ObservableProperty]
    private bool micTrack1 = true;

    [ObservableProperty]
    private bool micTrack2 = true;

    [ObservableProperty]
    private bool micTrack3 = false;

    // Customizable Global Hotkeys (Default: Ctrl+Alt+Shift+D5, Ctrl+Alt+Shift+D8)
    [ObservableProperty]
    private bool recordCtrl = true;

    [ObservableProperty]
    private bool recordAlt = true;

    [ObservableProperty]
    private bool recordShift = true;

    [ObservableProperty]
    private bool recordWin = false;

    [ObservableProperty]
    private int recordVkCode = 0x35; // D5

    [ObservableProperty]
    private string recordKeyName = "D5";

    [ObservableProperty]
    private bool pauseCtrl = true;

    [ObservableProperty]
    private bool pauseAlt = true;

    [ObservableProperty]
    private bool pauseShift = true;

    [ObservableProperty]
    private bool pauseWin = false;

    [ObservableProperty]
    private int pauseVkCode = 0x38; // D8

    [ObservableProperty]
    private string pauseKeyName = "D8";

    // Legacy AudioTrackMode & Auto-Stop
    [ObservableProperty]
    private ObservableCollection<AudioTrackMode> audioTrackModeList;

    [ObservableProperty]
    private AudioTrackMode selectedAudioTrackMode = AudioTrackMode.MixToSingleTrack;

    [ObservableProperty]
    private int autoStopMinutes = 0;

    [ObservableProperty]
    private ObservableCollection<int> autoStopPresetList;

    [ObservableProperty]
    private bool drawMouse = true;

    [ObservableProperty]
    private bool autoShowMiniBar = true;

    [ObservableProperty]
    private QualityProfile? selectedProfile;

    [ObservableProperty]
    private bool systemAudioEnabled = true;

    [ObservableProperty]
    private int systemAudioVolume = 100;

    [ObservableProperty]
    private double systemAudioLevel = 0;

    [ObservableProperty]
    private int speakerSyncOffsetMs = 0; // -500 to +1000 ms

    [ObservableProperty]
    private double speakerGainDb = 0.0; // -50.0 to +50.0 dB

    [ObservableProperty]
    private bool speakerAutoDucking = false;

    [ObservableProperty]
    private bool micAudioEnabled = true;

    [ObservableProperty]
    private int micAudioVolume = 90;

    [ObservableProperty]
    private double micAudioLevel = 0;

    [ObservableProperty]
    private int micSyncOffsetMs = 0; // -500 to +1000 ms

    [ObservableProperty]
    private double micGainDb = 0.0; // -50.0 to +50.0 dB

    [ObservableProperty]
    private bool micNoiseSuppression = true;

    [ObservableProperty]
    private bool micNoiseGate = false;

    [ObservableProperty]
    private double micNoiseGateThresholdDb = -36.0; // -60.0 to -20.0 dB

    [ObservableProperty]
    private bool micHighPassFilter = true;

    // Studio Vocal Polish (Compressor, EQ & De-Esser)
    [ObservableProperty]
    private bool micCompressor = true;

    [ObservableProperty]
    private double micCompressorThresholdDb = -18.0;

    [ObservableProperty]
    private double micCompressorRatio = 4.0;

    [ObservableProperty]
    private VocalProfile selectedVocalProfile = VocalProfile.BroadcastWarmth;

    [ObservableProperty]
    private bool micDeEsser = true;

    // Auto-Tune & Voice FX (Pitch Correction & Voice Changer)
    [ObservableProperty]
    private bool micAutoTune = false;

    [ObservableProperty]
    private MusicalKey selectedAutoTuneKey = MusicalKey.C;

    [ObservableProperty]
    private AutoTuneScale selectedAutoTuneScale = AutoTuneScale.Chromatic;

    [ObservableProperty]
    private int micAutoTuneSpeed = 20;

    [ObservableProperty]
    private int micPitchShiftSemitones = 0;

    public string SpeakerGainDisplay => $"{(SpeakerGainDb >= 0 ? "+" : "")}{SpeakerGainDb:F1} dB";
    public string MicGainDisplay => $"{(MicGainDb >= 0 ? "+" : "")}{MicGainDb:F1} dB";
    public string MicGateThresholdDisplay => $"{MicNoiseGateThresholdDb:F1} dB";
    public string MicCompressorThresholdDisplay => $"{MicCompressorThresholdDb:F1} dB";
    public string MicCompressorRatioDisplay => $"{MicCompressorRatio:F1}:1";
    public string MicAutoTuneSpeedDisplay => MicAutoTuneSpeed <= 8 ? "0ms (Hard Robot)" : $"{MicAutoTuneSpeed} ms";
    public string MicPitchShiftDisplay => $"{(MicPitchShiftSemitones > 0 ? "+" : "")}{MicPitchShiftSemitones} {(MicPitchShiftSemitones switch { < -8 => "(Quỷ / Monster)", < -2 => "(Nam trầm)", 0 => "(Giọng thật)", < 8 => "(Nữ)", _ => "(Chipmunk)" })}";

    // Karaoke & Spatial Effects (Stereo Echo & Reverb)
    [ObservableProperty]
    private bool micEcho = false;

    [ObservableProperty]
    private int micEchoDelayMs = 220; // 50ms - 600ms

    [ObservableProperty]
    private double micEchoFeedback = 35.0; // 0% - 80%

    [ObservableProperty]
    private double micEchoWetMix = 30.0; // 0% - 100%

    [ObservableProperty]
    private bool micReverb = false;

    [ObservableProperty]
    private double micReverbRoomSize = 50.0; // 10% - 95%

    [ObservableProperty]
    private double micReverbDamping = 40.0; // 0% - 100%

    [ObservableProperty]
    private double micReverbWetMix = 25.0; // 0% - 100%

    public string MicEchoDelayDisplay => $"{MicEchoDelayMs} ms";
    public string MicEchoFeedbackDisplay => $"{MicEchoFeedback:F0}%";
    public string MicEchoWetMixDisplay => $"{MicEchoWetMix:F0}%";
    public string MicReverbRoomSizeDisplay => $"{MicReverbRoomSize:F0}%";
    public string MicReverbDampingDisplay => $"{MicReverbDamping:F0}%";
    public string MicReverbWetMixDisplay => $"{MicReverbWetMix:F0}%";

    // Auto Latency Detector State
    private readonly AudioLatencyDetectorService _latencyDetector = new();

    [ObservableProperty]
    private bool isDetectingLatency = false;

    [ObservableProperty]
    private string latencyStatusMessage = "Sẵn sàng phân tích độ lệch bài hát & micro.";

    [ObservableProperty]
    private int detectedLatencyMs = 0;

    [ObservableProperty]
    private double latencyConfidence = 0.0;

    [ObservableProperty]
    private bool hasDetectedLatency = false;

    [ObservableProperty]
    private bool isHoldingLiveMeasure = false;

    [ObservableProperty]
    private string liveHoldButtonText = "🎙️ NHẤN GIỮ ĐỂ HÁT (BUÔNG TAY ĐỂ TÍNH ĐỘ TRỄ)";

    [ObservableProperty]
    private double liveHoldElapsedSeconds = 0.0;

    private Avalonia.Threading.DispatcherTimer? _liveHoldTimer;

    public bool HasActiveVocalFx => MicEcho || MicReverb || MicAutoTune || (MicPitchShiftSemitones != 0) || MicCompressor || MicNoiseSuppression || MicNoiseGate || MicDeEsser;

    public string VocalFxBadgeText
    {
        get
        {
            int count = 0;
            if (MicEcho) count++;
            if (MicReverb) count++;
            if (MicAutoTune) count++;
            if (MicPitchShiftSemitones != 0) count++;
            if (MicCompressor) count++;
            if (MicNoiseSuppression) count++;
            if (MicNoiseGate) count++;
            if (MicDeEsser) count++;
            return count > 0 ? $"ĐANG BẬT {count} HIỆU ỨNG" : "CHƯA BẬT HIỆU ỨNG";
        }
    }

    public string VocalFxStatusSummary
    {
        get
        {
            var active = new System.Collections.Generic.List<string>();
            if (MicEcho) active.Add("Echo");
            if (MicReverb) active.Add("Reverb");
            if (MicAutoTune) active.Add($"AutoTune[{SelectedAutoTuneKey}]");
            if (MicPitchShiftSemitones != 0) active.Add($"Pitch[{(MicPitchShiftSemitones > 0 ? "+" : "")}{MicPitchShiftSemitones}]");
            if (MicCompressor) active.Add("Compressor");
            if (MicNoiseSuppression) active.Add("Denoise");
            return active.Count > 0 ? string.Join(" • ", active) : "Giọng mộc (Dry natural)";
        }
    }

    public ObservableCollection<VocalProfile> VocalProfileList { get; }
    public ObservableCollection<AutoTuneScale> AutoTuneScaleList { get; }
    public ObservableCollection<MusicalKey> MusicalKeyList { get; }

    [ObservableProperty]
    private RecordingState currentState = RecordingState.Idle;

    [ObservableProperty]
    private string elapsedTimeString = "00:00:00";

    [ObservableProperty]
    private string fileSizeString = "0.00 MB";

    [ObservableProperty]
    private string currentFpsString = "0.0 FPS";

    [ObservableProperty]
    private string recordedFramesString = "0 frames";

    [ObservableProperty]
    private string droppedFramesString = "0 drops";

    [ObservableProperty]
    private string outputDirectory = string.Empty;

    [ObservableProperty]
    private string commandLinePreview = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Hệ thống sẵn sàng ghi hình. (Phím tắt: F8 = Quay/Dừng, F9 = Tạm dừng)";

    public bool IsRecording => CurrentState == RecordingState.Recording;
    public bool IsPaused => CurrentState == RecordingState.Paused;
    public bool IsIdle => CurrentState == RecordingState.Idle;
    public bool IsNotRecording => CurrentState == RecordingState.Idle;

    public bool IsCrfMode => SelectedRateControl == RateControlMode.CRF;
    public bool IsCqpMode => SelectedRateControl == RateControlMode.CQP;
    public bool IsCbrMode => SelectedRateControl == RateControlMode.CBR;
    public bool IsVbrMode => SelectedRateControl == RateControlMode.VBR;

    public bool IsCustomAreaMode => SelectedCaptureSource == CaptureSourceType.CustomArea;
    public bool IsActiveWindowMode => SelectedCaptureSource == CaptureSourceType.ActiveWindow;
    public bool IsCameraPipMode => SelectedCaptureSource == CaptureSourceType.CameraPiP;

    public ObservableCollection<ContainerFormat> FormatList { get; }
    public ObservableCollection<VideoCodecType> VideoCodecList { get; }
    public ObservableCollection<AudioCodecType> AudioCodecList { get; }
    public ObservableCollection<RateControlMode> RateControlList { get; }
    public ObservableCollection<int> FpsList { get; }
    public ObservableCollection<PresetSpeed> PresetList { get; }
    public ObservableCollection<HwAccelType> HwAccelList { get; }
    public ObservableCollection<CaptureSourceType> CaptureSourceList { get; }
    public ObservableCollection<QualityProfile> Profiles { get; }

    #endregion

    #region Localization Properties

    public string CurrentLanguageText => _loc.CurrentLanguage == LanguageMode.Vietnamese ? "🇻🇳 TIẾNG VIỆT" : "🇬🇧 ENGLISH";
    public string AppTitleText => _loc.GetText("AppTitle");
    public string SubtitleText => _loc.GetText("Subtitle");
    public string StatusBadgeText => CurrentState switch
    {
        RecordingState.Idle => _loc.GetText("StateIdle"),
        RecordingState.Recording => _loc.GetText("StateRecording"),
        RecordingState.Paused => _loc.GetText("StatePaused"),
        RecordingState.Finalizing => _loc.GetText("StateFinalizing"),
        _ => "IDLE"
    };

    public string StatusBadgeColor => CurrentState switch
    {
        RecordingState.Recording => "#FF1744",
        RecordingState.Paused => "#FFB800",
        RecordingState.Finalizing => "#00F0FF",
        _ => "#00E676"
    };

    public string StatusBadgeBackground => CurrentState switch
    {
        RecordingState.Recording => "#33FF1744",
        RecordingState.Paused => "#33FFB800",
        RecordingState.Finalizing => "#3300F0FF",
        _ => "#1E293B"
    };

    [ObservableProperty] private string labelContainerFormat = "";
    [ObservableProperty] private string labelVideoCodec = "";
    [ObservableProperty] private string labelAudioCodec = "";
    [ObservableProperty] private string labelRateControl = "";
    [ObservableProperty] private string labelFps = "";
    [ObservableProperty] private string labelPreset = "";
    [ObservableProperty] private string labelHwAcceleration = "";
    [ObservableProperty] private string labelCaptureSource = "";
    [ObservableProperty] private string labelAudioMixer = "";
    [ObservableProperty] private string labelSystemAudio = "";
    [ObservableProperty] private string labelMicrophone = "";
    [ObservableProperty] private string labelLiveCommand = "";
    [ObservableProperty] private string btnStartText = "";
    [ObservableProperty] private string btnStopText = "";
    [ObservableProperty] private string btnPauseText = "";
    [ObservableProperty] private string btnResumeText = "";
    [ObservableProperty] private string btnOpenFolderText = "";
    [ObservableProperty] private string btnCopyCommandText = "";

    private void UpdateTranslations()
    {
        LabelContainerFormat = _loc.GetText("ContainerFormat");
        LabelVideoCodec = _loc.GetText("VideoCodec");
        LabelAudioCodec = _loc.GetText("AudioCodec");
        LabelRateControl = _loc.GetText("RateControl");
        LabelFps = _loc.GetText("Fps");
        LabelPreset = _loc.GetText("Preset");
        LabelHwAcceleration = _loc.GetText("HwAcceleration");
        LabelCaptureSource = _loc.GetText("CaptureSource");
        LabelAudioMixer = _loc.GetText("AudioMixer");
        LabelSystemAudio = _loc.GetText("SystemAudio");
        LabelMicrophone = _loc.GetText("Microphone");
        LabelLiveCommand = _loc.GetText("LiveCommandPreview");
        BtnStartText = _loc.GetText("BtnStart");
        BtnStopText = _loc.GetText("BtnStop");
        BtnPauseText = _loc.GetText("BtnPause");
        BtnResumeText = _loc.GetText("BtnResume");
        BtnOpenFolderText = _loc.GetText("BtnOpenFolder");
        BtnCopyCommandText = _loc.GetText("BtnCopyCommand");
    }

    #endregion

    #region Change Triggers

    partial void OnSelectedFormatChanged(ContainerFormat value) => RefreshCommandPreview();
    partial void OnSelectedVideoCodecChanged(VideoCodecType value) => RefreshCommandPreview();
    partial void OnSelectedAudioCodecChanged(AudioCodecType value) => RefreshCommandPreview();
    partial void OnSelectedRateControlChanged(RateControlMode value)
    {
        OnPropertyChanged(nameof(IsCrfMode));
        OnPropertyChanged(nameof(IsCqpMode));
        OnPropertyChanged(nameof(IsCbrMode));
        OnPropertyChanged(nameof(IsVbrMode));
        RefreshCommandPreview();
    }
    partial void OnCrfValueChanged(int value) => RefreshCommandPreview();
    partial void OnCqpValueChanged(int value) => RefreshCommandPreview();
    partial void OnBitrateKbpsChanged(int value) => RefreshCommandPreview();
    partial void OnMaxBitrateKbpsChanged(int value) => RefreshCommandPreview();
    partial void OnSelectedFpsChanged(int value) => RefreshCommandPreview();
    partial void OnSelectedPresetChanged(PresetSpeed value) => RefreshCommandPreview();
    partial void OnSelectedHwAccelChanged(HwAccelType value) => RefreshCommandPreview();
    partial void OnSelectedAudioTrackModeChanged(AudioTrackMode value) => RefreshCommandPreview();
    partial void OnDrawMouseChanged(bool value) => RefreshCommandPreview();

    partial void OnSelectedCaptureSourceChanged(CaptureSourceType value)
    {
        OnPropertyChanged(nameof(IsCustomAreaMode));
        OnPropertyChanged(nameof(IsActiveWindowMode));
        OnPropertyChanged(nameof(IsCameraPipMode));

        if (value == CaptureSourceType.ActiveWindow && OpenWindowsList.Count == 0)
        {
            RefreshWindows();
        }
        else if (value == CaptureSourceType.CameraPiP && WebcamList.Count == 0)
        {
            RefreshWebcams();
        }

        RefreshCommandPreview();
    }

    partial void OnAreaXChanged(int value) => RefreshCommandPreview();
    partial void OnAreaYChanged(int value) => RefreshCommandPreview();
    partial void OnAreaWidthChanged(int value) => RefreshCommandPreview();
    partial void OnAreaHeightChanged(int value) => RefreshCommandPreview();
    partial void OnSelectedWindowTitleChanged(string value) => RefreshCommandPreview();
    partial void OnSelectedWebcamChanged(string value) => RefreshCommandPreview();
    partial void OnSelectedPipPositionChanged(PipPosition value) => RefreshCommandPreview();
    partial void OnSelectedPipSizeChanged(PipSize value) => RefreshCommandPreview();

    partial void OnSystemAudioEnabledChanged(bool value)
    {
        UpdateAudioMonitoring();
        if (!value) SystemAudioLevel = 0;
        RefreshCommandPreview();
    }

    partial void OnSystemAudioVolumeChanged(int value)
    {
        UpdateAudioMonitoring();
    }

    partial void OnSpeakerSyncOffsetMsChanged(int value) => RefreshCommandPreview();

    partial void OnSpeakerGainDbChanged(double value)
    {
        OnPropertyChanged(nameof(SpeakerGainDisplay));
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnSpeakerAutoDuckingChanged(bool value)
    {
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicAudioEnabledChanged(bool value)
    {
        UpdateAudioMonitoring();
        if (!value) MicAudioLevel = 0;
        RefreshCommandPreview();
    }

    partial void OnMicAudioVolumeChanged(int value)
    {
        UpdateAudioMonitoring();
    }

    partial void OnMicSyncOffsetMsChanged(int value) => RefreshCommandPreview();

    partial void OnMicGainDbChanged(double value)
    {
        OnPropertyChanged(nameof(MicGainDisplay));
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicNoiseSuppressionChanged(bool value)
    {
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicNoiseGateChanged(bool value)
    {
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicNoiseGateThresholdDbChanged(double value)
    {
        OnPropertyChanged(nameof(MicGateThresholdDisplay));
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicHighPassFilterChanged(bool value)
    {
        UpdateAudioMonitoring();
        RefreshCommandPreview();
    }

    partial void OnMicCompressorChanged(bool value) => UpdateAudioMonitoring();
    partial void OnMicCompressorThresholdDbChanged(double value)
    {
        OnPropertyChanged(nameof(MicCompressorThresholdDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicCompressorRatioChanged(double value)
    {
        OnPropertyChanged(nameof(MicCompressorRatioDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnSelectedVocalProfileChanged(VocalProfile value) => UpdateAudioMonitoring();
    partial void OnMicDeEsserChanged(bool value)
    {
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }

    partial void OnMicAutoTuneChanged(bool value)
    {
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }
    partial void OnSelectedAutoTuneKeyChanged(MusicalKey value)
    {
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }
    partial void OnSelectedAutoTuneScaleChanged(AutoTuneScale value) => UpdateAudioMonitoring();
    partial void OnMicAutoTuneSpeedChanged(int value)
    {
        OnPropertyChanged(nameof(MicAutoTuneSpeedDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicPitchShiftSemitonesChanged(int value)
    {
        OnPropertyChanged(nameof(MicPitchShiftDisplay));
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }

    partial void OnMicEchoChanged(bool value)
    {
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }
    partial void OnMicEchoDelayMsChanged(int value)
    {
        OnPropertyChanged(nameof(MicEchoDelayDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicEchoFeedbackChanged(double value)
    {
        OnPropertyChanged(nameof(MicEchoFeedbackDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicEchoWetMixChanged(double value)
    {
        OnPropertyChanged(nameof(MicEchoWetMixDisplay));
        UpdateAudioMonitoring();
    }

    partial void OnMicReverbChanged(bool value)
    {
        NotifyVocalFxChanged();
        UpdateAudioMonitoring();
    }
    partial void OnMicReverbRoomSizeChanged(double value)
    {
        OnPropertyChanged(nameof(MicReverbRoomSizeDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicReverbDampingChanged(double value)
    {
        OnPropertyChanged(nameof(MicReverbDampingDisplay));
        UpdateAudioMonitoring();
    }
    partial void OnMicReverbWetMixChanged(double value)
    {
        OnPropertyChanged(nameof(MicReverbWetMixDisplay));
        UpdateAudioMonitoring();
    }

    private void NotifyVocalFxChanged()
    {
        OnPropertyChanged(nameof(HasActiveVocalFx));
        OnPropertyChanged(nameof(VocalFxBadgeText));
        OnPropertyChanged(nameof(VocalFxStatusSummary));
    }

    [RelayCommand]
    private void ResetPitchShift()
    {
        MicPitchShiftSemitones = 0;
    }

    [ObservableProperty]
    private bool isVocalStudioModalOpen = false;

    private VocalStudioWindow? _vocalStudioWindow;

    [RelayCommand]
    public void OpenVocalStudio()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            try
            {
                if (_vocalStudioWindow != null)
                {
                    if (_vocalStudioWindow.WindowState == WindowState.Minimized)
                    {
                        _vocalStudioWindow.WindowState = WindowState.Normal;
                    }
                    _vocalStudioWindow.Activate();
                    return;
                }

                _vocalStudioWindow = new VocalStudioWindow
                {
                    DataContext = this
                };

                _vocalStudioWindow.Closed += (s, e) => _vocalStudioWindow = null;
                // Mở hoàn toàn độc lập không gán Owner, để khi minimize FX window thì MainWindow không bị ẩn/tắt
                _vocalStudioWindow.Show();
                return;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Không thể mở cửa sổ Vocal Studio độc lập: {ex.Message}";
            }
        }

        // Trên Android / Mobile hoặc Desktop Fallback: Mở Modal trực tiếp trên màn hình chính
        IsVocalStudioModalOpen = true;
    }

    [RelayCommand]
    public void CloseVocalStudio()
    {
        IsVocalStudioModalOpen = false;
        try
        {
            _vocalStudioWindow?.Close();
        }
        catch { }
    }

    [RelayCommand]
    private async Task CalibrateLatencyWithPulseAsync()
    {
        if (IsDetectingLatency) return;
        IsDetectingLatency = true;
        LatencyStatusMessage = "Đang phát xung bíp kiểm âm 10ms...";
        HasDetectedLatency = false;

        try
        {
            var res = await _latencyDetector.CalibrateWithPulseAsync();
            if (res.HasValue)
            {
                DetectedLatencyMs = res.Value;
                LatencyConfidence = _latencyDetector.ConfidencePercent;
                HasDetectedLatency = true;
                LatencyStatusMessage = _latencyDetector.StatusMessage;
            }
            else
            {
                LatencyStatusMessage = _latencyDetector.StatusMessage;
            }
        }
        catch (Exception ex)
        {
            LatencyStatusMessage = $"Lỗi đo xung: {ex.Message}";
        }
        finally
        {
            IsDetectingLatency = false;
        }
    }

    public void StartLiveAudioHoldCapture()
    {
        if (IsDetectingLatency) return;
        HasDetectedLatency = false;
        IsDetectingLatency = true;
        IsHoldingLiveMeasure = true;
        LiveHoldElapsedSeconds = 0.0;
        LiveHoldButtonText = "🔴 ĐANG THU ÂM (0.0s)... GIỮ CHUỘT!";

        bool started = _latencyDetector.StartLiveHoldCapture();
        if (!started)
        {
            IsDetectingLatency = false;
            IsHoldingLiveMeasure = false;
            LiveHoldButtonText = "🎙️ NHẤN GIỮ ĐỂ HÁT (BUÔNG TAY ĐỂ TÍNH ĐỘ TRỄ)";
            LatencyStatusMessage = _latencyDetector.StatusMessage;
            return;
        }

        LatencyStatusMessage = "🔴 ĐANG THU ÂM TIẾNG HÁT & NHẠC... Hãy giữ chuột và hát theo bài hát trên loa!";

        _liveHoldTimer?.Stop();
        _liveHoldTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _liveHoldTimer.Tick += (s, e) =>
        {
            LiveHoldElapsedSeconds += 0.1;
            LiveHoldButtonText = $"🔴 ĐANG THU ÂM ({LiveHoldElapsedSeconds:F1}s)... HÃY GIỮ CHUỘT!";
        };
        _liveHoldTimer.Start();
    }

    public void StopLiveAudioHoldCapture()
    {
        if (!IsHoldingLiveMeasure) return;

        _liveHoldTimer?.Stop();
        _liveHoldTimer = null;
        IsHoldingLiveMeasure = false;
        LiveHoldButtonText = "🎙️ NHẤN GIỮ ĐỂ HÁT (BUÔNG TAY ĐỂ TÍNH ĐỘ TRỄ)";

        try
        {
            var res = _latencyDetector.StopLiveHoldCaptureAndAnalyze();
            if (res.HasValue)
            {
                DetectedLatencyMs = res.Value;
                LatencyConfidence = _latencyDetector.ConfidencePercent;
                HasDetectedLatency = true;
                LatencyStatusMessage = $"Đã phát hiện độ lệch bài hát & micro: {res.Value} ms (Độ tin cậy: {LatencyConfidence:0.0}%)";
            }
            else
            {
                LatencyStatusMessage = _latencyDetector.StatusMessage;
            }
        }
        catch (Exception ex)
        {
            LatencyStatusMessage = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsDetectingLatency = false;
        }
    }

    [RelayCommand]
    private void DetectLatencyFromLiveAudio()
    {
        if (IsDetectingLatency) return;
        LatencyStatusMessage = "Vui lòng NHẤN VÀ GIỮ CHUỘT trên nút đo bên dưới trong lúc hát, buông chuột khi hát xong để tính toán!";
    }

    [RelayCommand]
    private void ApplyDetectedLatencyToSyncOffset()
    {
        if (!HasDetectedLatency) return;
        MicSyncOffsetMs = -DetectedLatencyMs;
        LatencyStatusMessage = $"Đã áp dụng bù trừ lệch tiếng: Mic Sync Offset = {MicSyncOffsetMs} ms";
    }

    private void UpdateAudioMonitoring()
    {
        _engine.UpdateAudioMonitoringSettings(
            SystemAudioEnabled, SystemAudioVolume,
            MicAudioEnabled, MicAudioVolume,
            SpeakerGainDb, MicGainDb,
            MicNoiseSuppression,
            MicNoiseGate, MicNoiseGateThresholdDb,
            MicHighPassFilter,
            MicCompressor, MicCompressorThresholdDb, MicCompressorRatio,
            SelectedVocalProfile, MicDeEsser,
            MicAutoTune, SelectedAutoTuneKey, SelectedAutoTuneScale,
            MicAutoTuneSpeed, MicPitchShiftSemitones,
            MicEcho, MicEchoDelayMs, MicEchoFeedback, MicEchoWetMix,
            MicReverb, MicReverbRoomSize, MicReverbDamping, MicReverbWetMix,
            SpeakerAutoDucking
        );
    }

    partial void OnSpeakerTrack1Changed(bool value) => RefreshCommandPreview();
    partial void OnSpeakerTrack2Changed(bool value) => RefreshCommandPreview();
    partial void OnSpeakerTrack3Changed(bool value) => RefreshCommandPreview();
    partial void OnMicTrack1Changed(bool value) => RefreshCommandPreview();
    partial void OnMicTrack2Changed(bool value) => RefreshCommandPreview();
    partial void OnMicTrack3Changed(bool value) => RefreshCommandPreview();

    partial void OnRecordCtrlChanged(bool value) => UpdateHotKeys();
    partial void OnRecordAltChanged(bool value) => UpdateHotKeys();
    partial void OnRecordShiftChanged(bool value) => UpdateHotKeys();
    partial void OnRecordWinChanged(bool value) => UpdateHotKeys();
    partial void OnRecordVkCodeChanged(int value) => UpdateHotKeys();
    partial void OnRecordKeyNameChanged(string value) => UpdateHotKeys();

    partial void OnPauseCtrlChanged(bool value) => UpdateHotKeys();
    partial void OnPauseAltChanged(bool value) => UpdateHotKeys();
    partial void OnPauseShiftChanged(bool value) => UpdateHotKeys();
    partial void OnPauseWinChanged(bool value) => UpdateHotKeys();
    partial void OnPauseVkCodeChanged(int value) => UpdateHotKeys();
    partial void OnPauseKeyNameChanged(string value) => UpdateHotKeys();

    public void SetRecordKey(Avalonia.Input.Key key)
    {
        var (vk, name) = KeyBindingHelper.FromAvaloniaKey(key);
        if (vk != 0 && !string.IsNullOrEmpty(name))
        {
            RecordVkCode = vk;
            RecordKeyName = name;
        }
    }

    public void SetRecordKeyFromInput(string input)
    {
        var (vk, name) = KeyBindingHelper.ParseKey(input);
        if (vk != 0 && !string.IsNullOrEmpty(name))
        {
            RecordVkCode = vk;
            RecordKeyName = name;
        }
    }

    public void SetPauseKey(Avalonia.Input.Key key)
    {
        var (vk, name) = KeyBindingHelper.FromAvaloniaKey(key);
        if (vk != 0 && !string.IsNullOrEmpty(name))
        {
            PauseVkCode = vk;
            PauseKeyName = name;
        }
    }

    public void SetPauseKeyFromInput(string input)
    {
        var (vk, name) = KeyBindingHelper.ParseKey(input);
        if (vk != 0 && !string.IsNullOrEmpty(name))
        {
            PauseVkCode = vk;
            PauseKeyName = name;
        }
    }

    public string GetRecordShortcutDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (RecordCtrl) parts.Add("Ctrl");
        if (RecordAlt) parts.Add("Alt");
        if (RecordShift) parts.Add("Shift");
        if (RecordWin) parts.Add("Win");
        parts.Add(RecordKeyName);
        return string.Join(" + ", parts);
    }

    public string GetPauseShortcutDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (PauseCtrl) parts.Add("Ctrl");
        if (PauseAlt) parts.Add("Alt");
        if (PauseShift) parts.Add("Shift");
        if (PauseWin) parts.Add("Win");
        parts.Add(PauseKeyName);
        return string.Join(" + ", parts);
    }

    private void UpdateHotKeys()
    {
        var config = BuildCurrentConfig();
        _hotKeyService.UpdateHotKeys(config);
        if (CurrentState == RecordingState.Idle)
        {
            StatusMessage = $"Hệ thống sẵn sàng ghi hình. (Phím tắt: {GetRecordShortcutDisplay()} = Quay/Dừng, {GetPauseShortcutDisplay()} = Tạm dừng)";
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    partial void OnSelectedProfileChanged(QualityProfile? value)
    {
        if (value == null) return;
        SelectedFormat = value.Config.Format;
        SelectedVideoCodec = value.Config.VideoCodec;
        SelectedAudioCodec = value.Config.AudioCodec;
        SelectedRateControl = value.Config.RateControl;
        CrfValue = value.Config.CrfValue;
        CqpValue = value.Config.CqpValue;
        BitrateKbps = value.Config.BitrateKbps;
        SelectedFps = value.Config.Fps;
        SelectedPreset = value.Config.Preset;
        SystemAudioEnabled = value.Config.RecordSystemAudio;
        MicAudioEnabled = value.Config.RecordMicrophone;
        RefreshCommandPreview();
    }

    private void RefreshCommandPreview()
    {
        var config = BuildCurrentConfig();
        string ext = _pipeline.GetOutputExtension(config.Format);
        string samplePath = Path.Combine(OutputDirectory, $"GMTPC_Record_Output{ext}");
        CommandLinePreview = _pipeline.BuildCommandLine(config, samplePath);
    }

    private RecordingConfig BuildCurrentConfig()
    {
        return new RecordingConfig
        {
            Format = SelectedFormat,
            VideoCodec = SelectedVideoCodec,
            AudioCodec = SelectedAudioCodec,
            RateControl = SelectedRateControl,
            CrfValue = CrfValue,
            CqpValue = CqpValue,
            BitrateKbps = BitrateKbps,
            MaxBitrateKbps = MaxBitrateKbps,
            Fps = SelectedFps,
            Preset = SelectedPreset,
            HwAcceleration = SelectedHwAccel,
            CaptureSource = SelectedCaptureSource,
            AreaX = AreaX,
            AreaY = AreaY,
            AreaWidth = AreaWidth,
            AreaHeight = AreaHeight,
            SelectedWindowTitle = SelectedWindowTitle,
            WebcamDeviceName = SelectedWebcam,
            CameraPipPosition = SelectedPipPosition,
            CameraPipSize = SelectedPipSize,
            AudioTrackMode = SelectedAudioTrackMode,
            AutoStopMinutes = AutoStopMinutes,
            DrawMouse = DrawMouse,
            RecordSystemAudio = SystemAudioEnabled,
            SystemAudioVolume = SystemAudioVolume,
            RecordMicrophone = MicAudioEnabled,
            MicrophoneVolume = MicAudioVolume,
            SpeakerTrack1 = SpeakerTrack1,
            SpeakerTrack2 = SpeakerTrack2,
            SpeakerTrack3 = SpeakerTrack3,
            MicTrack1 = MicTrack1,
            MicTrack2 = MicTrack2,
            MicTrack3 = MicTrack3,
            SpeakerSyncOffsetMs = SpeakerSyncOffsetMs,
            SpeakerGainDb = SpeakerGainDb,
            SpeakerAutoDucking = SpeakerAutoDucking,
            MicSyncOffsetMs = MicSyncOffsetMs,
            MicGainDb = MicGainDb,
            MicNoiseSuppression = MicNoiseSuppression,
            MicNoiseGate = MicNoiseGate,
            MicNoiseGateThresholdDb = MicNoiseGateThresholdDb,
            MicHighPassFilter = MicHighPassFilter,
            MicCompressor = MicCompressor,
            MicCompressorThresholdDb = MicCompressorThresholdDb,
            MicCompressorRatio = MicCompressorRatio,
            MicVocalProfile = SelectedVocalProfile,
            MicDeEsser = MicDeEsser,
            MicAutoTune = MicAutoTune,
            MicAutoTuneKey = SelectedAutoTuneKey,
            MicAutoTuneScale = SelectedAutoTuneScale,
            MicAutoTuneSpeed = MicAutoTuneSpeed,
            MicPitchShiftSemitones = MicPitchShiftSemitones,
            MicEcho = MicEcho,
            MicEchoDelayMs = MicEchoDelayMs,
            MicEchoFeedback = MicEchoFeedback,
            MicEchoWetMix = MicEchoWetMix,
            MicReverb = MicReverb,
            MicReverbRoomSize = MicReverbRoomSize,
            MicReverbDamping = MicReverbDamping,
            MicReverbWetMix = MicReverbWetMix,
            RecordCtrl = RecordCtrl,
            RecordAlt = RecordAlt,
            RecordShift = RecordShift,
            RecordWin = RecordWin,
            RecordVkCode = RecordVkCode,
            RecordKeyName = RecordKeyName,
            PauseCtrl = PauseCtrl,
            PauseAlt = PauseAlt,
            PauseShift = PauseShift,
            PauseWin = PauseWin,
            PauseVkCode = PauseVkCode,
            PauseKeyName = PauseKeyName,
            OutputDirectory = OutputDirectory
        };
    }

    #endregion

    #region Engine Callbacks

    private void Engine_StateChanged(RecordingState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CurrentState = state;
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsNotRecording));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(StatusBadgeColor));
            OnPropertyChanged(nameof(StatusBadgeBackground));

            if (state == RecordingState.Recording)
            {
                StatusMessage = string.IsNullOrEmpty(_engine.LastErrorMessage)
                    ? $"🔴 Đang quay phim & thu âm trực tiếp... ({GetRecordShortcutDisplay()} = Dừng, {GetPauseShortcutDisplay()} = Tạm dừng)"
                    : $"⚠️ Đang quay phim (Lưu ý: {_engine.LastErrorMessage})";

                // Show floating mini-bar if enabled on desktop
                if (AutoShowMiniBar && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    if (desktop.MainWindow != null)
                    {
                        desktop.MainWindow.WindowState = WindowState.Minimized;
                    }

                    if (_miniBarWindow == null)
                    {
                        _miniBarWindow = new FloatingMiniBarWindow { DataContext = this };
                    }
                    _miniBarWindow.Show();
                    _miniBarWindow.PositionAtTopRight();
                }
            }
            else if (state == RecordingState.Paused)
            {
                StatusMessage = $"⏸️ Quá trình quay đang tạm dừng. ({GetPauseShortcutDisplay()} = Tiếp tục, {GetRecordShortcutDisplay()} = Dừng & Lưu)";
            }
            else if (state == RecordingState.Idle)
            {
                StatusMessage = "✅ Đã lưu video thành công.";

                // Hide mini-bar and restore main window
                _miniBarWindow?.Hide();
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.Activate();
                }
            }
        });
    }

    private void Engine_StatsUpdated(RecordingStats stats)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ElapsedTimeString = stats.FormattedTime;
            FileSizeString = stats.FormattedSize;
            CurrentFpsString = $"{stats.CurrentFps:F1} FPS";
            RecordedFramesString = $"{stats.RecordedFrames:N0} frames";
            DroppedFramesString = $"{stats.DroppedFrames} drops";
        });
    }

    private void Engine_AudioLevelsUpdated(double sysLevel, double micLevel)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SystemAudioLevel = sysLevel;
            MicAudioLevel = micLevel;
        });
    }

    private void Engine_AutoStopped()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = $"⏰ Đã tự động dừng quay theo lịch hẹn ({AutoStopMinutes} phút).";
        });
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void ToggleLanguage()
    {
        _loc.ToggleLanguage();
    }

    [RelayCommand]
    private void OpenRegionSelector()
    {
        var selector = new RegionSelectorWindow();
        selector.RegionSelected = (x, y, w, h) =>
        {
            AreaX = x;
            AreaY = y;
            AreaWidth = (w / 2) * 2;
            AreaHeight = (h / 2) * 2;
            SelectedCaptureSource = CaptureSourceType.CustomArea;
            RefreshCommandPreview();
        };
        selector.Show();
    }

    [RelayCommand]
    private void RestoreMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            desktop.MainWindow.WindowState = WindowState.Normal;
            desktop.MainWindow.Activate();
        }
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        OpenWindowsList.Clear();
        var windows = WindowEnumerator.GetOpenWindows();
        foreach (var w in windows)
        {
            OpenWindowsList.Add(w);
        }
        if (OpenWindowsList.Count > 0 && (string.IsNullOrEmpty(SelectedWindowTitle) || !OpenWindowsList.Contains(SelectedWindowTitle)))
        {
            SelectedWindowTitle = OpenWindowsList[0];
        }
    }

    [RelayCommand]
    private void RefreshWebcams()
    {
        WebcamList.Clear();
        string? detected = _pipeline.GetDetectedWebcamDevice();
        if (!string.IsNullOrEmpty(detected))
        {
            WebcamList.Add(detected);
            SelectedWebcam = detected;
        }
        else
        {
            WebcamList.Add("Logi C270 HD WebCam");
            SelectedWebcam = "Logi C270 HD WebCam";
        }
    }

    [RelayCommand]
    private void SetPresetFhd()
    {
        AreaX = 0;
        AreaY = 0;
        AreaWidth = 1920;
        AreaHeight = 1080;
    }

    [RelayCommand]
    private void SetPresetHd()
    {
        AreaX = 0;
        AreaY = 0;
        AreaWidth = 1280;
        AreaHeight = 720;
    }

    [RelayCommand]
    private void SetPresetSd()
    {
        AreaX = 0;
        AreaY = 0;
        AreaWidth = 854;
        AreaHeight = 480;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (CurrentState != RecordingState.Idle) return;
        var config = BuildCurrentConfig();
        await _engine.StartRecordingAsync(config);
    }

    [RelayCommand]
    private async Task TogglePauseResumeAsync()
    {
        if (CurrentState == RecordingState.Recording)
        {
            await _engine.PauseRecordingAsync();
        }
        else if (CurrentState == RecordingState.Paused)
        {
            await _engine.ResumeRecordingAsync();
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (CurrentState == RecordingState.Idle) return;
        string savedFile = await _engine.StopRecordingAsync();
        if (File.Exists(savedFile))
        {
            var fi = new FileInfo(savedFile);
            string extra = !string.IsNullOrEmpty(_engine.LastErrorMessage) ? $" [{_engine.LastErrorMessage}]" : "";
            StatusMessage = $"{_loc.GetText("SavedTo")} {savedFile} ({fi.Length / (1024.0 * 1024.0):F2} MB){extra}";
        }
        else
        {
            string err = !string.IsNullOrEmpty(_engine.LastErrorMessage) ? $" (Chi tiết: {_engine.LastErrorMessage})" : "";
            StatusMessage = $"⚠️ Không tìm thấy file: {savedFile}{err}";
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            if (!Directory.Exists(OutputDirectory))
            {
                Directory.CreateDirectory(OutputDirectory);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", OutputDirectory) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo("xdg-open", OutputDirectory) { UseShellExecute = true });
            }
        }
        catch
        {
            // Ignore if file manager cannot be opened on mobile
        }
    }

    [RelayCommand]
    private void ResetRecordShortcut()
    {
        RecordCtrl = true;
        RecordAlt = true;
        RecordShift = true;
        RecordWin = false;
        RecordVkCode = 0x35; // D5
        RecordKeyName = "D5";
    }

    [RelayCommand]
    private void ResetPauseShortcut()
    {
        PauseCtrl = true;
        PauseAlt = true;
        PauseShift = true;
        PauseWin = false;
        PauseVkCode = 0x38; // D8
        PauseKeyName = "D8";
    }

    [RelayCommand]
    private void ResetSpeakerDsp()
    {
        SpeakerSyncOffsetMs = 0;
        SpeakerGainDb = 0.0;
    }

    [RelayCommand]
    private void ResetMicDsp()
    {
        MicSyncOffsetMs = 0;
        MicGainDb = 0.0;
        MicNoiseSuppression = true;
        MicNoiseGate = false;
        MicNoiseGateThresholdDb = -36.0;
        MicHighPassFilter = true;
    }

    #endregion

    public void Dispose()
    {
        try { _liveHoldTimer?.Stop(); } catch { }
        try { _hotKeyService.Dispose(); } catch { }
        try { _engine.Dispose(); } catch { }
        try { _latencyDetector.Dispose(); } catch { }
        try { _vocalStudioWindow?.Close(); } catch { }
        try { _miniBarWindow?.Close(); } catch { }
    }
}
