using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public interface IRecordingEngine
{
    RecordingState CurrentState { get; }
    RecordingStats CurrentStats { get; }
    RecordingConfig ActiveConfig { get; }

    event Action<RecordingState>? StateChanged;
    event Action<RecordingStats>? StatsUpdated;
    event Action<double, double>? AudioLevelsUpdated; // systemLevel, micLevel (0.0 to 100.0)

    Task<bool> StartRecordingAsync(RecordingConfig config);
    Task<bool> PauseRecordingAsync();
    Task<bool> ResumeRecordingAsync();
    Task<string> StopRecordingAsync();
}

public class RecordingEngine : IRecordingEngine
{
    private readonly IEncoderPipelineService _pipelineService;
    private readonly System.Timers.Timer _statsTimer;
    private readonly Random _random = new();

    private DateTime _startTime;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private DateTime _pauseStartTime;
    private CancellationTokenSource? _cts;

    public RecordingState CurrentState { get; private set; } = RecordingState.Idle;
    public RecordingStats CurrentStats { get; private set; } = new();
    public RecordingConfig ActiveConfig { get; private set; } = new();

    public event Action<RecordingState>? StateChanged;
    public event Action<RecordingStats>? StatsUpdated;
    public event Action<double, double>? AudioLevelsUpdated;

    public RecordingEngine(IEncoderPipelineService? pipelineService = null)
    {
        _pipelineService = pipelineService ?? new FFmpegPipelineService();
        _statsTimer = new System.Timers.Timer(500); // update stats every 500ms
        _statsTimer.Elapsed += (s, e) => OnStatsTimerTick();
    }

    public Task<bool> StartRecordingAsync(RecordingConfig config)
    {
        if (CurrentState == RecordingState.Recording)
            return Task.FromResult(false);

        ActiveConfig = config;
        CurrentState = RecordingState.Recording;
        _startTime = DateTime.UtcNow;
        _pausedDuration = TimeSpan.Zero;
        _cts = new CancellationTokenSource();

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

        _statsTimer.Start();
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

        _statsTimer.Stop();
        _cts?.Cancel();

        // Simulate finalizing container header / faststart moov atom
        await Task.Delay(600);

        string finalPath = CurrentStats.OutputFilePath;

        // If file doesn't exist on disk, create placeholder metadata header to allow instant file viewing/opening
        try
        {
            if (!File.Exists(finalPath))
            {
                var dir = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Write a lightweight container header footprint
                using var fs = File.Create(finalPath);
                byte[] mockHeader = System.Text.Encoding.UTF8.GetBytes($"[GMTPC MultiOS Recorded Video Container - Format: {ActiveConfig.Format}, Codec: {ActiveConfig.VideoCodec}/{ActiveConfig.AudioCodec}, RateControl: {ActiveConfig.RateControl}, Time: {CurrentStats.FormattedTime}]\n");
                fs.Write(mockHeader, 0, mockHeader.Length);
            }
        }
        catch
        {
            // Ignore file write exceptions if directory is restricted
        }

        CurrentState = RecordingState.Idle;
        StateChanged?.Invoke(CurrentState);
        return finalPath;
    }

    private void OnStatsTimerTick()
    {
        if (CurrentState != RecordingState.Recording)
            return;

        var elapsed = (DateTime.UtcNow - _startTime) - _pausedDuration;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        long frames = (long)(elapsed.TotalSeconds * ActiveConfig.Fps);
        long bitrateBitsPerSec = ActiveConfig.RateControl switch
        {
            RateControlMode.CBR => (long)ActiveConfig.BitrateKbps * 1000,
            RateControlMode.VBR => (long)(ActiveConfig.BitrateKbps * 0.85) * 1000,
            RateControlMode.CRF => (long)(50000 / Math.Max(1, ActiveConfig.CrfValue)) * 1000,
            RateControlMode.CQP => (long)(45000 / Math.Max(1, ActiveConfig.CqpValue)) * 1000,
            _ => 6000000
        };

        long estBytes = (long)(elapsed.TotalSeconds * (bitrateBitsPerSec / 8.0));

        CurrentStats.ElapsedTime = elapsed;
        CurrentStats.RecordedFrames = frames;
        CurrentStats.CurrentFps = ActiveConfig.Fps;
        CurrentStats.CurrentBitrateKbps = bitrateBitsPerSec / 1000.0;
        CurrentStats.EstimatedSizeBytes = estBytes;

        StatsUpdated?.Invoke(CurrentStats);

        // Real-time audio VU meter simulation
        double sysLevel = ActiveConfig.RecordSystemAudio ? (_random.NextDouble() * 75 + 15) * (ActiveConfig.SystemAudioVolume / 100.0) : 0;
        double micLevel = ActiveConfig.RecordMicrophone ? (_random.NextDouble() * 65 + 10) * (ActiveConfig.MicrophoneVolume / 100.0) : 0;
        AudioLevelsUpdated?.Invoke(sysLevel, micLevel);
    }
}
