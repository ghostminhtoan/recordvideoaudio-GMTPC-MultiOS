using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public interface IEncoderPipelineService
{
    string BuildCommandLine(RecordingConfig config, string outputPath);
    string GetOutputExtension(ContainerFormat format);
    string GenerateDefaultFileName(ContainerFormat format);
}

public class FFmpegPipelineService : IEncoderPipelineService
{
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

    public string BuildCommandLine(RecordingConfig config, string outputPath)
    {
        var sb = new StringBuilder();
        sb.Append("ffmpeg -y ");

        // 1. Platform-specific Capture Input
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sb.Append($"-f gdigrab -framerate {config.Fps} -i desktop ");
            if (config.RecordSystemAudio)
            {
                sb.Append("-f dshow -i audio=\"virtual-audio-capturer\" ");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            sb.Append($"-f x11grab -framerate {config.Fps} -i :0.0 ");
            if (config.RecordSystemAudio)
            {
                sb.Append("-f pulse -i default ");
            }
        }
        else // Android / Mobile
        {
            sb.Append($"-f android_camera -framerate {config.Fps} -i 0 ");
        }

        // 2. Video Codec Selection & Hardware Acceleration
        string videoEncoder = config.VideoCodec switch
        {
            VideoCodecType.H264 => config.HwAcceleration switch
            {
                HwAccelType.NVENC => "h264_nvenc",
                HwAccelType.QSV => "h264_qsv",
                HwAccelType.AMF => "h264_amf",
                HwAccelType.VAAPI => "h264_vaapi",
                HwAccelType.MediaCodec => "h264_mediacodec",
                _ => "libx264"
            },
            VideoCodecType.HEVC => config.HwAcceleration switch
            {
                HwAccelType.NVENC => "hevc_nvenc",
                HwAccelType.QSV => "hevc_qsv",
                HwAccelType.AMF => "hevc_amf",
                HwAccelType.VAAPI => "hevc_vaapi",
                HwAccelType.MediaCodec => "hevc_mediacodec",
                _ => "libx265"
            },
            _ => "libx264"
        };
        sb.Append($"-c:v {videoEncoder} ");

        // 3. Encoder Preset
        string preset = config.Preset.ToString().ToLowerInvariant();
        sb.Append($"-preset {preset} ");

        // 4. Rate Control Modes (CBR, VBR, CQP, CRF)
        bool isNvenc = videoEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase);
        switch (config.RateControl)
        {
            case RateControlMode.CRF:
                sb.Append($"-crf {config.CrfValue} ");
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

        // 5. Audio Codec
        string audioEncoder = config.AudioCodec switch
        {
            AudioCodecType.AAC => "aac -b:a 192k",
            AudioCodecType.MP3 => "libmp3lame -b:a 192k",
            _ => "aac -b:a 192k"
        };
        sb.Append($"-c:a {audioEncoder} ");

        // 6. Container Format Specific Flags
        if (config.Format == ContainerFormat.MP4)
        {
            sb.Append("-movflags +faststart ");
        }

        // 7. Output Destination
        sb.Append($"\"{outputPath}\"");

        return sb.ToString();
    }
}
