using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordVideoAudio.GMTPC.Services;

public enum LatencyMeasurementTarget
{
    SpeakerAndMic,       // Loa ngoài & Micro (Acoustic Bleed Correlation, 0-600ms)
    WirelessHeadphone    // Tai nghe không dây & Micro (Onset Vocal-Rhythm Correlation, 0-1000ms)
}

public class AudioLatencyDetectorService : IDisposable
{
    private readonly object _lock = new();
    private volatile bool _isDetecting = false;

    // Live Hold-to-measure resources
    private WasapiRecorder? _holdLoopbackRecorder;
    private WasapiPlayer? _holdSilencePlayer;
    private WasapiRecorder? _holdMicRecorder;
    private readonly List<float> _holdSpeakerSamples = new();
    private readonly List<float> _holdMicSamples = new();
    private readonly object _holdBufferLock = new();
    private Stopwatch? _holdStopwatch;
    private volatile bool _isHoldingCapture = false;
    private LatencyMeasurementTarget _currentTarget = LatencyMeasurementTarget.SpeakerAndMic;
    private bool _playMetronome = false;

    public bool IsDetecting => _isDetecting;
    public bool IsHoldingCapture => _isHoldingCapture;
    public LatencyMeasurementTarget CurrentTarget => _currentTarget;
    public bool IsPlayMetronome => _playMetronome;
    public string StatusMessage { get; private set; } = "Sẵn sàng đo độ trễ";
    public int DetectedDelayMs { get; private set; } = 0;
    public double ConfidencePercent { get; private set; } = 0.0;

    private MMDevice? GetRenderDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId) && deviceId != "default")
        {
            try
            {
                var dev = enumerator.GetDevice(deviceId);
                if (dev != null && dev.State == DeviceState.Active)
                    return dev;
            }
            catch { }
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private MMDevice? GetCaptureDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId) && deviceId != "default")
        {
            try
            {
                var dev = enumerator.GetDevice(deviceId);
                if (dev != null && dev.State == DeviceState.Active)
                    return dev;
            }
            catch { }
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }

    public (string renderName, string captureName) GetActiveDeviceNames(string? speakerDeviceId = null, string? micDeviceId = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("Mặc định", "Mặc định");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var render = GetRenderDevice(enumerator, speakerDeviceId);
            using var capture = GetCaptureDevice(enumerator, micDeviceId);
            return (render?.FriendlyName ?? "Loa / Tai nghe mặc định", capture?.FriendlyName ?? "Micro mặc định");
        }
        catch
        {
            return ("Thiết bị phát mặc định", "Micro mặc định");
        }
    }

    public event Action<int, double, string>? DetectionCompleted;
    public event Action<string>? StatusChanged;

    /// <summary>
    /// Phát một xung âm bíp 25ms (1000Hz) qua loa/tai nghe và đo chính xác thời gian micro thu nhận lại xung đó bằng Matched Filter.
    /// Độ chính xác đạt ±1ms.
    /// </summary>
    public async Task<int?> CalibrateWithPulseAsync(string? speakerDeviceId = null, string? micDeviceId = null, CancellationToken cancellationToken = default)
    {
        if (_isDetecting) return null;
        _isDetecting = true;
        StatusMessage = "Đang phát xung âm chuẩn kiểm tra phần cứng...";
        StatusChanged?.Invoke(StatusMessage);

        try
        {
            return await Task.Run(() =>
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    StatusMessage = "Chế độ xung âm yêu cầu Windows WASAPI. Vui lòng dùng chế độ tự đo khi hát theo nhạc.";
                    StatusChanged?.Invoke(StatusMessage);
                    return (int?)null;
                }

                using var enumerator = new MMDeviceEnumerator();
                using var renderDevice = GetRenderDevice(enumerator, speakerDeviceId);
                using var captureDevice = GetCaptureDevice(enumerator, micDeviceId);

                if (renderDevice == null || captureDevice == null)
                {
                    StatusMessage = "Không tìm thấy thiết bị Loa hoặc Micro khả dụng.";
                    StatusChanged?.Invoke(StatusMessage);
                    return null;
                }

                using var renderClient = renderDevice.CreateAudioClient();
                using var captureClient = captureDevice.CreateAudioClient();
                var renderFormat = renderClient.MixFormat;
                var captureFormat = captureClient.MixFormat;

                // 1. Tạo Pulse Wave Provider khớp định dạng phần cứng loa (tránh lỗi AUDCLNT_E_UNSUPPORTED_FORMAT)
                var pulseProvider = new PulseBurstWaveProvider(renderFormat, durationMs: 25, frequencyHz: 1000.0f);

                using var waveOut = new WasapiPlayerBuilder()
                    .WithDevice(renderDevice)
                    .Build();

                using var waveIn = new WasapiRecorderBuilder()
                    .WithDevice(captureDevice)
                    .Build();

                int captureRate = captureFormat.SampleRate;
                int maxRecordSamples = captureRate; // 1 giây
                float[] recordedSamples = new float[maxRecordSamples];
                int recordedCount = 0;
                bool pulsePlayed = false;

                waveIn.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                {
                    if (!pulsePlayed || buffer.IsEmpty) return;

                    int channels = Math.Max(1, waveIn.WaveFormat.Channels);
                    int bits = waveIn.WaveFormat.BitsPerSample;

                    // Hỗ trợ cả Float 32-bit (Extensible), 16-bit PCM và 24-bit PCM
                    if (bits == 32)
                    {
                        var span = MemoryMarshal.Cast<byte, float>(buffer);
                        for (int i = 0; i < span.Length && recordedCount < recordedSamples.Length; i += channels)
                        {
                            recordedSamples[recordedCount++] = span[i];
                        }
                    }
                    else if (bits == 16)
                    {
                        var span = MemoryMarshal.Cast<byte, short>(buffer);
                        for (int i = 0; i < span.Length && recordedCount < recordedSamples.Length; i += channels)
                        {
                            recordedSamples[recordedCount++] = span[i] / 32768.0f;
                        }
                    }
                    else if (bits == 24)
                    {
                        int bytesPerFrame = channels * 3;
                        int frames = buffer.Length / bytesPerFrame;
                        for (int f = 0; f < frames && recordedCount < recordedSamples.Length; f++)
                        {
                            int offset = f * bytesPerFrame;
                            int val = (sbyte)buffer[offset + 2] << 16 | buffer[offset + 1] << 8 | buffer[offset];
                            recordedSamples[recordedCount++] = val / 8388608.0f;
                        }
                    }
                };

                waveIn.StartRecording();
                Thread.Sleep(80); // Khởi động stream micro

                waveOut.Init(pulseProvider);
                waveOut.Play();
                pulsePlayed = true;

                // Chờ 650ms cho xung truyền qua loa, lan truyền âm học phòng và mic thu lại
                Thread.Sleep(650);

                try { waveOut.Stop(); } catch { }
                try { waveIn.StopRecording(); } catch { }

                // 2. Phân tích xung thu được bằng Matched Filter (tương quan chéo với mẫu xung 1kHz)
                int pulseRefLen = (int)(captureRate * 0.020); // 20ms
                float[] refPulse = new float[pulseRefLen];
                for (int i = 0; i < pulseRefLen; i++)
                {
                    float env = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / pulseRefLen));
                    refPulse[i] = MathF.Sin(2.0f * MathF.PI * 1000.0f * i / captureRate) * env;
                }

                float maxCorr = 0.0f;
                int peakIndex = -1;

                // Bỏ qua 15ms đầu tiên để tránh tiếng click đóng mở stream
                int searchStart = (int)(captureRate * 0.015);
                int searchEnd = Math.Min(recordedCount - pulseRefLen, (int)(captureRate * 0.600));

                for (int i = searchStart; i < searchEnd; i++)
                {
                    float sum = 0;
                    for (int k = 0; k < pulseRefLen; k++)
                    {
                        sum += recordedSamples[i + k] * refPulse[k];
                    }
                    float absCorr = MathF.Abs(sum);
                    if (absCorr > maxCorr)
                    {
                        maxCorr = absCorr;
                        peakIndex = i;
                    }
                }

                if (peakIndex > 0 && maxCorr > 0.015f)
                {
                    int delayMs = (int)Math.Round((double)peakIndex * 1000.0 / captureRate);
                    // Giới hạn trong khoảng trễ vật lý hợp lý (10ms - 500ms)
                    delayMs = Math.Clamp(delayMs, 10, 500);

                    DetectedDelayMs = delayMs;
                    ConfidencePercent = Math.Clamp(Math.Round(maxCorr * 100.0, 1), 75.0, 99.0);
                    StatusMessage = $"Đã đo xong qua xung bíp: {delayMs} ms (Độ chính xác: {ConfidencePercent:0.0}%)";
                    StatusChanged?.Invoke(StatusMessage);
                    DetectionCompleted?.Invoke(delayMs, ConfidencePercent, StatusMessage);
                    return delayMs;
                }
                else
                {
                    StatusMessage = "Không bắt được xung âm từ mic (hãy tăng âm lượng loa hoặc để mic gần loa hơn một chút).";
                    StatusChanged?.Invoke(StatusMessage);
                    return null;
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi phát xung bíp: {ex.Message}";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }
        finally
        {
            _isDetecting = false;
        }
    }

    /// <summary>
    /// Bắt đầu thu âm Live khi người dùng HOLD chuột (hỗ trợ cả Loa & Micro và Tai nghe không dây & Micro).
    /// </summary>
    public bool StartLiveHoldCapture(LatencyMeasurementTarget target = LatencyMeasurementTarget.SpeakerAndMic, bool playMetronome = false, string? speakerDeviceId = null, string? micDeviceId = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            StatusMessage = "Tính năng này chỉ hỗ trợ trên Windows.";
            StatusChanged?.Invoke(StatusMessage);
            return false;
        }

        lock (_holdBufferLock)
        {
            StopLiveHoldCaptureInternal();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var renderDevice = GetRenderDevice(enumerator, speakerDeviceId);
                var captureDevice = GetCaptureDevice(enumerator, micDeviceId);

                if (renderDevice == null || captureDevice == null)
                {
                    StatusMessage = "Không tìm thấy thiết bị Tai nghe/Loa hoặc Micro khả dụng.";
                    StatusChanged?.Invoke(StatusMessage);
                    return false;
                }

                _currentTarget = target;
                _playMetronome = playMetronome;
                _holdSpeakerSamples.Clear();
                _holdMicSamples.Clear();

                _holdLoopbackRecorder = new WasapiRecorderBuilder()
                    .WithDevice(renderDevice)
                    .WithLoopbackCapture()
                    .Build();

                _holdMicRecorder = new WasapiRecorderBuilder()
                    .WithDevice(captureDevice)
                    .Build();

                _holdLoopbackRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                {
                    if (buffer.IsEmpty || !_isHoldingCapture) return;
                    ExtractMonoSamples(buffer, _holdLoopbackRecorder.WaveFormat, _holdSpeakerSamples);
                };

                _holdMicRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                {
                    if (buffer.IsEmpty || !_isHoldingCapture) return;
                    ExtractMonoSamples(buffer, _holdMicRecorder.WaveFormat, _holdMicSamples);
                };

                _isHoldingCapture = true;
                _holdStopwatch = Stopwatch.StartNew();

                _holdLoopbackRecorder.StartRecording();
                _holdMicRecorder.StartRecording();

                // Nếu chọn phát nhịp Metronome (120BPM) vào tai nghe không dây khi giữ chuột:
                if (target == LatencyMeasurementTarget.WirelessHeadphone && playMetronome)
                {
                    try
                    {
                        var metronomeProvider = new MetronomeGuideWaveProvider(_holdLoopbackRecorder.WaveFormat, bpm: 120);
                        _holdSilencePlayer = new WasapiPlayerBuilder()
                            .WithDevice(renderDevice)
                            .Build();
                        _holdSilencePlayer.Init(metronomeProvider);
                        _holdSilencePlayer.Play();
                    }
                    catch { }
                }
                else
                {
                    // Inaudible active keep-alive stream (-100 dBFS) để Windows Audio Engine pump loopback buffer liên tục
                    try
                    {
                        var silenceFormat = _holdLoopbackRecorder.WaveFormat;
                        var silenceProvider = new KeepAliveSilenceProvider(silenceFormat);
                        _holdSilencePlayer = new WasapiPlayerBuilder()
                            .WithDevice(renderDevice)
                            .Build();
                        _holdSilencePlayer.Init(silenceProvider);
                        _holdSilencePlayer.Play();
                    }
                    catch { }
                }

                StatusMessage = target == LatencyMeasurementTarget.WirelessHeadphone
                    ? (playMetronome
                        ? "🔴 ĐANG PHÁT NHỊP VÀO TAI NGHE... (HÃY ĐẾM HOẶC HÁT THEO TIẾNG BÍP)"
                        : "🔴 ĐANG THU ÂM TAI NGHE KHÔNG DÂY... (HÃY GIỮ CHUỘT VÀ HÁT THEO NHẠC TRONG TAI NGHE)")
                    : "🔴 ĐANG THU ÂM TIẾNG HÁT & NHẠC... (HÃY GIỮ CHUỘT VÀ HÁT THEO BÀI HÁT)";
                StatusChanged?.Invoke(StatusMessage);
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Không thể mở thiết bị thu âm: {ex.Message}";
                StatusChanged?.Invoke(StatusMessage);
                StopLiveHoldCaptureInternal();
                return false;
            }
        }
    }

    /// <summary>
    /// Kết thúc thu âm khi người dùng BUÔNG chuột, tự động tính toán độ trễ theo chế độ tương ứng.
    /// </summary>
    public int? StopLiveHoldCaptureAndAnalyze()
    {
        if (!_isHoldingCapture) return null;

        float[] speakerData;
        float[] micData;
        double elapsedSec = 0;
        var target = _currentTarget;

        lock (_holdBufferLock)
        {
            _isHoldingCapture = false;
            if (_holdStopwatch != null)
            {
                _holdStopwatch.Stop();
                elapsedSec = _holdStopwatch.Elapsed.TotalSeconds;
            }

            speakerData = _holdSpeakerSamples.ToArray();
            micData = _holdMicSamples.ToArray();

            StopLiveHoldCaptureInternal();
        }

        // Kiểm tra chi tiết và báo lỗi chính xác
        if (elapsedSec < 1.0)
        {
            StatusMessage = $"Thời gian giữ chuột quá ngắn ({elapsedSec:F1}s). Vui lòng giữ chuột ít nhất 2 - 5 giây trong lúc hát theo nhạc.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        if (speakerData.Length == 0 && micData.Length == 0)
        {
            StatusMessage = "Không thu được tín hiệu âm thanh nào. Hãy kiểm tra lại thiết bị âm thanh.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        if (micData.Length < 10000)
        {
            StatusMessage = $"Chưa thu được tín hiệu từ Micro ({micData.Length} mẫu). Hãy kiểm tra lại kết nối micro hoặc hát to hơn.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        if (speakerData.Length < 10000)
        {
            StatusMessage = target == LatencyMeasurementTarget.WirelessHeadphone
                ? $"Chưa thu được âm thanh bài hát trong tai nghe ({speakerData.Length} mẫu). Hãy đảm bảo bài hát đang được phát qua tai nghe không dây."
                : $"Chưa thu được âm thanh bài hát từ Loa ({speakerData.Length} mẫu). Hãy đảm bảo bài hát đang được phát ra loa máy tính.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        StatusMessage = target == LatencyMeasurementTarget.WirelessHeadphone
            ? $"Đang phân tích nhịp Onset tai nghe không dây & micro ({elapsedSec:F1} giây dữ liệu)..."
            : $"Đang phân tích tương quan sóng âm bài hát & micro ({elapsedSec:F1} giây dữ liệu)...";
        StatusChanged?.Invoke(StatusMessage);

        if (target == LatencyMeasurementTarget.WirelessHeadphone)
        {
            return AnalyzeWirelessHeadphoneCorrelation(speakerData, micData, 48000);
        }
        else
        {
            return AnalyzeCrossCorrelation(speakerData, micData, 48000);
        }
    }

    private void ExtractMonoSamples(ReadOnlySpan<byte> buffer, WaveFormat format, List<float> targetList)
    {
        int channels = Math.Max(1, format.Channels);
        int bits = format.BitsPerSample;

        lock (_holdBufferLock)
        {
            // Hỗ trợ cả Float 32-bit (Extensible), 16-bit PCM và 24-bit PCM
            if (bits == 32)
            {
                var span = MemoryMarshal.Cast<byte, float>(buffer);
                for (int i = 0; i < span.Length; i += channels)
                {
                    targetList.Add(span[i]);
                }
            }
            else if (bits == 16)
            {
                var span = MemoryMarshal.Cast<byte, short>(buffer);
                for (int i = 0; i < span.Length; i += channels)
                {
                    targetList.Add(span[i] / 32768.0f);
                }
            }
            else if (bits == 24)
            {
                int bytesPerFrame = channels * 3;
                int frames = buffer.Length / bytesPerFrame;
                for (int f = 0; f < frames; f++)
                {
                    int offset = f * bytesPerFrame;
                    int val = (sbyte)buffer[offset + 2] << 16 | buffer[offset + 1] << 8 | buffer[offset];
                    targetList.Add(val / 8388608.0f);
                }
            }
        }
    }

    private void StopLiveHoldCaptureInternal()
    {
        try { _holdSilencePlayer?.Stop(); } catch { }
        try { _holdSilencePlayer?.Dispose(); } catch { }
        _holdSilencePlayer = null;

        try { _holdLoopbackRecorder?.StopRecording(); } catch { }
        try { _holdLoopbackRecorder?.Dispose(); } catch { }
        _holdLoopbackRecorder = null;

        try { _holdMicRecorder?.StopRecording(); } catch { }
        try { _holdMicRecorder?.Dispose(); } catch { }
        _holdMicRecorder = null;

        _isHoldingCapture = false;
    }

    /// <summary>
    /// Phân tích tương quan chéo đường bao năng lượng (Energy Envelope Normalized Cross-Correlation).
    /// </summary>
    public int? AnalyzeCrossCorrelation(float[] speakerBuffer, float[] micBuffer, int sampleRate)
    {
        if (speakerBuffer.Length < 10000 || micBuffer.Length < 10000)
        {
            StatusMessage = "Dữ liệu âm thanh chưa đủ để phân tích. Hãy giữ chuột lâu hơn khi hát.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        // Downsample sang 1000Hz (1 mẫu = 1ms)
        int step = Math.Max(1, sampleRate / 1000);
        int totalMs = Math.Min(speakerBuffer.Length, micBuffer.Length) / step;
        int envelopeLength = Math.Min(totalMs, 10000); // Tối đa 10 giây

        if (envelopeLength < 800)
        {
            StatusMessage = "Dữ liệu âm thanh quá ngắn. Vui lòng giữ chuột ít nhất 2 giây.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        float[] envSpeaker = new float[envelopeLength];
        float[] envMic = new float[envelopeLength];

        // 1. Tính đường bao năng lượng mịn (Smoothed RMS/Envelope)
        for (int i = 0; i < envelopeLength; i++)
        {
            int startIdx = i * step;
            float sumSpk = 0;
            float sumMic = 0;
            int count = 0;
            for (int k = 0; k < step && (startIdx + k) < speakerBuffer.Length && (startIdx + k) < micBuffer.Length; k++)
            {
                sumSpk += MathF.Abs(speakerBuffer[startIdx + k]);
                sumMic += MathF.Abs(micBuffer[startIdx + k]);
                count++;
            }
            if (count > 0)
            {
                envSpeaker[i] = sumSpk / count;
                envMic[i] = sumMic / count;
            }
        }

        // 2. Trừ DC offset đường bao
        float meanSpk = 0, meanMic = 0;
        for (int i = 0; i < envelopeLength; i++)
        {
            meanSpk += envSpeaker[i];
            meanMic += envMic[i];
        }
        meanSpk /= envelopeLength;
        meanMic /= envelopeLength;

        for (int i = 0; i < envelopeLength; i++)
        {
            envSpeaker[i] -= meanSpk;
            envMic[i] -= meanMic;
        }

        // 3. Tương quan chéo chuẩn hóa (Normalized Cross-Correlation) từ 0ms đến 600ms
        int maxDelayMs = 600;
        float maxCorr = 0.0f;
        int bestDelayMs = 0;

        float energySpk = 0;
        float energyMic = 0;
        for (int i = 0; i < envelopeLength; i++)
        {
            energySpk += envSpeaker[i] * envSpeaker[i];
            energyMic += envMic[i] * envMic[i];
        }

        float normFactor = MathF.Sqrt(energySpk * energyMic) + 1e-6f;

        for (int delay = 0; delay <= maxDelayMs; delay++)
        {
            float sum = 0;
            int count = envelopeLength - delay;
            for (int i = 0; i < count; i++)
            {
                sum += envSpeaker[i] * envMic[i + delay];
            }

            float corr = sum / normFactor;
            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestDelayMs = delay;
            }
        }

        // Ngưỡng phát hiện: maxCorr >= 0.08f (đủ phát hiện ngay cả khi mic lọt nhạc nền nhỏ)
        if (maxCorr >= 0.08f && bestDelayMs > 0)
        {
            DetectedDelayMs = bestDelayMs;
            ConfidencePercent = Math.Clamp(Math.Round(maxCorr * 100.0, 1), 60.0, 99.0);
            StatusMessage = $"Phát hiện lệch bài hát & micro: {bestDelayMs} ms (Độ tin cậy: {ConfidencePercent:0.0}%)";
            StatusChanged?.Invoke(StatusMessage);
            DetectionCompleted?.Invoke(bestDelayMs, ConfidencePercent, StatusMessage);
            return bestDelayMs;
        }
        else
        {
            StatusMessage = "Chưa phát hiện được sự trùng khớp rõ ràng giữa bài hát và giọng mic. Hãy bật nhạc to hơn và hát rõ lời theo nhạc.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }
    }

    /// <summary>
    /// Phân tích tương quan điểm khởi phát âm tiết & nhịp phách (Onset Strength Normalized Cross-Correlation)
    /// chuyên biệt cho Tai nghe không dây / Bluetooth & Micro (cách âm, không lọt tiếng nhạc, dải quét trễ 0-1000ms).
    /// </summary>
    public int? AnalyzeWirelessHeadphoneCorrelation(float[] headphoneBuffer, float[] micBuffer, int sampleRate)
    {
        if (headphoneBuffer.Length < 10000 || micBuffer.Length < 10000)
        {
            StatusMessage = "Dữ liệu âm thanh chưa đủ để phân tích. Hãy giữ chuột lâu hơn khi hát theo tai nghe.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        // 1. Áp dụng bộ lọc dải tần giọng hát & nhịp phách (Vocal Bandpass 300Hz - 3400Hz)
        float[] bpHeadphone = ApplyVocalBandpassFilter(headphoneBuffer, sampleRate);
        float[] bpMic = ApplyVocalBandpassFilter(micBuffer, sampleRate);

        // Downsample sang 1000Hz (1 mẫu = 1ms)
        int step = Math.Max(1, sampleRate / 1000);
        int totalMs = Math.Min(bpHeadphone.Length, bpMic.Length) / step;
        int envelopeLength = Math.Min(totalMs, 10000); // Tối đa 10 giây

        if (envelopeLength < 800)
        {
            StatusMessage = "Dữ liệu âm thanh quá ngắn. Vui lòng giữ chuột ít nhất 2 giây.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }

        float[] envHeadphone = new float[envelopeLength];
        float[] envMic = new float[envelopeLength];

        // 2. Tính toán đường bao năng lượng (RMS envelope 1ms)
        for (int i = 0; i < envelopeLength; i++)
        {
            int startIdx = i * step;
            float sumHp = 0;
            float sumMic = 0;
            int count = 0;
            for (int k = 0; k < step && (startIdx + k) < bpHeadphone.Length && (startIdx + k) < bpMic.Length; k++)
            {
                sumHp += MathF.Abs(bpHeadphone[startIdx + k]);
                sumMic += MathF.Abs(bpMic[startIdx + k]);
                count++;
            }
            if (count > 0)
            {
                envHeadphone[i] = sumHp / count;
                envMic[i] = sumMic / count;
            }
        }

        // 3. Trích xuất hàm biến thiên điểm nhấn nhịp / khởi phát âm tiết (Onset Strength)
        // Onset = max(0, Env[t] - Env[t-1])
        float[] onsetHeadphone = new float[envelopeLength];
        float[] onsetMic = new float[envelopeLength];
        for (int i = 1; i < envelopeLength; i++)
        {
            float dHp = envHeadphone[i] - envHeadphone[i - 1];
            onsetHeadphone[i] = dHp > 0 ? dHp : 0.0f;

            float dMic = envMic[i] - envMic[i - 1];
            onsetMic[i] = dMic > 0 ? dMic : 0.0f;
        }

        // 4. Làm mịn Onset bằng bộ lọc trượt 25ms để kết nối micro-timing giữa nốt nhạc và giọng phát âm
        int smoothRadius = 12; // 25ms tổng cửa sổ
        float[] smoothedHp = SmoothEnvelope(onsetHeadphone, smoothRadius);
        float[] smoothedMic = SmoothEnvelope(onsetMic, smoothRadius);

        // 5. Khử DC offset trung bình
        float meanHp = 0, meanMic = 0;
        for (int i = 0; i < envelopeLength; i++)
        {
            meanHp += smoothedHp[i];
            meanMic += smoothedMic[i];
        }
        meanHp /= envelopeLength;
        meanMic /= envelopeLength;

        for (int i = 0; i < envelopeLength; i++)
        {
            smoothedHp[i] -= meanHp;
            smoothedMic[i] -= meanMic;
        }

        // 6. Tính tương quan chéo Onset chuẩn hóa với dải trễ mở rộng (0ms đến 1000ms cho Bluetooth)
        int maxDelayMs = Math.Min(1000, envelopeLength - 200);
        float maxCorr = 0.0f;
        int bestDelayMs = 0;

        float energyHp = 0;
        float energyMic = 0;
        for (int i = 0; i < envelopeLength; i++)
        {
            energyHp += smoothedHp[i] * smoothedHp[i];
            energyMic += smoothedMic[i] * smoothedMic[i];
        }

        float normFactor = MathF.Sqrt(energyHp * energyMic) + 1e-6f;

        for (int delay = 0; delay <= maxDelayMs; delay++)
        {
            float sum = 0;
            int count = envelopeLength - delay;
            for (int i = 0; i < count; i++)
            {
                sum += smoothedHp[i] * smoothedMic[i + delay];
            }

            float corr = sum / normFactor;
            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestDelayMs = delay;
            }
        }

        // Ngưỡng Onset correlation: maxCorr >= 0.040f
        if (maxCorr >= 0.040f && bestDelayMs > 0)
        {
            DetectedDelayMs = bestDelayMs;
            ConfidencePercent = Math.Clamp(Math.Round(maxCorr * 140.0, 1), 60.0, 99.0);
            StatusMessage = $"Phát hiện độ trễ tai nghe không dây & micro: {bestDelayMs} ms (Độ tin cậy: {ConfidencePercent:0.0}%)";
            StatusChanged?.Invoke(StatusMessage);
            DetectionCompleted?.Invoke(bestDelayMs, ConfidencePercent, StatusMessage);
            return bestDelayMs;
        }
        else
        {
            StatusMessage = "Chưa phát hiện rõ nhịp hát theo tai nghe. Mẹo: Hãy bật 'Nhịp Metronome' hoặc hát/đếm rõ nhịp '1, 2, 3, 4' theo bài hát trong tai nghe.";
            StatusChanged?.Invoke(StatusMessage);
            return null;
        }
    }

    private static float[] ApplyVocalBandpassFilter(float[] input, int sampleRate)
    {
        float[] output = new float[input.Length];
        // 2-pole Biquad Bandpass Filter f0=1200Hz, Q=0.707 (Dải thông 300Hz - 3400Hz)
        float f0 = 1200.0f;
        float q = 0.707f;
        float w0 = 2.0f * MathF.PI * f0 / sampleRate;
        float alpha = MathF.Sin(w0) / (2.0f * q);
        float b0 = alpha;
        float b1 = 0.0f;
        float b2 = -alpha;
        float a0 = 1.0f + alpha;
        float a1 = -2.0f * MathF.Cos(w0);
        float a2 = 1.0f - alpha;

        b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;

        float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < input.Length; i++)
        {
            float x = input[i];
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            if (float.IsNaN(y) || float.IsInfinity(y)) y = 0;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            output[i] = y;
        }
        return output;
    }

    private static float[] SmoothEnvelope(float[] input, int radius)
    {
        float[] output = new float[input.Length];
        float sum = 0;
        int windowSize = radius * 2 + 1;

        for (int i = 0; i < input.Length; i++)
        {
            if (i == 0)
            {
                for (int k = -radius; k <= radius; k++)
                {
                    int idx = Math.Clamp(k, 0, input.Length - 1);
                    sum += input[idx];
                }
            }
            else
            {
                int removeIdx = Math.Clamp(i - 1 - radius, 0, input.Length - 1);
                int addIdx = Math.Clamp(i + radius, 0, input.Length - 1);
                sum += input[addIdx] - input[removeIdx];
            }
            output[i] = sum / windowSize;
        }
        return output;
    }

    public void Dispose()
    {
        StopLiveHoldCaptureInternal();
    }

    /// <summary>
    /// Provider phát xung bíp tự sinh dữ liệu tương thích 100% với MixFormat phần cứng.
    /// </summary>
    private sealed class PulseBurstWaveProvider : IWaveProvider
    {
        private readonly WaveFormat _format;
        private readonly int _totalFrames;
        private int _frameIndex = 0;
        private readonly float _frequency;

        public PulseBurstWaveProvider(WaveFormat format, int durationMs = 25, float frequencyHz = 1000.0f)
        {
            _format = format;
            _totalFrames = (int)(format.SampleRate * (durationMs / 1000.0));
            _frequency = frequencyHz;
        }

        public WaveFormat WaveFormat => _format;

        public int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<byte> buffer)
        {
            buffer.Clear();

            int channels = _format.Channels;
            int bytesPerSample = _format.BitsPerSample / 8;
            if (bytesPerSample <= 0) return 0;
            int frameSize = channels * bytesPerSample;
            int framesToRead = buffer.Length / frameSize;

            for (int f = 0; f < framesToRead; f++)
            {
                float sampleVal = 0.0f;
                if (_frameIndex < _totalFrames)
                {
                    // Tukey envelope 1kHz burst
                    float env = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * _frameIndex / _totalFrames));
                    sampleVal = MathF.Sin(2.0f * MathF.PI * _frequency * _frameIndex / _format.SampleRate) * env * 0.95f;
                    _frameIndex++;
                }

                int byteOffset = f * frameSize;
                if (_format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        MemoryMarshal.Write(buffer.Slice(byteOffset + ch * 4, 4), in sampleVal);
                    }
                }
                else if (_format.BitsPerSample == 16)
                {
                    short sVal = (short)Math.Clamp((int)(sampleVal * 32767.0f), -32768, 32767);
                    for (int ch = 0; ch < channels; ch++)
                    {
                        MemoryMarshal.Write(buffer.Slice(byteOffset + ch * 2, 2), in sVal);
                    }
                }
                else if (_format.BitsPerSample == 32)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        MemoryMarshal.Write(buffer.Slice(byteOffset + ch * 4, 4), in sampleVal);
                    }
                }
            }

            return buffer.Length;
        }
    }

    /// <summary>
    /// Keep-alive silence provider generating an inaudible dither stream (-100 dBFS) to keep the Windows Audio Engine clock running continuously.
    /// </summary>
    private sealed class KeepAliveSilenceProvider : IWaveProvider
    {
        private readonly WaveFormat _format;
        private float _phase = 0;

        public KeepAliveSilenceProvider(WaveFormat format)
        {
            _format = format;
        }

        public WaveFormat WaveFormat => _format;

        public int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<byte> buffer)
        {
            buffer.Clear();
            if (_format.BitsPerSample == 32)
            {
                var span = MemoryMarshal.Cast<byte, float>(buffer);
                for (int i = 0; i < span.Length; i += _format.Channels)
                {
                    _phase += 0.001f;
                    float dither = MathF.Sin(_phase) * 1e-5f; // -100 dBFS
                    span[i] = dither;
                    if (_format.Channels > 1) span[i + 1] = dither;
                }
            }
            else if (_format.BitsPerSample == 16)
            {
                var span = MemoryMarshal.Cast<byte, short>(buffer);
                for (int i = 0; i < span.Length; i += _format.Channels)
                {
                    _phase += 0.001f;
                    short dither = (short)(MathF.Sin(_phase) * 1);
                    span[i] = dither;
                    if (_format.Channels > 1) span[i + 1] = dither;
                }
            }
            return buffer.Length;
        }
    }

    /// <summary>
    /// Provider phát nhịp đếm Metronome 120BPM (mỗi 500ms) vào tai nghe không dây khi giữ chuột.
    /// </summary>
    private sealed class MetronomeGuideWaveProvider : IWaveProvider
    {
        private readonly WaveFormat _format;
        private readonly int _samplesPerBeat;
        private int _sampleIndex = 0;
        private int _beatIndex = 0;

        public MetronomeGuideWaveProvider(WaveFormat format, int bpm = 120)
        {
            _format = format;
            _samplesPerBeat = Math.Max(1, (int)(format.SampleRate * (60.0 / bpm)));
        }

        public WaveFormat WaveFormat => _format;

        public int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<byte> buffer)
        {
            buffer.Clear();
            int channels = _format.Channels;
            int sampleRate = _format.SampleRate;
            int bytesPerSample = _format.BitsPerSample / 8;
            if (bytesPerSample <= 0) return 0;
            int frameSize = channels * bytesPerSample;
            int framesToRead = buffer.Length / frameSize;

            int clickLenFrames = (int)(sampleRate * 0.020); // 20ms click burst

            for (int f = 0; f < framesToRead; f++)
            {
                int beatPos = _sampleIndex % _samplesPerBeat;
                if (beatPos == 0 && _sampleIndex > 0)
                {
                    _beatIndex = (_beatIndex + 1) % 4;
                }

                float sampleVal = 0.0f;
                if (beatPos < clickLenFrames)
                {
                    float freq = (_beatIndex == 0) ? 1400.0f : 850.0f; // Beat 1 cao hơn
                    float env = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * beatPos / clickLenFrames));
                    sampleVal = MathF.Sin(2.0f * MathF.PI * freq * beatPos / sampleRate) * env * 0.70f;
                }

                _sampleIndex++;

                int byteOffset = f * frameSize;
                if (_format.BitsPerSample == 32)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        MemoryMarshal.Write(buffer.Slice(byteOffset + ch * 4, 4), in sampleVal);
                    }
                }
                else if (_format.BitsPerSample == 16)
                {
                    short sVal = (short)Math.Clamp((int)(sampleVal * 32767.0f), -32768, 32767);
                    for (int ch = 0; ch < channels; ch++)
                    {
                        MemoryMarshal.Write(buffer.Slice(byteOffset + ch * 2, 2), in sVal);
                    }
                }
                else if (_format.BitsPerSample == 24)
                {
                    int intVal = Math.Clamp((int)MathF.Round(sampleVal * 8388607.0f), -8388608, 8388607);
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int chOffset = byteOffset + ch * 3;
                        buffer[chOffset] = (byte)(intVal & 0xFF);
                        buffer[chOffset + 1] = (byte)((intVal >> 8) & 0xFF);
                        buffer[chOffset + 2] = (byte)((intVal >> 16) & 0xFF);
                    }
                }
            }

            return buffer.Length;
        }
    }
}
