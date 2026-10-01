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

    Task<bool> StartRecordingAsync(RecordingConfig config);
    Task<bool> PauseRecordingAsync();
    Task<bool> ResumeRecordingAsync();
    Task<string> StopRecordingAsync();
    void UpdateAudioMonitoringSettings(
        bool speakerEnabled, double speakerVolume,
        bool micEnabled, double micVolume,
        double speakerGainDb = 0.0, double micGainDb = 0.0,
        bool micNoiseGate = false, double micNoiseGateThresholdDb = -36.0);
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
    private bool _monitorMicNoiseGate = false;
    private double _monitorMicNoiseGateThresholdDb = -36.0;

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

    public void UpdateAudioMonitoringSettings(
        bool speakerEnabled, double speakerVolume,
        bool micEnabled, double micVolume,
        double speakerGainDb = 0.0, double micGainDb = 0.0,
        bool micNoiseGate = false, double micNoiseGateThresholdDb = -36.0)
    {
        _monitorSpeaker = speakerEnabled;
        _monitorSpeakerVolume = speakerVolume;
        _monitorSpeakerGainDb = speakerGainDb;
        _monitorMic = micEnabled;
        _monitorMicVolume = micVolume;
        _monitorMicGainDb = micGainDb;
        _monitorMicNoiseGate = micNoiseGate;
        _monitorMicNoiseGateThresholdDb = micNoiseGateThresholdDb;
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

        // 1. Start WASAPI Hardware Audio Recording for Speaker & Mic
        if (config.RecordSystemAudio || config.RecordMicrophone)
        {
            string? audioErr = _audioRecorder.StartRecording(
                config.RecordSystemAudio,
                config.SystemAudioVolume,
                config.RecordMicrophone,
                config.MicrophoneVolume,
                outDir
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
                // Intermediate video file
                string ext = _pipelineService.GetOutputExtension(config.Format);
                _tempVideoPath = Path.Combine(outDir, $"temp_video_{Guid.NewGuid():N}{ext}");

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

                _ffmpegProcess = new Process { StartInfo = psi };
                _ffmpegProcess.ErrorDataReceived += OnFFmpegErrorDataReceived;

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

        // 1. Stop FFmpeg Video Process gracefully
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                await _ffmpegProcess.StandardInput.WriteLineAsync("q");
                await _ffmpegProcess.StandardInput.FlushAsync();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ffmpegProcess.WaitForExitAsync(cts.Token);
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

        // 3. Mux Video + Audio using FFmpeg (lossless video copy, ultra-fast ~0.5s)
        if (File.Exists(_tempVideoPath) && !string.IsNullOrEmpty(ffmpegPath))
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
                    CreateNoWindow = true
                };

                using var muxProcess = Process.Start(muxPsi);
                if (muxProcess != null)
                {
                    await muxProcess.WaitForExitAsync();
                }
            }
            catch { }
            finally
            {
                // Clean temporary files
                try { if (File.Exists(_tempVideoPath)) File.Delete(_tempVideoPath); } catch { }
                try { if (!string.IsNullOrEmpty(speakerWav) && File.Exists(speakerWav)) File.Delete(speakerWav); } catch { }
                try { if (!string.IsNullOrEmpty(micWav) && File.Exists(micWav)) File.Delete(micWav); } catch { }
            }
        }
        else if (File.Exists(_tempVideoPath))
        {
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(_tempVideoPath, finalPath);
            }
            catch { }
        }

        // 4. Update file statistics
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
        _levelTimer.Stop();
        _levelTimer.Dispose();
        _audioMonitor.Dispose();
        _audioRecorder.Dispose();
    }
}
