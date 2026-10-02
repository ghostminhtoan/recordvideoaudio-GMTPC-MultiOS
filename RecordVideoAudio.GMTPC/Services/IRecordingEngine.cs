using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public interface IRecordingEngine : IDisposable
{
    RecordingState CurrentState { get; }
    RecordingStats CurrentStats { get; }
    RecordingConfig ActiveConfig { get; }
    string LastErrorMessage { get; }

    event Action<RecordingState>? StateChanged;
    event Action<RecordingStats>? StatsUpdated;
    event Action<double, double>? AudioLevelsUpdated; // speakerLevel, micLevel (0-100)
    event Action? AutoStopped;

    WasapiAudioRecorder AudioRecorder { get; }

    Task<bool> StartRecordingAsync(RecordingConfig config);
    Task<bool> PauseRecordingAsync();
    Task<bool> ResumeRecordingAsync();
    Task<string> StopRecordingAsync();
    void UpdateAudioMonitoringSettings(
        bool speakerEnabled, double speakerVolume,
        bool micEnabled, double micVolume,
        double speakerGainDb = 0.0, double micGainDb = 0.0,
        bool micNoiseSuppression = true,
        bool micNoiseGate = false, double micNoiseGateThresholdDb = -36.0,
        bool micHighPassFilter = true,
        bool micCompressor = true, double micCompressorThresholdDb = -18.0, double micCompressorRatio = 4.0,
        VocalProfile micVocalProfile = VocalProfile.BroadcastWarmth, bool micDeEsser = true,
        bool micAutoTune = false, MusicalKey micAutoTuneKey = MusicalKey.C, AutoTuneScale micAutoTuneScale = AutoTuneScale.Chromatic,
        int micAutoTuneSpeed = 20, int micPitchShiftSemitones = 0,
        bool micEcho = false, int micEchoDelayMs = 220, double micEchoFeedback = 35.0, double micEchoWetMix = 30.0,
        bool micReverb = false, double micReverbRoomSize = 50.0, double micReverbDamping = 40.0, double micReverbWetMix = 25.0,
        bool speakerAutoDucking = false);

    bool IsAudioMonitoringEnabled { get; }
    void SetAudioMonitoring(bool enabled, AudioMonitorMode mode, double volume, bool masterMixDucking = true);
    void SetMasterMixDucking(bool enabled);
    void SetSelectedAudioDevices(string? speakerDeviceId, string? micDeviceId);
}

public class RecordingEngine : IRecordingEngine
{
    private readonly IEncoderPipelineService _pipelineService;
    private readonly RealtimeAudioMonitor _audioMonitor;
    private readonly WasapiAudioRecorder _audioRecorder;
    private readonly System.Timers.Timer _levelTimer;

    private Process? _ffmpegProcess;
    private DateTime _startTime;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private DateTime _pauseStartTime;
    private string _tempVideoPath = string.Empty;

    private bool _monitorSpeaker = true;
    private double _monitorSpeakerVolume = 100;
    private double _monitorSpeakerGainDb = 0.0;
    private bool _monitorMic = true;
    private double _monitorMicVolume = 90;
    private double _monitorMicGainDb = 0.0;
    private bool _monitorMicNoiseSuppression = true;
    private bool _monitorMicNoiseGate = false;
    private double _monitorMicNoiseGateThresholdDb = -36.0;
    private bool _monitorMicHighPassFilter = true;
    private bool _monitorMicCompressor = true;
    private double _monitorMicCompressorThresholdDb = -18.0;
    private double _monitorMicCompressorRatio = 4.0;
    private VocalProfile _monitorMicVocalProfile = VocalProfile.BroadcastWarmth;
    private bool _monitorMicDeEsser = true;
    private bool _monitorMicAutoTune = false;
    private MusicalKey _monitorMicAutoTuneKey = MusicalKey.C;
    private AutoTuneScale _monitorMicAutoTuneScale = AutoTuneScale.Chromatic;
    private int _monitorMicAutoTuneSpeed = 20;
    private int _monitorMicPitchShiftSemitones = 0;

    public RecordingState CurrentState { get; private set; } = RecordingState.Idle;
    public RecordingStats CurrentStats { get; private set; } = new();
    public RecordingConfig ActiveConfig { get; private set; } = new();
    public string LastErrorMessage { get; private set; } = string.Empty;

    public event Action<RecordingState>? StateChanged;
    public event Action<RecordingStats>? StatsUpdated;
    public event Action<double, double>? AudioLevelsUpdated;
    public event Action? AutoStopped;

    public RecordingEngine(IEncoderPipelineService? pipelineService = null)
    {
        _pipelineService = pipelineService ?? new FFmpegPipelineService();
        _audioMonitor = new RealtimeAudioMonitor();
        _audioRecorder = new WasapiAudioRecorder();

        // 60ms timer for smooth 16+ FPS real-time audio VU meter tracing
        _levelTimer = new System.Timers.Timer(60);
        _levelTimer.Elapsed += (s, e) => OnAudioLevelTick();
        _levelTimer.Start();
    }

    public WasapiAudioRecorder AudioRecorder => _audioRecorder;
    public bool IsAudioMonitoringEnabled => _audioRecorder.IsMonitoring;

    public void SetAudioMonitoring(bool enabled, AudioMonitorMode mode, double volume, bool masterMixDucking = true)
    {
        _audioRecorder.SetMonitoring(enabled, mode, (float)(volume / 100.0), masterMixDucking);
    }

    public void SetMasterMixDucking(bool enabled)
    {
        _audioRecorder.SetMasterMixDucking(enabled);
    }

    public void SetSelectedAudioDevices(string? speakerDeviceId, string? micDeviceId)
    {
        _audioRecorder.SetSelectedDevices(speakerDeviceId, micDeviceId);
        _audioMonitor.UpdateSelectedDevices(speakerDeviceId, micDeviceId);
    }

    private bool _monitorMicEcho = false;
    private int _monitorMicEchoDelayMs = 220;
    private double _monitorMicEchoFeedback = 35.0;
    private double _monitorMicEchoWetMix = 30.0;
    private bool _monitorMicReverb = false;
    private double _monitorMicReverbRoomSize = 50.0;
    private double _monitorMicReverbDamping = 40.0;
    private double _monitorMicReverbWetMix = 25.0;
    private bool _monitorSpeakerAutoDucking = false;

    public void UpdateAudioMonitoringSettings(
        bool speakerEnabled, double speakerVolume,
        bool micEnabled, double micVolume,
        double speakerGainDb = 0.0, double micGainDb = 0.0,
        bool micNoiseSuppression = true,
        bool micNoiseGate = false, double micNoiseGateThresholdDb = -36.0,
        bool micHighPassFilter = true,
        bool micCompressor = true, double micCompressorThresholdDb = -18.0, double micCompressorRatio = 4.0,
        VocalProfile micVocalProfile = VocalProfile.BroadcastWarmth, bool micDeEsser = true,
        bool micAutoTune = false, MusicalKey micAutoTuneKey = MusicalKey.C, AutoTuneScale micAutoTuneScale = AutoTuneScale.Chromatic,
        int micAutoTuneSpeed = 20, int micPitchShiftSemitones = 0,
        bool micEcho = false, int micEchoDelayMs = 220, double micEchoFeedback = 35.0, double micEchoWetMix = 30.0,
        bool micReverb = false, double micReverbRoomSize = 50.0, double micReverbDamping = 40.0, double micReverbWetMix = 25.0,
        bool speakerAutoDucking = false)
    {
        _monitorSpeaker = speakerEnabled;
        _monitorSpeakerVolume = speakerVolume;
        _monitorSpeakerGainDb = speakerGainDb;
        _monitorSpeakerAutoDucking = speakerAutoDucking;
        _monitorMic = micEnabled;
        _monitorMicVolume = micVolume;
        _monitorMicGainDb = micGainDb;
        _monitorMicNoiseSuppression = micNoiseSuppression;
        _monitorMicNoiseGate = micNoiseGate;
        _monitorMicNoiseGateThresholdDb = micNoiseGateThresholdDb;
        _monitorMicHighPassFilter = micHighPassFilter;
        _monitorMicCompressor = micCompressor;
        _monitorMicCompressorThresholdDb = micCompressorThresholdDb;
        _monitorMicCompressorRatio = micCompressorRatio;
        _monitorMicVocalProfile = micVocalProfile;
        _monitorMicDeEsser = micDeEsser;
        _monitorMicAutoTune = micAutoTune;
        _monitorMicAutoTuneKey = micAutoTuneKey;
        _monitorMicAutoTuneScale = micAutoTuneScale;
        _monitorMicAutoTuneSpeed = micAutoTuneSpeed;
        _monitorMicPitchShiftSemitones = micPitchShiftSemitones;
        _monitorMicEcho = micEcho;
        _monitorMicEchoDelayMs = micEchoDelayMs;
        _monitorMicEchoFeedback = micEchoFeedback;
        _monitorMicEchoWetMix = micEchoWetMix;
        _monitorMicReverb = micReverb;
        _monitorMicReverbRoomSize = micReverbRoomSize;
        _monitorMicReverbDamping = micReverbDamping;
        _monitorMicReverbWetMix = micReverbWetMix;

        // Forward immediately to WasapiAudioRecorder for dynamic real-time DSP during active recording
        _audioRecorder.UpdateRealtimeSettings(
            speakerEnabled, speakerVolume, speakerGainDb,
            micEnabled, micVolume, micGainDb,
            micNoiseSuppression, micNoiseGate, micNoiseGateThresholdDb,
            micHighPassFilter,
            micCompressor, micCompressorThresholdDb, micCompressorRatio,
            micVocalProfile, micDeEsser,
            micAutoTune, micAutoTuneKey, micAutoTuneScale,
            micAutoTuneSpeed, micPitchShiftSemitones,
            micEcho, micEchoDelayMs, micEchoFeedback, micEchoWetMix,
            micReverb, micReverbRoomSize, micReverbDamping, micReverbWetMix,
            speakerAutoDucking
        );

        if (ActiveConfig != null)
        {
            ActiveConfig.RecordSystemAudio = speakerEnabled;
            ActiveConfig.SystemAudioVolume = (int)speakerVolume;
            ActiveConfig.SpeakerGainDb = speakerGainDb;
            ActiveConfig.SpeakerAutoDucking = speakerAutoDucking;
            ActiveConfig.RecordMicrophone = micEnabled;
            ActiveConfig.MicrophoneVolume = (int)micVolume;
            ActiveConfig.MicGainDb = micGainDb;
            ActiveConfig.MicNoiseSuppression = micNoiseSuppression;
            ActiveConfig.MicNoiseGate = micNoiseGate;
            ActiveConfig.MicNoiseGateThresholdDb = micNoiseGateThresholdDb;
            ActiveConfig.MicHighPassFilter = micHighPassFilter;
            ActiveConfig.MicCompressor = micCompressor;
            ActiveConfig.MicCompressorThresholdDb = micCompressorThresholdDb;
            ActiveConfig.MicCompressorRatio = micCompressorRatio;
            ActiveConfig.MicVocalProfile = micVocalProfile;
            ActiveConfig.MicDeEsser = micDeEsser;
            ActiveConfig.MicAutoTune = micAutoTune;
            ActiveConfig.MicAutoTuneKey = micAutoTuneKey;
            ActiveConfig.MicAutoTuneScale = micAutoTuneScale;
            ActiveConfig.MicAutoTuneSpeed = micAutoTuneSpeed;
            ActiveConfig.MicPitchShiftSemitones = micPitchShiftSemitones;
            ActiveConfig.MicEcho = micEcho;
            ActiveConfig.MicEchoDelayMs = micEchoDelayMs;
            ActiveConfig.MicEchoFeedback = micEchoFeedback;
            ActiveConfig.MicEchoWetMix = micEchoWetMix;
            ActiveConfig.MicReverb = micReverb;
            ActiveConfig.MicReverbRoomSize = micReverbRoomSize;
            ActiveConfig.MicReverbDamping = micReverbDamping;
            ActiveConfig.MicReverbWetMix = micReverbWetMix;
        }
    }

    public Task<bool> StartRecordingAsync(RecordingConfig config)
    {
        if (CurrentState == RecordingState.Recording)
            return Task.FromResult(false);

        ActiveConfig = config;
        LastErrorMessage = string.Empty;
        _pausedDuration = TimeSpan.Zero;

        string outDir = string.IsNullOrWhiteSpace(config.OutputDirectory)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings")
            : config.OutputDirectory;

        if (!Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string fileName = string.IsNullOrWhiteSpace(config.CustomFileName)
            ? _pipelineService.GenerateDefaultFileName(config.Format)
            : config.CustomFileName;

        CurrentStats = new RecordingStats
        {
            OutputFilePath = Path.Combine(outDir, fileName),
            ElapsedTime = TimeSpan.Zero,
            RecordedFrames = 0,
            DroppedFrames = 0,
            CurrentFps = config.Fps,
            CurrentBitrateKbps = config.BitrateKbps,
            EstimatedSizeBytes = 0
        };

        // 1. Start WASAPI Hardware Audio Recording for Speaker & Mic with Full DSP
        if (config.RecordSystemAudio || config.RecordMicrophone)
        {
            string? audioErr = _audioRecorder.StartRecording(
                config.RecordSystemAudio,
                config.SystemAudioVolume,
                config.SpeakerGainDb,
                config.RecordMicrophone,
                config.MicrophoneVolume,
                config.MicGainDb,
                config.MicNoiseSuppression,
                config.MicNoiseGate,
                config.MicNoiseGateThresholdDb,
                config.MicHighPassFilter,
                config.MicCompressor,
                config.MicCompressorThresholdDb,
                config.MicCompressorRatio,
                config.MicVocalProfile,
                config.MicDeEsser,
                config.MicAutoTune,
                config.MicAutoTuneKey,
                config.MicAutoTuneScale,
                config.MicAutoTuneSpeed,
                config.MicPitchShiftSemitones,
                outDir,
                config.MicEcho,
                config.MicEchoDelayMs,
                config.MicEchoFeedback,
                config.MicEchoWetMix,
                config.MicReverb,
                config.MicReverbRoomSize,
                config.MicReverbDamping,
                config.MicReverbWetMix,
                config.SpeakerAutoDucking,
                config.SelectedSpeakerDeviceId,
                config.SelectedMicrophoneDeviceId
            );
            if (!string.IsNullOrEmpty(audioErr))
            {
                LastErrorMessage = $"Lưu ý âm thanh: {audioErr}";
            }
        }

        // 2. Start Video Capture with FFmpeg
        string? ffmpegPath = _pipelineService.FindFFmpegExecutable();
        if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
        {
            try
            {
                // Intermediate streamable container: Always use MKV for temporary video recording to prevent moov atom corruption on crash/kill
                _tempVideoPath = Path.Combine(outDir, $"temp_video_{Guid.NewGuid():N}.mkv");

                string arguments = _pipelineService.BuildArguments(config, _tempVideoPath, true);

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = outDir
                };

                _ffmpegProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _ffmpegProcess.ErrorDataReceived += OnFFmpegErrorDataReceived;
                _ffmpegProcess.Exited += (s, e) =>
                {
                    if (CurrentState == RecordingState.Recording)
                    {
                        LastErrorMessage = "Cảnh báo: Tiến trình ghi hình FFmpeg đã dừng đột ngột.";
                    }
                };

                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                _startTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Lỗi khởi chạy FFmpeg: {ex.Message}";
                _ffmpegProcess = null;
                _startTime = DateTime.UtcNow;
            }
        }
        else
        {
            LastErrorMessage = "Không tìm thấy ffmpeg.exe trong thư mục ứng dụng hoặc hệ thống.";
            _startTime = DateTime.UtcNow;
        }

        CurrentState = RecordingState.Recording;
        StateChanged?.Invoke(CurrentState);
        return Task.FromResult(true);
    }

    public Task<bool> PauseRecordingAsync()
    {
        if (CurrentState != RecordingState.Recording)
            return Task.FromResult(false);

        CurrentState = RecordingState.Paused;
        _pauseStartTime = DateTime.UtcNow;
        StateChanged?.Invoke(CurrentState);
        return Task.FromResult(true);
    }

    public Task<bool> ResumeRecordingAsync()
    {
        if (CurrentState != RecordingState.Paused)
            return Task.FromResult(false);

        _pausedDuration += DateTime.UtcNow - _pauseStartTime;
        CurrentState = RecordingState.Recording;
        StateChanged?.Invoke(CurrentState);
        return Task.FromResult(true);
    }

    public async Task<string> StopRecordingAsync()
    {
        if (CurrentState == RecordingState.Idle)
            return string.Empty;

        CurrentState = RecordingState.Finalizing;
        StateChanged?.Invoke(CurrentState);

        // 1. Stop FFmpeg Video Process gracefully with 15s timeout and active polling
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                await _ffmpegProcess.StandardInput.WriteLineAsync("q");
                await _ffmpegProcess.StandardInput.FlushAsync();

                var stopWatch = Stopwatch.StartNew();
                while (!_ffmpegProcess.HasExited && stopWatch.ElapsedMilliseconds < 15000)
                {
                    await Task.Delay(100);
                }

                if (!_ffmpegProcess.HasExited)
                {
                    try { _ffmpegProcess.Kill(true); } catch { }
                    await Task.Delay(200);
                }
            }
            catch
            {
                try
                {
                    if (!_ffmpegProcess.HasExited)
                    {
                        _ffmpegProcess.Kill(true);
                    }
                }
                catch { }
            }
            finally
            {
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }
        }

        // 2. Stop WASAPI Audio Recording
        var (speakerWav, micWav) = _audioRecorder.StopRecording();

        string finalPath = CurrentStats.OutputFilePath;
        string? ffmpegPath = _pipelineService.FindFFmpegExecutable();

        bool hasValidTempVideo = File.Exists(_tempVideoPath) && new FileInfo(_tempVideoPath).Length > 0;
        bool muxSuccess = false;

        // 3. Mux Video + Audio using FFmpeg (lossless video stream copy, ultra-fast ~0.5s)
        if (hasValidTempVideo && !string.IsNullOrEmpty(ffmpegPath))
        {
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);

                string muxArgs = _pipelineService.BuildMuxArguments(_tempVideoPath, speakerWav, micWav, finalPath, ActiveConfig);

                var muxPsi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = muxArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using var muxProcess = Process.Start(muxPsi);
                if (muxProcess != null)
                {
                    string muxErr = await muxProcess.StandardError.ReadToEndAsync();
                    await muxProcess.WaitForExitAsync();

                    if (muxProcess.ExitCode == 0 && File.Exists(finalPath) && new FileInfo(finalPath).Length > 1024)
                    {
                        muxSuccess = true;
                    }
                    else
                    {
                        LastErrorMessage = $"Muxing không thành công: {muxErr}";
                    }
                }
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Ngoại lệ khi Muxing: {ex.Message}";
            }
        }

        // 4. Multi-Tier Emergency Rescue Pipeline (Bảo vệ dữ liệu người dùng)
        if (!muxSuccess && hasValidTempVideo)
        {
            try
            {
                // Cấp cứu 1: Thử remux copy chỉ video sang finalPath (loại bỏ track audio bị lỗi)
                if (!string.IsNullOrEmpty(ffmpegPath))
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    string faststart = ActiveConfig.Format == ContainerFormat.MP4 ? "-movflags +faststart " : "";
                    string rescueArgs = $"-y -i \"{_tempVideoPath}\" -c:v copy {faststart}\"{finalPath}\"";

                    var rescuePsi = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = rescueArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var rescueProcess = Process.Start(rescuePsi);
                    if (rescueProcess != null)
                    {
                        await rescueProcess.WaitForExitAsync();
                        if (rescueProcess.ExitCode == 0 && File.Exists(finalPath) && new FileInfo(finalPath).Length > 1024)
                        {
                            muxSuccess = true;
                            LastErrorMessage = "Đã cứu hộ video thành công (loại bỏ track âm thanh bị lỗi).";
                        }
                    }
                }

                // Cấp cứu 2: Nếu remux vẫn thất bại, copy trực tiếp file tạm MKV sang thư mục đích
                if (!muxSuccess)
                {
                    string outDir = Path.GetDirectoryName(finalPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string rescueFileName = $"RESCUED_{Path.GetFileNameWithoutExtension(finalPath)}.mkv";
                    string rescueDestPath = Path.Combine(outDir, rescueFileName);

                    File.Copy(_tempVideoPath, rescueDestPath, true);
                    finalPath = rescueDestPath;
                    CurrentStats.OutputFilePath = finalPath;
                    muxSuccess = true;
                    LastErrorMessage = $"Đã bảo toàn nguyên vẹn file video tại: {rescueFileName}";
                }
            }
            catch (Exception rescueEx)
            {
                LastErrorMessage = $"Không thể cứu hộ file tạm: {rescueEx.Message}";
            }
        }
        else if (!hasValidTempVideo && File.Exists(finalPath) && new FileInfo(finalPath).Length > 1024)
        {
            muxSuccess = true;
        }

        // 5. Cleanup Gate: CHỈ XÓA FILE TẠM KHI ĐÃ CÓ FILE ĐÍCH HOẶC FILE CỨU HỘ ĐẠT CHUẨN!
        if (muxSuccess)
        {
            try { if (File.Exists(_tempVideoPath)) File.Delete(_tempVideoPath); } catch { }
            try { if (!string.IsNullOrEmpty(speakerWav) && File.Exists(speakerWav)) File.Delete(speakerWav); } catch { }
            try { if (!string.IsNullOrEmpty(micWav) && File.Exists(micWav)) File.Delete(micWav); } catch { }
        }
        else
        {
            // Trường hợp cực hiếm: giữ nguyên file tạm để người dùng không mất dữ liệu
            if (File.Exists(_tempVideoPath))
            {
                finalPath = _tempVideoPath;
                CurrentStats.OutputFilePath = finalPath;
            }
        }

        // 6. Cập nhật kích thước file thực tế
        if (File.Exists(finalPath))
        {
            var fi = new FileInfo(finalPath);
            CurrentStats.EstimatedSizeBytes = fi.Length;
        }

        CurrentState = RecordingState.Idle;
        StateChanged?.Invoke(CurrentState);
        return finalPath;
    }

    private void OnFFmpegErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;

        var frameMatch = Regex.Match(e.Data, @"frame=\s*(\d+)");
        var fpsMatch = Regex.Match(e.Data, @"fps=\s*([\d\.]+)");
        var timeMatch = Regex.Match(e.Data, @"time=\s*(\d+:\d+:\d+\.\d+)");
        var sizeMatch = Regex.Match(e.Data, @"size=\s*(\d+)kB");
        var bitrateMatch = Regex.Match(e.Data, @"bitrate=\s*([\d\.]+)kbits/s");

        if (timeMatch.Success)
        {
            if (TimeSpan.TryParse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var t))
            {
                CurrentStats.ElapsedTime = t;
            }
        }
        else
        {
            var elapsed = (DateTime.UtcNow - _startTime) - _pausedDuration;
            if (elapsed > TimeSpan.Zero) CurrentStats.ElapsedTime = elapsed;
        }

        if (frameMatch.Success && long.TryParse(frameMatch.Groups[1].Value, out var frames))
        {
            CurrentStats.RecordedFrames = frames;
        }

        if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var fps))
        {
            CurrentStats.CurrentFps = fps;
        }

        if (bitrateMatch.Success && double.TryParse(bitrateMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var br))
        {
            CurrentStats.CurrentBitrateKbps = br;
        }

        if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var kb))
        {
            CurrentStats.EstimatedSizeBytes = kb * 1024;
        }

        StatsUpdated?.Invoke(CurrentStats);

        // Auto-stop scheduled timer check
        if (ActiveConfig.AutoStopMinutes > 0 && CurrentStats.ElapsedTime.TotalMinutes >= ActiveConfig.AutoStopMinutes && CurrentState == RecordingState.Recording)
        {
            Task.Run(async () =>
            {
                await StopRecordingAsync();
                AutoStopped?.Invoke();
            });
        }
    }

    private void OnAudioLevelTick()
    {
        // Real-time audio VU meter readings directly from Windows sound hardware with Gain & Gate applied
        var (sysLevel, micLevel) = _audioMonitor.GetCurrentLevels(
            _monitorSpeaker,
            _monitorSpeakerVolume,
            _monitorSpeakerGainDb,
            _monitorMic,
            _monitorMicVolume,
            _monitorMicGainDb,
            _monitorMicNoiseGate,
            _monitorMicNoiseGateThresholdDb
        );

        AudioLevelsUpdated?.Invoke(sysLevel, micLevel);
    }

    public void Dispose()
    {
        try { _levelTimer.Stop(); } catch { }
        try { _levelTimer.Dispose(); } catch { }
        try { _audioMonitor.Dispose(); } catch { }
        try { _audioRecorder.Dispose(); } catch { }

        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                _ffmpegProcess.Kill();
                _ffmpegProcess.Dispose();
            }
            catch { }
            _ffmpegProcess = null;
        }
    }
}
