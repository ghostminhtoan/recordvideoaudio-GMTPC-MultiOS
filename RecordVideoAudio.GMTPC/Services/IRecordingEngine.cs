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

public interface IRecordingEngine
{
    RecordingState CurrentState { get; }
    RecordingStats CurrentStats { get; }
    RecordingConfig ActiveConfig { get; }
    string LastErrorMessage { get; }

    event Action<RecordingState>? StateChanged;
    event Action<RecordingStats>? StatsUpdated;
    event Action<double, double>? AudioLevelsUpdated;

    Task<bool> StartRecordingAsync(RecordingConfig config);
    Task<bool> PauseRecordingAsync();
    Task<bool> ResumeRecordingAsync();
    Task<string> StopRecordingAsync();
}

public class RecordingEngine : IRecordingEngine
{
    private readonly IEncoderPipelineService _pipelineService;
    private readonly System.Timers.Timer _levelTimer;
    private readonly Random _random = new();

    private Process? _ffmpegProcess;
    private DateTime _startTime;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private DateTime _pauseStartTime;
    private readonly StringBuilder _stderrBuffer = new();

    public RecordingState CurrentState { get; private set; } = RecordingState.Idle;
    public RecordingStats CurrentStats { get; private set; } = new();
    public RecordingConfig ActiveConfig { get; private set; } = new();
    public string LastErrorMessage { get; private set; } = string.Empty;

    public event Action<RecordingState>? StateChanged;
    public event Action<RecordingStats>? StatsUpdated;
    public event Action<double, double>? AudioLevelsUpdated;

    public RecordingEngine(IEncoderPipelineService? pipelineService = null)
    {
        _pipelineService = pipelineService ?? new FFmpegPipelineService();
        _levelTimer = new System.Timers.Timer(150);
        _levelTimer.Elapsed += (s, e) => OnAudioLevelTick();
    }

    public Task<bool> StartRecordingAsync(RecordingConfig config)
    {
        if (CurrentState == RecordingState.Recording)
            return Task.FromResult(false);

        ActiveConfig = config;
        LastErrorMessage = string.Empty;
        _pausedDuration = TimeSpan.Zero;
        _stderrBuffer.Clear();

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

        string? ffmpegPath = _pipelineService.FindFFmpegExecutable();

        if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
        {
            try
            {
                string arguments = _pipelineService.BuildArguments(config, CurrentStats.OutputFilePath);

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
        _levelTimer.Start();
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
        _levelTimer.Stop();

        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try
            {
                // Send 'q' to gracefully stop FFmpeg and write container trailer / moov atom
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

        string finalPath = CurrentStats.OutputFilePath;

        // Verify resulting file
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

        lock (_stderrBuffer)
        {
            _stderrBuffer.AppendLine(e.Data);
            if (_stderrBuffer.Length > 8000)
            {
                _stderrBuffer.Remove(0, 4000);
            }
        }

        // Parse line: frame=  123 fps= 60.0 q=20.0 size=    1536kB time=00:00:02.05 bitrate=6138.4kbits/s
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
    }

    private void OnAudioLevelTick()
    {
        if (CurrentState != RecordingState.Recording)
            return;

        double sysLevel = ActiveConfig.RecordSystemAudio ? (_random.NextDouble() * 70 + 20) * (ActiveConfig.SystemAudioVolume / 100.0) : 0;
        double micLevel = ActiveConfig.RecordMicrophone ? (_random.NextDouble() * 65 + 15) * (ActiveConfig.MicrophoneVolume / 100.0) : 0;
        AudioLevelsUpdated?.Invoke(sysLevel, micLevel);
    }
}
