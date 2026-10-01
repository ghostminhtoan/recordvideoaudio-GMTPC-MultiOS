using System;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordVideoAudio.GMTPC.Services;

public class WasapiAudioRecorder : IDisposable
{
    private WasapiRecorder? _loopbackRecorder;
    private WaveFileWriter? _loopbackWriter;
    private string? _loopbackPath;

    private WasapiPlayer? _silencePlayer;

    private WasapiRecorder? _micRecorder;
    private WaveFileWriter? _micWriter;
    private string? _micPath;

    // Real-time Audio Configuration (Thread-Safe volatile)
    private volatile bool _recordSpeaker = true;
    private volatile float _speakerVolume = 1.0f;
    private volatile float _speakerGainDb = 0.0f;

    private volatile bool _recordMic = true;
    private volatile float _micVolume = 0.9f;
    private volatile float _micGainDb = 0.0f;
    private volatile bool _micNoiseSuppression = true;
    private volatile bool _micNoiseGate = false;
    private volatile float _micNoiseGateThresholdDb = -36.0f;
    private volatile bool _micHighPassFilter = true;

    // Pre-allocated reusable audio processing buffers
    private byte[] _loopbackBuffer = new byte[65536];
    private byte[] _micBuffer = new byte[65536];

    // High-Pass Filter 80Hz IIR Biquad State (per channel)
    private readonly float[] _micBiquadX1 = new float[8];
    private readonly float[] _micBiquadX2 = new float[8];
    private readonly float[] _micBiquadY1 = new float[8];
    private readonly float[] _micBiquadY2 = new float[8];
    private int _lastSampleRate = 0;
    private float _hpB0 = 1.0f, _hpB1 = 0.0f, _hpB2 = 0.0f, _hpA1 = 0.0f, _hpA2 = 0.0f;

    // Adaptive Noise Suppression Floor & Envelope Follower State
    private float _micSignalEnvelope = 0.0f;
    private float _micNoiseFloor = 0.005f; // Initial estimate ~ -46 dB

    // Noise Gate Envelope State
    private float _micGateGain = 1.0f;
    private int _micGateHoldCounter = 0;

    public bool IsRecording { get; private set; }

    public void UpdateRealtimeSettings(
        bool recordSpeaker, double speakerVolume, double speakerGainDb,
        bool recordMic, double micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter)
    {
        _recordSpeaker = recordSpeaker;
        _speakerVolume = (float)Math.Clamp(speakerVolume / 100.0, 0.0, 1.0);
        _speakerGainDb = (float)Math.Clamp(speakerGainDb, -50.0, 50.0);

        _recordMic = recordMic;
        _micVolume = (float)Math.Clamp(micVolume / 100.0, 0.0, 1.0);
        _micGainDb = (float)Math.Clamp(micGainDb, -50.0, 50.0);
        _micNoiseSuppression = micNoiseSuppression;
        _micNoiseGate = micNoiseGate;
        _micNoiseGateThresholdDb = (float)Math.Clamp(micNoiseGateThresholdDb, -80.0, 0.0);
        _micHighPassFilter = micHighPassFilter;
    }

    public string? StartRecording(bool recordSpeaker, float speakerVolume, bool recordMic, float micVolume, string outDir)
    {
        return StartRecording(recordSpeaker, speakerVolume, 0.0, recordMic, micVolume, 0.0, true, false, -36.0, true, outDir);
    }

    public string? StartRecording(
        bool recordSpeaker, float speakerVolume, double speakerGainDb,
        bool recordMic, float micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter,
        string outDir)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        StopRecording();

        UpdateRealtimeSettings(
            recordSpeaker, speakerVolume, speakerGainDb,
            recordMic, micVolume, micGainDb,
            micNoiseSuppression, micNoiseGate, micNoiseGateThresholdDb,
            micHighPassFilter
        );

        ResetDspFilters();

        try
        {
            var enumerator = new MMDeviceEnumerator();

            // 1. System Speaker Loopback Capture
            if (recordSpeaker)
            {
                try
                {
                    var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (renderDevice != null)
                    {
                        _loopbackPath = Path.Combine(outDir, $"temp_speaker_{Guid.NewGuid():N}.wav");

                        _loopbackRecorder = new WasapiRecorderBuilder()
                            .WithDevice(renderDevice)
                            .WithLoopbackCapture()
                            .Build();

                        _loopbackWriter = new WaveFileWriter(_loopbackPath, _loopbackRecorder.WaveFormat);

                        _loopbackRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                        {
                            try
                            {
                                if (_loopbackWriter != null && !buffer.IsEmpty)
                                {
                                    if (_loopbackBuffer.Length < buffer.Length)
                                    {
                                        Array.Resize(ref _loopbackBuffer, Math.Max(_loopbackBuffer.Length * 2, buffer.Length));
                                    }

                                    buffer.CopyTo(_loopbackBuffer);
                                    Span<byte> workSpan = _loopbackBuffer.AsSpan(0, buffer.Length);

                                    var fmt = _loopbackRecorder.WaveFormat;
                                    ProcessSpeakerData(workSpan, fmt.BitsPerSample);

                                    _loopbackWriter.Write(workSpan);
                                }
                            }
                            catch { }
                        };

                        _loopbackRecorder.StartRecording();

                        // Keep Windows Audio Engine active during silence using lightweight SilenceProvider
                        try
                        {
                            var silenceFormat = _loopbackRecorder.WaveFormat;
                            var silenceProvider = new SilenceProvider(silenceFormat);
                            _silencePlayer = new WasapiPlayerBuilder()
                                .WithDevice(renderDevice)
                                .Build();
                            _silencePlayer.Init(silenceProvider);
                            _silencePlayer.Play();
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 2. Microphone Capture
            if (recordMic)
            {
                try
                {
                    var captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                    if (captureDevice != null)
                    {
                        _micPath = Path.Combine(outDir, $"temp_mic_{Guid.NewGuid():N}.wav");

                        _micRecorder = new WasapiRecorderBuilder()
                            .WithDevice(captureDevice)
                            .Build();

                        _micWriter = new WaveFileWriter(_micPath, _micRecorder.WaveFormat);

                        _micRecorder.DataAvailable += (ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition) =>
                        {
                            try
                            {
                                if (_micWriter != null && !buffer.IsEmpty)
                                {
                                    if (_micBuffer.Length < buffer.Length)
                                    {
                                        Array.Resize(ref _micBuffer, Math.Max(_micBuffer.Length * 2, buffer.Length));
                                    }

                                    buffer.CopyTo(_micBuffer);
                                    Span<byte> workSpan = _micBuffer.AsSpan(0, buffer.Length);

                                    var fmt = _micRecorder.WaveFormat;
                                    ProcessMicData(workSpan, fmt.BitsPerSample, fmt.Channels, fmt.SampleRate);

                                    _micWriter.Write(workSpan);
                                }
                            }
                            catch { }
                        };

                        _micRecorder.StartRecording();
                    }
                }
                catch { }
            }

            IsRecording = true;
        }
        catch (Exception ex)
        {
            StopRecording();
            return ex.Message;
        }

        return null;
    }

    private void ResetDspFilters()
    {
        Array.Clear(_micBiquadX1, 0, _micBiquadX1.Length);
        Array.Clear(_micBiquadX2, 0, _micBiquadX2.Length);
        Array.Clear(_micBiquadY1, 0, _micBiquadY1.Length);
        Array.Clear(_micBiquadY2, 0, _micBiquadY2.Length);
        _lastSampleRate = 0;
        _micSignalEnvelope = 0.0f;
        _micNoiseFloor = 0.005f;
        _micGateGain = 1.0f;
        _micGateHoldCounter = 0;
    }

    private void ProcessSpeakerData(Span<byte> data, int bitsPerSample)
    {
        if (!_recordSpeaker || _speakerVolume <= 0.0001f)
        {
            data.Clear();
            return;
        }

        float linearGain = MathF.Pow(10.0f, _speakerGainDb / 20.0f);
        float totalMultiplier = _speakerVolume * linearGain;

        if (bitsPerSample == 32)
        {
            Span<float> samples = MemoryMarshal.Cast<byte, float>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                float val = samples[i] * totalMultiplier;
                if (MathF.Abs(val) > 0.85f)
                {
                    val = MathF.Tanh(val * 0.95f);
                }
                samples[i] = val;
            }
        }
        else if (bitsPerSample == 16)
        {
            Span<short> samples = MemoryMarshal.Cast<byte, short>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                float val = (samples[i] / 32768.0f) * totalMultiplier;
                if (MathF.Abs(val) > 0.85f)
                {
                    val = MathF.Tanh(val * 0.95f);
                }
                samples[i] = (short)Math.Clamp((int)MathF.Round(val * 32767.0f), -32768, 32767);
            }
        }
    }

    private void UpdateHighPassCoefficients(int sampleRate)
    {
        if (_lastSampleRate == sampleRate || sampleRate <= 0) return;
        _lastSampleRate = sampleRate;

        float fc = 80.0f;
        float q = 0.7071f;
        float w0 = 2.0f * MathF.PI * fc / sampleRate;
        float cosW0 = MathF.Cos(w0);
        float alpha = MathF.Sin(w0) / (2.0f * q);
        float a0 = 1.0f + alpha;

        _hpB0 = ((1.0f + cosW0) / 2.0f) / a0;
        _hpB1 = (-(1.0f + cosW0)) / a0;
        _hpB2 = ((1.0f + cosW0) / 2.0f) / a0;
        _hpA1 = (-2.0f * cosW0) / a0;
        _hpA2 = (1.0f - alpha) / a0;
    }

    private float ProcessMicSample(float sample, int ch, int sampleRate)
    {
        float val = sample;

        // 1. High-Pass Filter 80Hz (IIR Biquad) to remove rumble / desk bumps
        if (_micHighPassFilter)
        {
            int channelIndex = ch % _micBiquadX1.Length;
            float y = _hpB0 * val + _hpB1 * _micBiquadX1[channelIndex] + _hpB2 * _micBiquadX2[channelIndex]
                      - _hpA1 * _micBiquadY1[channelIndex] - _hpA2 * _micBiquadY2[channelIndex];

            if (float.IsNaN(y) || float.IsInfinity(y)) y = 0.0f;

            _micBiquadX2[channelIndex] = _micBiquadX1[channelIndex];
            _micBiquadX1[channelIndex] = val;
            _micBiquadY2[channelIndex] = _micBiquadY1[channelIndex];
            _micBiquadY1[channelIndex] = y;

            val = y;
        }

        float absVal = MathF.Abs(val);

        // 2. Adaptive Noise Suppression (Spectral Subtraction / Expander)
        if (_micNoiseSuppression)
        {
            _micSignalEnvelope = MathF.Max(absVal, _micSignalEnvelope * 0.999f);

            // Adapt noise floor estimate slowly during low-energy periods
            if (_micSignalEnvelope < _micNoiseFloor)
            {
                _micNoiseFloor = _micNoiseFloor * 0.99f + _micSignalEnvelope * 0.01f;
            }
            else
            {
                _micNoiseFloor = _micNoiseFloor * 0.99995f + _micSignalEnvelope * 0.00005f;
            }

            _micNoiseFloor = Math.Clamp(_micNoiseFloor, 0.0001f, 0.05f);

            float snr = _micSignalEnvelope / _micNoiseFloor;
            if (snr < 2.5f)
            {
                float atten = Math.Clamp((snr - 1.0f) / 1.5f, 0.10f, 1.0f);
                val *= atten;
            }
        }

        // 3. Noise Gate (Attack/Hold/Release)
        if (_micNoiseGate)
        {
            float gateLinear = MathF.Pow(10.0f, _micNoiseGateThresholdDb / 20.0f);
            if (absVal > gateLinear)
            {
                _micGateGain = MathF.Min(1.0f, _micGateGain + 0.05f); // attack ~ 10ms
                _micGateHoldCounter = (int)(0.08f * sampleRate);       // hold 80ms
            }
            else
            {
                if (_micGateHoldCounter > 0)
                {
                    _micGateHoldCounter--;
                }
                else
                {
                    _micGateGain = MathF.Max(0.0f, _micGateGain - 0.002f); // release ~ 150ms
                }
            }
            val *= _micGateGain;
        }

        // 4. Gain (-50 dB to +50 dB) and Volume
        float micGainLinear = MathF.Pow(10.0f, _micGainDb / 20.0f);
        val *= (_micVolume * micGainLinear);

        // 5. Soft Limiter (chống méo clipping)
        if (MathF.Abs(val) > 0.85f)
        {
            val = MathF.Tanh(val * 0.95f);
        }

        return val;
    }

    private void ProcessMicData(Span<byte> data, int bitsPerSample, int channels, int sampleRate)
    {
        if (!_recordMic || _micVolume <= 0.0001f)
        {
            data.Clear();
            return;
        }

        UpdateHighPassCoefficients(sampleRate);

        if (bitsPerSample == 32)
        {
            Span<float> samples = MemoryMarshal.Cast<byte, float>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                int ch = channels > 1 ? (i % channels) : 0;
                samples[i] = ProcessMicSample(samples[i], ch, sampleRate);
            }
        }
        else if (bitsPerSample == 16)
        {
            Span<short> samples = MemoryMarshal.Cast<byte, short>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                int ch = channels > 1 ? (i % channels) : 0;
                float inVal = samples[i] / 32768.0f;
                float outVal = ProcessMicSample(inVal, ch, sampleRate);
                samples[i] = (short)Math.Clamp((int)MathF.Round(outVal * 32767.0f), -32768, 32767);
            }
        }
    }

    public (string? speakerWav, string? micWav) StopRecording()
    {
        if (!IsRecording) return (null, null);
        IsRecording = false;

        // Stop and flush speaker loopback
        try
        {
            if (_silencePlayer != null)
            {
                _silencePlayer.Stop();
                _silencePlayer.Dispose();
                _silencePlayer = null;
            }
        }
        catch { }

        try
        {
            if (_loopbackRecorder != null)
            {
                _loopbackRecorder.StopRecording();
                _loopbackRecorder.Dispose();
                _loopbackRecorder = null;
            }
        }
        catch { }

        try
        {
            if (_loopbackWriter != null)
            {
                _loopbackWriter.Flush();
                _loopbackWriter.Dispose();
                _loopbackWriter = null;
            }
        }
        catch { }

        // Stop and flush mic capture
        try
        {
            if (_micRecorder != null)
            {
                _micRecorder.StopRecording();
                _micRecorder.Dispose();
                _micRecorder = null;
            }
        }
        catch { }

        try
        {
            if (_micWriter != null)
            {
                _micWriter.Flush();
                _micWriter.Dispose();
                _micWriter = null;
            }
        }
        catch { }

        // Validate that WAV files exist and have data chunk beyond the standard 44-byte header
        string? speaker = (!string.IsNullOrEmpty(_loopbackPath) && File.Exists(_loopbackPath) && new FileInfo(_loopbackPath).Length > 44)
            ? _loopbackPath
            : null;

        string? mic = (!string.IsNullOrEmpty(_micPath) && File.Exists(_micPath) && new FileInfo(_micPath).Length > 44)
            ? _micPath
            : null;

        return (speaker, mic);
    }

    public void Dispose()
    {
        StopRecording();
    }
}
