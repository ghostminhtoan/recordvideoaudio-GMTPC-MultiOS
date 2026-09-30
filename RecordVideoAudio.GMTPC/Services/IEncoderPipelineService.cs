using System;
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
    string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, AudioCodecType audioCodec, ContainerFormat format);
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            switch (config.CaptureSource)
            {
                case CaptureSourceType.CustomArea:
                    int w = Math.Max(2, (config.AreaWidth / 2) * 2); // ensure even number
                    int h = Math.Max(2, (config.AreaHeight / 2) * 2);
                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -offset_x {config.AreaX} -offset_y {config.AreaY} -video_size {w}x{h} -i desktop ");
                    break;

                case CaptureSourceType.ActiveWindow:
                    if (!string.IsNullOrWhiteSpace(config.SelectedWindowTitle))
                    {
                        string safeTitle = config.SelectedWindowTitle.Replace("\"", "\\\"");
                        sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -i title=\"{safeTitle}\" ");
                    }
                    else
                    {
                        sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -i desktop ");
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

                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -i desktop ");
                    sb.Append($"-f dshow -i video=\"{webcam}\" ");
                    sb.Append($"-filter_complex \"[1:v]fps={config.Fps},scale={camW}:{camH}[cam];[0:v][cam]overlay={overlayPos}[vout]\" -map \"[vout]\" ");
                    break;

                case CaptureSourceType.FullScreen:
                default:
                    sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -i desktop ");
                    break;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            sb.Append($"-f x11grab -framerate {config.Fps} -draw_mouse 1 -i :0.0 ");
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

    public string BuildMuxArguments(string videoPath, string? speakerWav, string? micWav, string outputPath, AudioCodecType audioCodec, ContainerFormat format)
    {
        var sb = new StringBuilder();
        sb.Append("-y ");
        sb.Append($"-i \"{videoPath}\" ");

        string audioEncoder = audioCodec switch
        {
            AudioCodecType.AAC => "aac -b:a 192k",
            AudioCodecType.MP3 => "mp3_mf -b:a 192k",
            _ => "aac -b:a 192k"
        };

        if (!string.IsNullOrEmpty(speakerWav) && !string.IsNullOrEmpty(micWav))
        {
            sb.Append($"-i \"{speakerWav}\" ");
            sb.Append($"-i \"{micWav}\" ");
            sb.Append("-filter_complex \"[1:a][2:a]amix=inputs=2:duration=first:dropout_transition=2[aout]\" ");
            sb.Append("-map 0:v ");
            sb.Append("-map \"[aout]\" ");
            sb.Append($"-c:v copy -c:a {audioEncoder} ");
        }
        else if (!string.IsNullOrEmpty(speakerWav))
        {
            sb.Append($"-i \"{speakerWav}\" ");
            sb.Append("-map 0:v -map 1:a ");
            sb.Append($"-c:v copy -c:a {audioEncoder} ");
        }
        else if (!string.IsNullOrEmpty(micWav))
        {
            sb.Append($"-i \"{micWav}\" ");
            sb.Append("-map 0:v -map 1:a ");
            sb.Append($"-c:v copy -c:a {audioEncoder} ");
        }
        else
        {
            sb.Append("-c:v copy ");
        }

        if (format == ContainerFormat.MP4)
        {
            sb.Append("-movflags +faststart ");
        }

        sb.Append($"\"{outputPath}\"");
        return sb.ToString();
    }
}
