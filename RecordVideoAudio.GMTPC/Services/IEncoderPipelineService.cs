using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public interface IEncoderPipelineService
{
    string BuildCommandLine(RecordingConfig config, string outputPath);
    string BuildArguments(RecordingConfig config, string outputPath, bool videoOnly = false);
    string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, RecordingConfig config);
    string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, AudioCodecType audioCodec, ContainerFormat format, AudioTrackMode audioTrackMode = AudioTrackMode.MixToSingleTrack);
    string GetOutputExtension(ContainerFormat format);
    string GenerateDefaultFileName(ContainerFormat format);
    string? FindFFmpegExecutable();
    string? GetDetectedAudioDevice();
    string? GetDetectedWebcamDevice();
}

public class FFmpegPipelineService : IEncoderPipelineService
{
    private static string? _cachedAudioDevice = null;
    private static string? _cachedWebcamDevice = null;
    private static bool _devicesQueried = false;

    public string GetOutputExtension(ContainerFormat format) => format switch
    {
        ContainerFormat.MKV => ".mkv",
        ContainerFormat.MP4 => ".mp4",
        _ => ".mp4"
    };

    public string GenerateDefaultFileName(ContainerFormat format)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"GMTPC_Record_{timestamp}{GetOutputExtension(format)}";
    }

    public string? FindFFmpegExecutable()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string directPath = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(directPath)) return directPath;

        string distWin = Path.Combine(baseDir, "..", "..", "..", "..", "dist", "windows", "ffmpeg.exe");
        if (File.Exists(distWin)) return Path.GetFullPath(distWin);

        string userLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string vibePath = Path.Combine(userLocal, "vibe", "ffmpeg.exe");
        if (File.Exists(vibePath)) return vibePath;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(part.Trim(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    private void QueryDevices()
    {
        if (_devicesQueried) return;
        _devicesQueried = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string? ffmpeg = FindFFmpegExecutable();
        if (string.IsNullOrEmpty(ffmpeg)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p != null)
            {
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);

                var audioMatch = Regex.Match(stderr, "\"([^\"]+)\"\\s*\\(audio\\)");
                if (audioMatch.Success)
                {
                    _cachedAudioDevice = audioMatch.Groups[1].Value;
                }

                var videoMatch = Regex.Match(stderr, "\"([^\"]+)\"\\s*\\(video\\)");
                if (videoMatch.Success)
                {
                    _cachedWebcamDevice = videoMatch.Groups[1].Value;
                }
            }
        }
        catch { }
    }

    public string? GetDetectedAudioDevice()
    {
        QueryDevices();
        return _cachedAudioDevice;
    }

    public string? GetDetectedWebcamDevice()
    {
        QueryDevices();
        return _cachedWebcamDevice;
    }

    public string BuildCommandLine(RecordingConfig config, string outputPath)
    {
        return $"ffmpeg {BuildArguments(config, outputPath, false)}";
    }

    public string BuildArguments(RecordingConfig config, string outputPath, bool videoOnly = false)
    {
        var sb = new StringBuilder();
        sb.Append("-y ");

        // 1. Capture Inputs according to CaptureSource
        int dm = config.DrawMouse ? 1 : 0;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            switch (config.CaptureSource)
            {
                case CaptureSourceType.CustomArea:
                    int w = Math.Max(2, (config.AreaWidth / 2) * 2); // ensure even number
                    int h = Math.Max(2, (config.AreaHeight / 2) * 2);
                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse {dm} -offset_x {config.AreaX} -offset_y {config.AreaY} -video_size {w}x{h} -i desktop ");
                    break;

                case CaptureSourceType.ActiveWindow:
                    if (!string.IsNullOrWhiteSpace(config.SelectedWindowTitle))
                    {
                        string safeTitle = config.SelectedWindowTitle.Replace("\"", "\\\"");
                        sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse {dm} -i title=\"{safeTitle}\" ");
                    }
                    else
                    {
                        sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse {dm} -i desktop ");
                    }
                    break;

                case CaptureSourceType.CameraPiP:
                    string webcam = !string.IsNullOrEmpty(config.WebcamDeviceName)
                        ? config.WebcamDeviceName
                        : (GetDetectedWebcamDevice() ?? "Logi C270 HD WebCam");

                    var (camW, camH) = config.CameraPipSize switch
                    {
                        PipSize.Small => (320, 240),
                        PipSize.Medium => (480, 360),
                        PipSize.Large => (640, 480),
                        _ => (320, 240)
                    };

                    string overlayPos = config.CameraPipPosition switch
                    {
                        PipPosition.BottomRight => "main_w-overlay_w-20:main_h-overlay_h-20",
                        PipPosition.BottomLeft => "20:main_h-overlay_h-20",
                        PipPosition.TopRight => "main_w-overlay_w-20:20",
                        PipPosition.TopLeft => "20:20",
                        _ => "main_w-overlay_w-20:main_h-overlay_h-20"
                    };

                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse {dm} -i desktop ");
                    sb.Append($"-f dshow -i video=\"{webcam}\" ");
                    sb.Append($"-filter_complex \"[1:v]fps={config.Fps},scale={camW}:{camH}[cam];[0:v][cam]overlay={overlayPos}[vout]\" -map \"[vout]\" ");
                    break;

                case CaptureSourceType.FullScreen:
                default:
                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse {dm} -i desktop ");
                    break;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            sb.Append($"-f x11grab -framerate {config.Fps} -draw_mouse {dm} -i :0.0 ");
        }
        else
        {
            sb.Append($"-f android_camera -framerate {config.Fps} -i 0 ");
        }

        // 2. Video Codec
        string videoEncoder = config.VideoCodec switch
        {
            VideoCodecType.H264 => config.HwAcceleration switch
            {
                HwAccelType.NVENC => "h264_nvenc",
                HwAccelType.QSV => "h264_qsv",
                HwAccelType.AMF => "h264_amf",
                HwAccelType.VAAPI => "h264_vaapi",
                HwAccelType.MediaCodec => "h264_mediacodec",
                _ => "h264_nvenc"
            },
            VideoCodecType.HEVC => config.HwAcceleration switch
            {
                HwAccelType.NVENC => "hevc_nvenc",
                HwAccelType.QSV => "hevc_qsv",
                HwAccelType.AMF => "hevc_amf",
                HwAccelType.VAAPI => "hevc_vaapi",
                HwAccelType.MediaCodec => "hevc_mediacodec",
                _ => "hevc_nvenc"
            },
            _ => "h264_nvenc"
        };
        sb.Append($"-c:v {videoEncoder} ");

        // 3. Preset
        string preset = config.Preset.ToString().ToLowerInvariant();
        sb.Append($"-preset {preset} ");

        // 4. Rate Control Modes
        bool isNvenc = videoEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
        switch (config.RateControl)
        {
            case RateControlMode.CRF:
                if (isNvenc)
                {
                    sb.Append($"-cq {config.CrfValue} -qmin {Math.Max(0, config.CrfValue - 3)} -qmax {Math.Min(51, config.CrfValue + 3)} ");
                }
                else
                {
                    sb.Append($"-crf {config.CrfValue} ");
                }
                break;

            case RateControlMode.CQP:
                if (isNvenc)
                {
                    sb.Append($"-rc constqp -qp {config.CqpValue} ");
                }
                else
                {
                    sb.Append($"-qp {config.CqpValue} ");
                }
                break;

            case RateControlMode.CBR:
                sb.Append($"-b:v {config.BitrateKbps}k -minrate {config.BitrateKbps}k -maxrate {config.BitrateKbps}k -bufsize {config.BitrateKbps * 2}k ");
                break;

            case RateControlMode.VBR:
                sb.Append($"-b:v {config.BitrateKbps}k -maxrate {config.MaxBitrateKbps}k -bufsize {config.MaxBitrateKbps * 2}k ");
                break;
        }

        // Pixel format
        sb.Append("-pix_fmt yuv420p ");

        // 5. Container Format Flags
        if (config.Format == ContainerFormat.MP4)
        {
            sb.Append("-movflags +faststart ");
        }

        // 6. Output Destination
        sb.Append($"\"{outputPath}\"");

        return sb.ToString();
    }

    public string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, RecordingConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("-y ");
        sb.Append($"-i \"{videoPath}\" ");

        string audioEncoder = config.AudioCodec switch
        {
            AudioCodecType.AAC => "aac -b:a 192k",
            AudioCodecType.MP3 => "mp3_mf -b:a 192k",
            _ => "aac -b:a 192k"
        };

        bool hasSpeaker = !string.IsNullOrEmpty(speakerWav) && File.Exists(speakerWav);
        bool hasMic = !string.IsNullOrEmpty(micWav) && File.Exists(micWav);

        // Track routing matrix flags
        bool spk1 = hasSpeaker && config.SpeakerTrack1;
        bool spk2 = hasSpeaker && config.SpeakerTrack2;
        bool spk3 = hasSpeaker && config.SpeakerTrack3;

        bool mic1 = hasMic && config.MicTrack1;
        bool mic2 = hasMic && config.MicTrack2;
        bool mic3 = hasMic && config.MicTrack3;

        // If no tracks selected at all, fallback to default behavior
        if (!spk1 && !spk2 && !spk3 && !mic1 && !mic2 && !mic3)
        {
            if (hasSpeaker && hasMic)
            {
                spk1 = true; mic1 = true;
                mic2 = true;
                spk3 = true;
            }
            else if (hasSpeaker)
            {
                spk1 = true;
            }
            else if (hasMic)
            {
                mic1 = true;
            }
        }

        int spkCount = (spk1 ? 1 : 0) + (spk2 ? 1 : 0) + (spk3 ? 1 : 0);
        int micCount = (mic1 ? 1 : 0) + (mic2 ? 1 : 0) + (mic3 ? 1 : 0);

        if (spkCount == 0 && micCount == 0)
        {
            sb.Append("-c:v copy ");
            if (config.Format == ContainerFormat.MP4) sb.Append("-movflags +faststart ");
            sb.Append($"\"{outputPath}\"");
            return sb.ToString();
        }

        int currentInputIndex = 1;
        int? spkInputIndex = null;
        int? micInputIndex = null;

        if (spkCount > 0)
        {
            sb.Append($"-i \"{speakerWav}\" ");
            spkInputIndex = currentInputIndex++;
        }

        if (micCount > 0)
        {
            sb.Append($"-i \"{micWav}\" ");
            micInputIndex = currentInputIndex++;
        }

        var filterParts = new List<string>();
        var spkLabels = new List<string>();
        var micLabels = new List<string>();

        // 1. Pre-process Speaker Audio (Sync Offset + Gain)
        string spkSourceLabel = spkInputIndex.HasValue ? $"[{spkInputIndex.Value}:a]" : string.Empty;
        if (spkInputIndex.HasValue)
        {
            var spkFilters = new List<string>();
            if (config.SpeakerSyncOffsetMs > 0)
            {
                spkFilters.Add($"adelay={config.SpeakerSyncOffsetMs}|{config.SpeakerSyncOffsetMs}");
            }
            else if (config.SpeakerSyncOffsetMs < 0)
            {
                double trimSec = -config.SpeakerSyncOffsetMs / 1000.0;
                spkFilters.Add($"atrim=start={trimSec:0.###},asetpts=PTS-STARTPTS");
            }

            if (Math.Abs(config.SpeakerGainDb) >= 0.1)
            {
                spkFilters.Add($"volume={config.SpeakerGainDb:0.#}dB");
            }

            if (spkFilters.Count > 0)
            {
                string spkDspLabel = "spk_dsp";
                filterParts.Add($"{spkSourceLabel}{string.Join(",", spkFilters)}[{spkDspLabel}]");
                spkSourceLabel = $"[{spkDspLabel}]";
            }
        }

        // 2. Pre-process Microphone Audio (Studio DSP Chain: High-Pass -> Denoise -> Gain -> Noise Gate -> Limiter -> Sync Offset)
        string micSourceLabel = micInputIndex.HasValue ? $"[{micInputIndex.Value}:a]" : string.Empty;
        if (micInputIndex.HasValue)
        {
            var micFilters = new List<string>();

            // Step 1: High-Pass Filter (80Hz) to cut sub-bass rumble & desk vibrations
            if (config.MicHighPassFilter)
            {
                micFilters.Add("highpass=f=80");
            }

            // Step 2: Studio Noise Suppression (Spectral Subtraction / AI Speech Denoise)
            if (config.MicNoiseSuppression)
            {
                micFilters.Add("afftdn=nr=12:nf=-32:tn=1");
            }

            // Step 3: Gain Amplification (-30 dB to +30 dB)
            if (Math.Abs(config.MicGainDb) >= 0.1)
            {
                micFilters.Add($"volume={config.MicGainDb:0.#}dB");
            }

            // Step 4: Noise Gate
            if (config.MicNoiseGate)
            {
                micFilters.Add($"agate=threshold={config.MicNoiseGateThresholdDb:0.#}dB:range=0.01:ratio=10:attack=20:release=250");
            }

            // Step 5: Limiter to prevent clipping
            micFilters.Add("alimiter=limit=0.98");

            // Step 6: Sync Offset Delay
            if (config.MicSyncOffsetMs > 0)
            {
                micFilters.Add($"adelay={config.MicSyncOffsetMs}|{config.MicSyncOffsetMs}");
            }
            else if (config.MicSyncOffsetMs < 0)
            {
                double trimSec = -config.MicSyncOffsetMs / 1000.0;
                micFilters.Add($"atrim=start={trimSec:0.###},asetpts=PTS-STARTPTS");
            }

            if (micFilters.Count > 0)
            {
                string micDspLabel = "mic_dsp";
                filterParts.Add($"{micSourceLabel}{string.Join(",", micFilters)}[{micDspLabel}]");
                micSourceLabel = $"[{micDspLabel}]";
            }
        }

        // Generate split filters for speaker if needed
        if (spkInputIndex.HasValue)
        {
            if (spkCount == 1)
            {
                string lbl = "spk_s0";
                filterParts.Add($"{spkSourceLabel}anull[{lbl}]");
                spkLabels.Add(lbl);
            }
            else
            {
                var lbls = new List<string>();
                for (int i = 0; i < spkCount; i++) lbls.Add($"spk_s{i}");
                filterParts.Add($"{spkSourceLabel}asplit={spkCount}{string.Concat(lbls.ConvertAll(l => $"[{l}]"))}");
                spkLabels.AddRange(lbls);
            }
        }

        // Generate split filters for mic if needed
        if (micInputIndex.HasValue)
        {
            if (micCount == 1)
            {
                string lbl = "mic_s0";
                filterParts.Add($"{micSourceLabel}anull[{lbl}]");
                micLabels.Add(lbl);
            }
            else
            {
                var lbls = new List<string>();
                for (int i = 0; i < micCount; i++) lbls.Add($"mic_s{i}");
                filterParts.Add($"{micSourceLabel}asplit={micCount}{string.Concat(lbls.ConvertAll(l => $"[{l}]"))}");
                micLabels.AddRange(lbls);
            }
        }

        int spkUsageIdx = 0;
        int micUsageIdx = 0;
        var outputTracks = new List<(string mapLabel, string title)>();

        // Track 1
        if (spk1 && mic1)
        {
            string outLbl = "aout1";
            filterParts.Add($"[{spkLabels[spkUsageIdx++]}][{micLabels[micUsageIdx++]}]amix=inputs=2:duration=first:dropout_transition=2[{outLbl}]");
            outputTracks.Add((outLbl, "Track 1: Mix (Speaker + Mic)"));
        }
        else if (spk1)
        {
            outputTracks.Add((spkLabels[spkUsageIdx++], "Track 1: System Audio"));
        }
        else if (mic1)
        {
            outputTracks.Add((micLabels[micUsageIdx++], "Track 1: Microphone"));
        }

        // Track 2
        if (spk2 && mic2)
        {
            string outLbl = "aout2";
            filterParts.Add($"[{spkLabels[spkUsageIdx++]}][{micLabels[micUsageIdx++]}]amix=inputs=2:duration=first:dropout_transition=2[{outLbl}]");
            outputTracks.Add((outLbl, "Track 2: Mix (Speaker + Mic)"));
        }
        else if (spk2)
        {
            outputTracks.Add((spkLabels[spkUsageIdx++], "Track 2: System Audio"));
        }
        else if (mic2)
        {
            outputTracks.Add((micLabels[micUsageIdx++], "Track 2: Microphone"));
        }

        // Track 3
        if (spk3 && mic3)
        {
            string outLbl = "aout3";
            filterParts.Add($"[{spkLabels[spkUsageIdx++]}][{micLabels[micUsageIdx++]}]amix=inputs=2:duration=first:dropout_transition=2[{outLbl}]");
            outputTracks.Add((outLbl, "Track 3: Mix (Speaker + Mic)"));
        }
        else if (spk3)
        {
            outputTracks.Add((spkLabels[spkUsageIdx++], "Track 3: System Audio"));
        }
        else if (mic3)
        {
            outputTracks.Add((micLabels[micUsageIdx++], "Track 3: Microphone"));
        }

        if (filterParts.Count > 0)
        {
            sb.Append($"-filter_complex \"{string.Join("; ", filterParts)}\" ");
        }

        sb.Append("-map 0:v -c:v copy ");

        for (int i = 0; i < outputTracks.Count; i++)
        {
            sb.Append($"-map \"[{outputTracks[i].mapLabel}]\" ");
            sb.Append($"-c:a:{i} {audioEncoder} -metadata:s:a:{i} title=\"{outputTracks[i].title}\" ");
        }

        if (config.Format == ContainerFormat.MP4)
        {
            sb.Append("-movflags +faststart ");
        }

        sb.Append($"\"{outputPath}\"");
        return sb.ToString();
    }

    public string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, AudioCodecType audioCodec, ContainerFormat format, AudioTrackMode audioTrackMode = AudioTrackMode.MixToSingleTrack)
    {
        var cfg = new RecordingConfig
        {
            AudioCodec = audioCodec,
            Format = format,
            SpeakerTrack1 = true,
            MicTrack1 = true,
            SpeakerTrack2 = false,
            MicTrack2 = audioTrackMode == AudioTrackMode.SeparateTracks,
            SpeakerTrack3 = audioTrackMode == AudioTrackMode.SeparateTracks,
            MicTrack3 = false
        };
        return BuildMuxArguments(videoPath, speakerWav, micWav, outputPath, cfg);
    }
}
