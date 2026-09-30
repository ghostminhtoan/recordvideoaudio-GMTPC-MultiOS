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
    string BuildArguments(RecordingConfig config, string outputPath);
    string GetOutputExtension(ContainerFormat format);
    string GenerateDefaultFileName(ContainerFormat format);
    string? FindFFmpegExecutable();
    string? GetDetectedAudioDevice();
}

public class FFmpegPipelineService : IEncoderPipelineService
{
    private static string? _cachedAudioDevice = null;
    private static bool _audioDeviceQueried = false;

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
        // 1. Check in application base directory
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string directPath = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(directPath)) return directPath;

        // 2. Check in dist\windows directory
        string distWin = Path.Combine(baseDir, "..", "..", "..", "..", "dist", "windows", "ffmpeg.exe");
        if (File.Exists(distWin)) return Path.GetFullPath(distWin);

        // 3. Check AppData local vibe / installed paths
        string userLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string vibePath = Path.Combine(userLocal, "vibe", "ffmpeg.exe");
        if (File.Exists(vibePath)) return vibePath;

        // 4. Check PATH environment
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

    public string? GetDetectedAudioDevice()
    {
        if (_audioDeviceQueried) return _cachedAudioDevice;
        _audioDeviceQueried = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        string? ffmpeg = FindFFmpegExecutable();
        if (string.IsNullOrEmpty(ffmpeg)) return null;

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

                var match = Regex.Match(stderr, "\"([^\"]+)\"\\s*\\(audio\\)");
                if (match.Success)
                {
                    _cachedAudioDevice = match.Groups[1].Value;
                }
            }
        }
        catch { }

        return _cachedAudioDevice;
    }

    public string BuildCommandLine(RecordingConfig config, string outputPath)
    {
        return $"ffmpeg {BuildArguments(config, outputPath)}";
    }

    public string BuildArguments(RecordingConfig config, string outputPath)
    {
        var sb = new StringBuilder();
        sb.Append("-y ");

        string? audioDevice = (config.RecordMicrophone || config.RecordSystemAudio) ? GetDetectedAudioDevice() : null;

        // 1. Capture Inputs
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sb.Append($"-f gdigrab -framerate {config.Fps} -draw_mouse 1 -i desktop ");

            // Include real audio device if detected
            if (audioDevice != null && (config.RecordMicrophone || config.RecordSystemAudio))
            {
                sb.Append($"-f dshow -i audio=\"{audioDevice}\" ");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            sb.Append($"-f x11grab -framerate {config.Fps} -draw_mouse 1 -i :0.0 ");
            if (config.RecordSystemAudio || config.RecordMicrophone)
            {
                sb.Append("-f pulse -i default ");
            }
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
                _ => "h264_nvenc" // Default to NVENC on modern Windows or fallback
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

        // 5. Audio Codec (only if audio input is present)
        if (audioDevice != null && (config.RecordMicrophone || config.RecordSystemAudio))
        {
            string audioEncoder = config.AudioCodec switch
            {
                AudioCodecType.AAC => "aac -b:a 192k",
                AudioCodecType.MP3 => "mp3_mf -b:a 192k",
                _ => "aac -b:a 192k"
            };
            sb.Append($"-c:a {audioEncoder} ");
        }

        // 6. Container Format Flags
        if (config.Format == ContainerFormat.MP4)
        {
            sb.Append("-movflags +faststart ");
        }

        // 7. Output Destination
        sb.Append($"\"{outputPath}\"");

        return sb.ToString();
    }
}
