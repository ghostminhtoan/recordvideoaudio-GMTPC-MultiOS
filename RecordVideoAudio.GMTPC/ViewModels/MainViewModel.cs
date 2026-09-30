using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordVideoAudio.GMTPC.Localization;
using RecordVideoAudio.GMTPC.Models;
using RecordVideoAudio.GMTPC.Services;

namespace RecordVideoAudio.GMTPC.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IRecordingEngine _engine;
    private readonly IEncoderPipelineService _pipeline;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public MainViewModel()
    {
        _pipeline = new FFmpegPipelineService();
        _engine = new RecordingEngine(_pipeline);

        _engine.StateChanged += Engine_StateChanged;
        _engine.StatsUpdated += Engine_StatsUpdated;
        _engine.AudioLevelsUpdated += Engine_AudioLevelsUpdated;

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
        Profiles = new ObservableCollection<QualityProfile>(QualityProfile.GetBuiltInProfiles());

        // Default folder
        OutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");

        UpdateTranslations();
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

    [ObservableProperty]
    private QualityProfile? selectedProfile;

    [ObservableProperty]
    private bool systemAudioEnabled = true;

    [ObservableProperty]
    private int systemAudioVolume = 100;

    [ObservableProperty]
    private double systemAudioLevel = 0;

    [ObservableProperty]
    private bool micAudioEnabled = true;

    [ObservableProperty]
    private int micAudioVolume = 90;

    [ObservableProperty]
    private double micAudioLevel = 0;

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
    private string statusMessage = "Hệ thống sẵn sàng ghi hình.";

    public bool IsRecording => CurrentState == RecordingState.Recording;
    public bool IsPaused => CurrentState == RecordingState.Paused;
    public bool IsIdle => CurrentState == RecordingState.Idle;
    public bool IsNotRecording => CurrentState == RecordingState.Idle;

    public bool IsCrfMode => SelectedRateControl == RateControlMode.CRF;
    public bool IsCqpMode => SelectedRateControl == RateControlMode.CQP;
    public bool IsCbrMode => SelectedRateControl == RateControlMode.CBR;
    public bool IsVbrMode => SelectedRateControl == RateControlMode.VBR;

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
    partial void OnSystemAudioEnabledChanged(bool value) => RefreshCommandPreview();
    partial void OnMicAudioEnabledChanged(bool value) => RefreshCommandPreview();

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
            RecordSystemAudio = SystemAudioEnabled,
            SystemAudioVolume = SystemAudioVolume,
            RecordMicrophone = MicAudioEnabled,
            MicrophoneVolume = MicAudioVolume,
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
                StatusMessage = "🔴 Đang quay phim & thu âm trực tiếp...";
            else if (state == RecordingState.Paused)
                StatusMessage = "⏸️ Quá trình quay đang tạm dừng.";
            else if (state == RecordingState.Idle)
                StatusMessage = "✅ Đã lưu video thành công.";
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

    #endregion

    #region Commands

    [RelayCommand]
    private void ToggleLanguage()
    {
        _loc.ToggleLanguage();
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
        StatusMessage = $"{_loc.GetText("SavedTo")} {savedFile}";
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

    #endregion
}
