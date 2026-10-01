using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public class WasapiAudioRecorder : IDisposable
{
    private WasapiRecorder? _loopbackRecorder;
    private AsyncAudioWriter? _loopbackWriter;
    private string? _loopbackPath;

    private WasapiPlayer? _silencePlayer;

    private WasapiRecorder? _micRecorder;
    private AsyncAudioWriter? _micWriter;
    private string? _micPath;

    // Real-time Audio Configuration (Thread-Safe volatile)
    private volatile bool _recordSpeaker = true;
    private volatile float _speakerVolume = 1.0f;
    private volatile float _speakerGainDb = 0.0f;
    private volatile bool _speakerAutoDucking = false;
    private float _currentDuckingGain = 1.0f;

    private volatile bool _recordMic = true;
    private volatile float _micVolume = 0.9f;
    private volatile float _micGainDb = 0.0f;
    private volatile bool _micNoiseSuppression = true;
    private volatile bool _micNoiseGate = false;
    private volatile float _micNoiseGateThresholdDb = -36.0f;
    private volatile bool _micHighPassFilter = true;

    // Studio Vocal Polish (Compressor, EQ & De-Esser)
    private volatile bool _micCompressor = true;
    private volatile float _micCompressorThresholdDb = -18.0f;
    private volatile float _micCompressorRatio = 4.0f;
    private volatile VocalProfile _micVocalProfile = VocalProfile.BroadcastWarmth;
    private volatile bool _micDeEsser = true;

    // Auto-Tune & Voice FX (Pitch Correction & Voice Changer)
    private volatile bool _micAutoTune = false;
    private volatile MusicalKey _micAutoTuneKey = MusicalKey.C;
    private volatile AutoTuneScale _micAutoTuneScale = AutoTuneScale.Chromatic;
    private volatile int _micAutoTuneSpeed = 20; // 0ms (Hard Robot) to 100ms (Natural)
    private volatile int _micPitchShiftSemitones = 0; // -12 to +12 semitones

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

    // Adaptive Smooth Denoise & Envelope State (Anti-chattering)
    private float _micVoiceEnvelope = 0.0f;
    private float _micNoiseFloor = 0.005f; // Initial estimate ~ -46 dB
    private float _denoiseGainSmoothed = 1.0f;

    // Smooth Noise Gate State (Long Hold to prevent word cutting)
    private float _micGateGain = 1.0f;
    private int _micGateHoldCounter = 0;

    // Dynamic Compressor State
    private float _compEnvelope = 0.0f;
    private const float CompAttackCoeff = 0.85f;
    private const float CompReleaseCoeff = 0.9995f;

    // 3-Band Vocal EQ Biquad State (Warmth, Clarity, Air)
    private readonly float[] _eq1X1 = new float[8];
    private readonly float[] _eq1X2 = new float[8];
    private readonly float[] _eq1Y1 = new float[8];
    private readonly float[] _eq1Y2 = new float[8];
    private float _eq1B0 = 1, _eq1B1 = 0, _eq1B2 = 0, _eq1A1 = 0, _eq1A2 = 0;

    private readonly float[] _eq2X1 = new float[8];
    private readonly float[] _eq2X2 = new float[8];
    private readonly float[] _eq2Y1 = new float[8];
    private readonly float[] _eq2Y2 = new float[8];
    private float _eq2B0 = 1, _eq2B1 = 0, _eq2B2 = 0, _eq2A1 = 0, _eq2A2 = 0;

    private readonly float[] _eq3X1 = new float[8];
    private readonly float[] _eq3X2 = new float[8];
    private readonly float[] _eq3Y1 = new float[8];
    private readonly float[] _eq3Y2 = new float[8];
    private float _eq3B0 = 1, _eq3B1 = 0, _eq3B2 = 0, _eq3A1 = 0, _eq3A2 = 0;

    private VocalProfile _lastVocalProfile = VocalProfile.Natural;
    private int _lastEqSampleRate = 0;

    // De-Esser State
    private float _sibilanceEnvelope = 0.0f;
    private float _deEsserGain = 1.0f;

    // Real-Time Pitch Shifter & Auto-Tune Engine (Continuous Phase Overlap-Add)
    private const int PitchDelayBufferSize = 8192;
    private const int PitchDelayBufferMask = PitchDelayBufferSize - 1;
    private const int PitchWindowSize = 1024; // ~21.3ms at 48kHz, optimal grain size for human speech
    private readonly float[] _pitchDelayBufferCh0 = new float[PitchDelayBufferSize];
    private readonly float[] _pitchDelayBufferCh1 = new float[PitchDelayBufferSize];
    private int _pitchWritePos = 0;
    private float _pitchPhase = 0.0f; // Continuous normalized phase [0..1)
    private float _smoothedPitchRatio = 1.0f;

    // Auto-Tune Tracker State
    private readonly float[] _pitchTrackBuffer = new float[512];
    private int _pitchTrackCount = 0;
    private float _smoothedAutoTuneShift = 0.0f;

    // Stereo Echo / Karaoke Delay State
    private volatile bool _micEcho = false;
    private volatile int _micEchoDelayMs = 220;
    private volatile float _micEchoFeedback = 0.35f;
    private volatile float _micEchoWetMix = 0.30f;
    private readonly float[] _echoBufferCh0 = new float[96000];
    private readonly float[] _echoBufferCh1 = new float[96000];
    private int _echoWritePos = 0;

    // Plate & Hall Reverb State (Schroeder-Moorer)
    private volatile bool _micReverb = false;
    private volatile float _micReverbRoomSize = 0.50f;
    private volatile float _micReverbDamping = 0.40f;
    private volatile float _micReverbWetMix = 0.25f;

    private readonly float[][] _combBuffers = [new float[1116], new float[1188], new float[1277], new float[1356]];
    private readonly int[] _combIndices = new int[4];
    private readonly float[] _combFilterStores = new float[4];
    private readonly float[][] _allpassBuffers = [new float[225], new float[341]];
    private readonly int[] _allpassIndices = new int[2];

    // Real-time audio snapshot buffers for Latency Detector
    private readonly float[] _latestSpeakerAudio = new float[48000];
    private readonly float[] _latestMicAudio = new float[48000];
    private int _speakerAudioWritePos = 0;
    private int _micAudioWritePos = 0;

    public bool IsRecording { get; private set; }

    public void GetLatestAudioSnapshot(out float[] speaker, out float[] mic)
    {
        speaker = new float[48000];
        mic = new float[48000];
        lock (_combIndices)
        {
            Array.Copy(_latestSpeakerAudio, speaker, 48000);
            Array.Copy(_latestMicAudio, mic, 48000);
        }
    }

    public void UpdateRealtimeSettings(
        bool recordSpeaker, double speakerVolume, double speakerGainDb,
        bool recordMic, double micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter,
        bool micCompressor, double micCompressorThresholdDb, double micCompressorRatio,
        VocalProfile micVocalProfile, bool micDeEsser,
        bool micAutoTune, MusicalKey micAutoTuneKey, AutoTuneScale micAutoTuneScale,
        int micAutoTuneSpeed, int micPitchShiftSemitones,
        bool micEcho = false, int micEchoDelayMs = 220, double micEchoFeedback = 35.0, double micEchoWetMix = 30.0,
        bool micReverb = false, double micReverbRoomSize = 50.0, double micReverbDamping = 40.0, double micReverbWetMix = 25.0,
        bool speakerAutoDucking = false)
    {
        _recordSpeaker = recordSpeaker;
        _speakerVolume = (float)Math.Clamp(speakerVolume / 100.0, 0.0, 1.0);
        _speakerGainDb = (float)Math.Clamp(speakerGainDb, -50.0, 50.0);
        _speakerAutoDucking = speakerAutoDucking;

        _recordMic = recordMic;
        _micVolume = (float)Math.Clamp(micVolume / 100.0, 0.0, 1.0);
        _micGainDb = (float)Math.Clamp(micGainDb, -50.0, 50.0);
        _micNoiseSuppression = micNoiseSuppression;
        _micNoiseGate = micNoiseGate;
        _micNoiseGateThresholdDb = (float)Math.Clamp(micNoiseGateThresholdDb, -80.0, 0.0);
        _micHighPassFilter = micHighPassFilter;

        _micCompressor = micCompressor;
        _micCompressorThresholdDb = (float)Math.Clamp(micCompressorThresholdDb, -60.0, 0.0);
        _micCompressorRatio = (float)Math.Clamp(micCompressorRatio, 1.0, 20.0);
        _micVocalProfile = micVocalProfile;
        _micDeEsser = micDeEsser;

        _micAutoTune = micAutoTune;
        _micAutoTuneKey = micAutoTuneKey;
        _micAutoTuneScale = micAutoTuneScale;
        _micAutoTuneSpeed = Math.Clamp(micAutoTuneSpeed, 0, 100);
        _micPitchShiftSemitones = Math.Clamp(micPitchShiftSemitones, -12, 12);

        _micEcho = micEcho;
        _micEchoDelayMs = Math.Clamp(micEchoDelayMs, 50, 600);
        _micEchoFeedback = (float)Math.Clamp(micEchoFeedback / 100.0, 0.0, 0.85);
        _micEchoWetMix = (float)Math.Clamp(micEchoWetMix / 100.0, 0.0, 1.0);

        _micReverb = micReverb;
        _micReverbRoomSize = (float)Math.Clamp(micReverbRoomSize / 100.0, 0.1, 0.95);
        _micReverbDamping = (float)Math.Clamp(micReverbDamping / 100.0, 0.0, 0.95);
        _micReverbWetMix = (float)Math.Clamp(micReverbWetMix / 100.0, 0.0, 1.0);
    }

    public void UpdateRealtimeSettings(
        bool recordSpeaker, double speakerVolume, double speakerGainDb,
        bool recordMic, double micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter,
        bool speakerAutoDucking = false)
    {
        UpdateRealtimeSettings(
            recordSpeaker, speakerVolume, speakerGainDb,
            recordMic, micVolume, micGainDb,
            micNoiseSuppression, micNoiseGate, micNoiseGateThresholdDb,
            micHighPassFilter,
            _micCompressor, _micCompressorThresholdDb, _micCompressorRatio,
            _micVocalProfile, _micDeEsser,
            _micAutoTune, _micAutoTuneKey, _micAutoTuneScale,
            _micAutoTuneSpeed, _micPitchShiftSemitones,
            _micEcho, _micEchoDelayMs, _micEchoFeedback * 100.0, _micEchoWetMix * 100.0,
            _micReverb, _micReverbRoomSize * 100.0, _micReverbDamping * 100.0, _micReverbWetMix * 100.0,
            speakerAutoDucking
        );
    }

    public string? StartRecording(bool recordSpeaker, float speakerVolume, bool recordMic, float micVolume, string outDir)
    {
        return StartRecording(
            recordSpeaker, speakerVolume, 0.0,
            recordMic, micVolume, 0.0,
            true, false, -36.0, true,
            true, -18.0, 4.0, VocalProfile.BroadcastWarmth, true,
            false, MusicalKey.C, AutoTuneScale.Chromatic, 20, 0,
            outDir
        );
    }

    public string? StartRecording(
        bool recordSpeaker, float speakerVolume, double speakerGainDb,
        bool recordMic, float micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter,
        string outDir,
        bool speakerAutoDucking = false)
    {
        return StartRecording(
            recordSpeaker, speakerVolume, speakerGainDb,
            recordMic, micVolume, micGainDb,
            micNoiseSuppression, micNoiseGate, micNoiseGateThresholdDb,
            micHighPassFilter,
            _micCompressor, _micCompressorThresholdDb, _micCompressorRatio,
            _micVocalProfile, _micDeEsser,
            _micAutoTune, _micAutoTuneKey, _micAutoTuneScale,
            _micAutoTuneSpeed, _micPitchShiftSemitones,
            outDir,
            _micEcho, _micEchoDelayMs, _micEchoFeedback * 100.0, _micEchoWetMix * 100.0,
            _micReverb, _micReverbRoomSize * 100.0, _micReverbDamping * 100.0, _micReverbWetMix * 100.0,
            speakerAutoDucking
        );
    }

    public string? StartRecording(
        bool recordSpeaker, float speakerVolume, double speakerGainDb,
        bool recordMic, float micVolume, double micGainDb,
        bool micNoiseSuppression, bool micNoiseGate, double micNoiseGateThresholdDb,
        bool micHighPassFilter,
        bool micCompressor, double micCompressorThresholdDb, double micCompressorRatio,
        VocalProfile micVocalProfile, bool micDeEsser,
        bool micAutoTune, MusicalKey micAutoTuneKey, AutoTuneScale micAutoTuneScale,
        int micAutoTuneSpeed, int micPitchShiftSemitones,
        string outDir,
        bool micEcho = false, int micEchoDelayMs = 220, double micEchoFeedback = 35.0, double micEchoWetMix = 30.0,
        bool micReverb = false, double micReverbRoomSize = 50.0, double micReverbDamping = 40.0, double micReverbWetMix = 25.0,
        bool speakerAutoDucking = false)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        StopRecording();

        UpdateRealtimeSettings(
            recordSpeaker, speakerVolume, speakerGainDb,
            recordMic, micVolume, micGainDb,
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

                        // Use lock-free background audio writer to avoid I/O disk stuttering
                        _loopbackWriter = new AsyncAudioWriter(_loopbackPath, _loopbackRecorder.WaveFormat);

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

                                    _loopbackWriter.Enqueue(workSpan);
                                }
                            }
                            catch { }
                        };

                        _loopbackRecorder.StartRecording();

                        // Keep Windows Audio Engine continuously running using inaudible active keep-alive stream
                        try
                        {
                            var silenceFormat = _loopbackRecorder.WaveFormat;
                            var silenceProvider = new KeepAliveSilenceProvider(silenceFormat);
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

                        // Use lock-free background audio writer to eliminate audio dropouts
                        _micWriter = new AsyncAudioWriter(_micPath, _micRecorder.WaveFormat);

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

                                    _micWriter.Enqueue(workSpan);
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

        Array.Clear(_eq1X1, 0, _eq1X1.Length);
        Array.Clear(_eq1X2, 0, _eq1X2.Length);
        Array.Clear(_eq1Y1, 0, _eq1Y1.Length);
        Array.Clear(_eq1Y2, 0, _eq1Y2.Length);

        Array.Clear(_eq2X1, 0, _eq2X1.Length);
        Array.Clear(_eq2X2, 0, _eq2X2.Length);
        Array.Clear(_eq2Y1, 0, _eq2Y1.Length);
        Array.Clear(_eq2Y2, 0, _eq2Y2.Length);

        Array.Clear(_eq3X1, 0, _eq3X1.Length);
        Array.Clear(_eq3X2, 0, _eq3X2.Length);
        Array.Clear(_eq3Y1, 0, _eq3Y1.Length);
        Array.Clear(_eq3Y2, 0, _eq3Y2.Length);

        Array.Clear(_pitchDelayBufferCh0, 0, _pitchDelayBufferCh0.Length);
        Array.Clear(_pitchDelayBufferCh1, 0, _pitchDelayBufferCh1.Length);
        _pitchWritePos = 0;
        _pitchPhase = 0.0f;
        _smoothedPitchRatio = 1.0f;
        _pitchTrackCount = 0;
        _smoothedAutoTuneShift = 0.0f;

        _lastSampleRate = 0;
        _lastEqSampleRate = 0;
        _micVoiceEnvelope = 0.0f;
        _micNoiseFloor = 0.005f;
        _denoiseGainSmoothed = 1.0f;
        _micGateGain = 1.0f;
        _micGateHoldCounter = 0;
        _compEnvelope = 0.0f;
        _sibilanceEnvelope = 0.0f;
        _deEsserGain = 1.0f;
        _currentDuckingGain = 1.0f;
    }

    private void ProcessSpeakerData(Span<byte> data, int bitsPerSample)
    {
        if (!_recordSpeaker || _speakerVolume <= 0.0001f)
        {
            data.Clear();
            return;
        }

        // Auto Ducking: Tự động hạ âm lượng Loa khi Micro đang thu tiếng nói/hát
        float targetDuck = (_speakerAutoDucking && _recordMic && _micVoiceEnvelope > 0.012f) ? 0.20f : 1.0f;
        float duckAlpha = targetDuck < _currentDuckingGain ? 0.005f : 0.0005f;
        _currentDuckingGain += (targetDuck - _currentDuckingGain) * duckAlpha;

        float linearGain = MathF.Pow(10.0f, _speakerGainDb / 20.0f);
        float totalMultiplier = _speakerVolume * linearGain * _currentDuckingGain;

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

                if (i % 2 == 0)
                {
                    _latestSpeakerAudio[_speakerAudioWritePos] = val;
                    _speakerAudioWritePos = (_speakerAudioWritePos + 1) % 48000;
                }
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

                if (i % 2 == 0)
                {
                    _latestSpeakerAudio[_speakerAudioWritePos] = val;
                    _speakerAudioWritePos = (_speakerAudioWritePos + 1) % 48000;
                }
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

    private void UpdateVocalEqCoefficients(int sampleRate)
    {
        if (_lastEqSampleRate == sampleRate && _lastVocalProfile == _micVocalProfile) return;
        _lastEqSampleRate = sampleRate;
        _lastVocalProfile = _micVocalProfile;

        if (sampleRate <= 0) return;

        float gainWarmth = 0.0f;
        float gainClarity = 0.0f;
        float gainAir = 0.0f;

        switch (_micVocalProfile)
        {
            case VocalProfile.BroadcastWarmth:
                gainWarmth = 3.5f;
                gainClarity = 2.0f;
                gainAir = 2.5f;
                break;
            case VocalProfile.CrystalClear:
                gainWarmth = -1.5f;
                gainClarity = 4.5f;
                gainAir = 3.5f;
                break;
            case VocalProfile.PodcastStudio:
                gainWarmth = 2.5f;
                gainClarity = 2.0f;
                gainAir = 1.5f;
                break;
            case VocalProfile.Natural:
            default:
                gainWarmth = 0.0f;
                gainClarity = 0.0f;
                gainAir = 0.0f;
                break;
        }

        CalcPeakingEq(150.0f, gainWarmth, 0.8f, sampleRate, out _eq1B0, out _eq1B1, out _eq1B2, out _eq1A1, out _eq1A2);
        CalcPeakingEq(3500.0f, gainClarity, 1.0f, sampleRate, out _eq2B0, out _eq2B1, out _eq2B2, out _eq2A1, out _eq2A2);
        CalcPeakingEq(10000.0f, gainAir, 0.7071f, sampleRate, out _eq3B0, out _eq3B1, out _eq3B2, out _eq3A1, out _eq3A2);
    }

    private static void CalcPeakingEq(float f0, float gainDb, float q, int sampleRate, out float b0, out float b1, out float b2, out float a1, out float a2)
    {
        if (MathF.Abs(gainDb) < 0.05f || sampleRate <= 0)
        {
            b0 = 1.0f; b1 = 0.0f; b2 = 0.0f; a1 = 0.0f; a2 = 0.0f;
            return;
        }

        float a = MathF.Pow(10.0f, gainDb / 40.0f);
        float w0 = 2.0f * MathF.PI * f0 / sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2.0f * q);

        float b0Raw = 1.0f + alpha * a;
        float b1Raw = -2.0f * cosW0;
        float b2Raw = 1.0f - alpha * a;
        float a0Raw = 1.0f + alpha / a;
        float a1Raw = -2.0f * cosW0;
        float a2Raw = 1.0f - alpha / a;

        b0 = b0Raw / a0Raw;
        b1 = b1Raw / a0Raw;
        b2 = b2Raw / a0Raw;
        a1 = a1Raw / a0Raw;
        a2 = a2Raw / a0Raw;
    }

    private float DetectPitchAndCalculateCorrection(float sample, int sampleRate)
    {
        _pitchTrackBuffer[_pitchTrackCount++] = sample;
        if (_pitchTrackCount < _pitchTrackBuffer.Length)
        {
            return _smoothedAutoTuneShift;
        }

        _pitchTrackCount = 0;

        // Sub-sampled autocorrelation pitch detection (Range 80Hz - 480Hz)
        int minLag = Math.Max(1, sampleRate / 480);
        int maxLag = Math.Min(_pitchTrackBuffer.Length - 1, sampleRate / 80);

        float maxCorr = 0.0f;
        int bestLag = -1;

        for (int lag = minLag; lag <= maxLag; lag += 2)
        {
            float corr = 0.0f;
            for (int i = 0; i < 256; i += 2)
            {
                corr += _pitchTrackBuffer[i] * _pitchTrackBuffer[i + lag];
            }

            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestLag = lag;
            }
        }

        if (bestLag > 0 && maxCorr > 0.005f)
        {
            float detectedFreq = (float)sampleRate / bestLag;
            if (detectedFreq >= 80.0f && detectedFreq <= 480.0f)
            {
                float midiNote = 69.0f + 12.0f * MathF.Log2(detectedFreq / 440.0f);
                float targetMidi = QuantizeToScale(midiNote, _micAutoTuneKey, _micAutoTuneScale);
                float diff = targetMidi - midiNote;

                if (_micAutoTuneSpeed <= 8)
                {
                    _smoothedAutoTuneShift = diff;
                }
                else
                {
                    float alpha = Math.Clamp(1.0f - (_micAutoTuneSpeed / 100.0f) * 0.9f, 0.05f, 0.95f);
                    _smoothedAutoTuneShift = _smoothedAutoTuneShift * (1.0f - alpha) + diff * alpha;
                }
            }
        }
        else
        {
            _smoothedAutoTuneShift *= 0.96f;
        }

        return _smoothedAutoTuneShift;
    }

    private static float QuantizeToScale(float midiNote, MusicalKey key, AutoTuneScale scale)
    {
        int rootOffset = (int)key;
        int noteIn12 = ((int)MathF.Round(midiNote) % 12 + 12) % 12;
        int octave = (int)MathF.Floor(midiNote / 12.0f);

        int[] allowedIntervals = scale switch
        {
            AutoTuneScale.Major => [0, 2, 4, 5, 7, 9, 11],
            AutoTuneScale.Minor => [0, 2, 3, 5, 7, 8, 10],
            AutoTuneScale.Chromatic => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
            _ => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
        };

        int bestInterval = allowedIntervals[0];
        int minDistance = 999;

        foreach (var interval in allowedIntervals)
        {
            int candidateNoteIn12 = (rootOffset + interval) % 12;
            int dist = Math.Abs(candidateNoteIn12 - noteIn12);
            if (dist > 6) dist = 12 - dist;

            if (dist < minDistance)
            {
                minDistance = dist;
                bestInterval = candidateNoteIn12;
            }
        }

        return octave * 12.0f + bestInterval;
    }

    private static float InterpolateDelay(float[] buffer, float pos)
    {
        int i0 = (int)pos;
        int i1 = (i0 + 1) & (buffer.Length - 1);
        float frac = pos - i0;
        return buffer[i0] + frac * (buffer[i1] - buffer[i0]);
    }

    private float ProcessMicSample(float sample, int ch, int channels, int sampleRate)
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

        // Track voice envelope with smooth attack and release
        _micVoiceEnvelope = _micVoiceEnvelope * 0.9992f + absVal * 0.0008f;

        // 2. Adaptive Smooth Denoise (Time-smoothed attenuation to eliminate stutter / chattering)
        if (_micNoiseSuppression)
        {
            if (_micVoiceEnvelope < _micNoiseFloor)
            {
                _micNoiseFloor = _micNoiseFloor * 0.995f + _micVoiceEnvelope * 0.005f;
            }
            else
            {
                _micNoiseFloor = _micNoiseFloor * 0.99998f + _micVoiceEnvelope * 0.00002f;
            }

            _micNoiseFloor = Math.Clamp(_micNoiseFloor, 0.0001f, 0.05f);

            float snr = _micVoiceEnvelope / _micNoiseFloor;
            float targetAtten = 1.0f;
            if (snr < 2.0f)
            {
                // Maximum 8dB attenuation with smooth knee to preserve speech tails
                targetAtten = Math.Clamp(snr / 2.0f, 0.40f, 1.0f);
            }

            // Smooth attenuation coefficient across 50ms to eliminate abrupt cutting
            _denoiseGainSmoothed = _denoiseGainSmoothed * 0.996f + targetAtten * 0.004f;
            val *= _denoiseGainSmoothed;
        }

        // 3. Smooth Noise Gate (Long 350ms Hold Time to prevent cutting off words)
        if (_micNoiseGate)
        {
            float gateLinear = MathF.Pow(10.0f, _micNoiseGateThresholdDb / 20.0f);
            if (_micVoiceEnvelope > gateLinear)
            {
                _micGateGain = MathF.Min(1.0f, _micGateGain + 0.02f);
                _micGateHoldCounter = (int)(0.35f * sampleRate); // 350ms Hold
            }
            else
            {
                if (_micGateHoldCounter > 0)
                {
                    _micGateHoldCounter--;
                }
                else
                {
                    _micGateGain = MathF.Max(0.0f, _micGateGain - 0.0005f); // 250ms smooth release
                }
            }
            val *= _micGateGain;
        }

        // 4. De-Esser (Triệt âm xì, chói tai quanh 7kHz)
        if (_micDeEsser)
        {
            _sibilanceEnvelope = MathF.Max(absVal, _sibilanceEnvelope * 0.998f);
            if (_sibilanceEnvelope > 0.15f && absVal > 0.12f)
            {
                _deEsserGain = MathF.Max(0.60f, _deEsserGain - 0.01f);
            }
            else
            {
                _deEsserGain = MathF.Min(1.0f, _deEsserGain + 0.003f);
            }
            val *= _deEsserGain;
        }

        // 5. 3-Band Vocal EQ (Warmth, Clarity, Air)
        if (_micVocalProfile != VocalProfile.Natural)
        {
            int chIdx = ch % _eq1X1.Length;

            // Band 1: Warmth (150Hz)
            float y1 = _eq1B0 * val + _eq1B1 * _eq1X1[chIdx] + _eq1B2 * _eq1X2[chIdx] - _eq1A1 * _eq1Y1[chIdx] - _eq1A2 * _eq1Y2[chIdx];
            if (float.IsNaN(y1) || float.IsInfinity(y1)) y1 = val;
            _eq1X2[chIdx] = _eq1X1[chIdx]; _eq1X1[chIdx] = val;
            _eq1Y2[chIdx] = _eq1Y1[chIdx]; _eq1Y1[chIdx] = y1;
            val = y1;

            // Band 2: Clarity (3500Hz)
            float y2 = _eq2B0 * val + _eq2B1 * _eq2X1[chIdx] + _eq2B2 * _eq2X2[chIdx] - _eq2A1 * _eq2Y1[chIdx] - _eq2A2 * _eq2Y2[chIdx];
            if (float.IsNaN(y2) || float.IsInfinity(y2)) y2 = val;
            _eq2X2[chIdx] = _eq2X1[chIdx]; _eq2X1[chIdx] = val;
            _eq2Y2[chIdx] = _eq2Y1[chIdx]; _eq2Y1[chIdx] = y2;
            val = y2;

            // Band 3: Air (10000Hz)
            float y3 = _eq3B0 * val + _eq3B1 * _eq3X1[chIdx] + _eq3B2 * _eq3X2[chIdx] - _eq3A1 * _eq3Y1[chIdx] - _eq3A2 * _eq3Y2[chIdx];
            if (float.IsNaN(y3) || float.IsInfinity(y3)) y3 = val;
            _eq3X2[chIdx] = _eq3X1[chIdx]; _eq3X1[chIdx] = val;
            _eq3Y2[chIdx] = _eq3Y1[chIdx]; _eq3Y1[chIdx] = y3;
            val = y3;
        }

        // 6. Dynamic Vocal Compressor (Chống rè và triệt tiêu vỡ tiếng khi nói to/hét)
        if (_micCompressor)
        {
            float curAbs = MathF.Abs(val);
            if (curAbs > _compEnvelope)
                _compEnvelope = curAbs * (1.0f - CompAttackCoeff) + _compEnvelope * CompAttackCoeff;
            else
                _compEnvelope = curAbs * (1.0f - CompReleaseCoeff) + _compEnvelope * CompReleaseCoeff;

            float envDb = _compEnvelope > 1e-5f ? 20.0f * MathF.Log10(_compEnvelope) : -100.0f;
            if (envDb > _micCompressorThresholdDb)
            {
                float overDb = envDb - _micCompressorThresholdDb;
                float gainReductionDb = overDb * (1.0f - 1.0f / _micCompressorRatio);
                float compGain = MathF.Pow(10.0f, -gainReductionDb / 20.0f);
                val *= compGain;
                val *= 1.25f; // +2 dB Makeup Gain
            }
        }

        // 7. Auto-Tune & Pitch Shifting (Continuous Phase Overlap-Add - 100% Non-Interrupting)
        if (_micAutoTune || _micPitchShiftSemitones != 0)
        {
            float autoTuneShift = _micAutoTune ? DetectPitchAndCalculateCorrection(val, sampleRate) : 0.0f;
            float targetShiftSemitones = autoTuneShift + _micPitchShiftSemitones;
            float targetPitchRatio = MathF.Pow(2.0f, Math.Clamp(targetShiftSemitones, -24.0f, 24.0f) / 12.0f);

            // Smooth pitch ratio to eliminate abrupt step changes and clicking
            _smoothedPitchRatio = _smoothedPitchRatio * 0.999f + targetPitchRatio * 0.001f;

            if (MathF.Abs(_smoothedPitchRatio - 1.0f) > 0.003f)
            {
                float[] delayBuf = (ch % 2 == 0) ? _pitchDelayBufferCh0 : _pitchDelayBufferCh1;
                delayBuf[_pitchWritePos] = val;

                // Tap 0
                float phase0 = _pitchPhase;
                float delay0 = phase0 * PitchWindowSize;
                float readPos0 = _pitchWritePos - delay0;
                while (readPos0 < 0) readPos0 += PitchDelayBufferSize;

                // Tap 1 (shifted by 180 degrees = 0.5 cycle)
                float phase1 = phase0 + 0.5f;
                if (phase1 >= 1.0f) phase1 -= 1.0f;
                float delay1 = phase1 * PitchWindowSize;
                float readPos1 = _pitchWritePos - delay1;
                while (readPos1 < 0) readPos1 += PitchDelayBufferSize;

                // Continuous Hanning weights: w0 + w1 == 1.0 identically at all times
                float cosPhase = MathF.Cos(2.0f * MathF.PI * phase0);
                float w0 = 0.5f - 0.5f * cosPhase;
                float w1 = 0.5f + 0.5f * cosPhase;

                float s0 = InterpolateDelay(delayBuf, readPos0);
                float s1 = InterpolateDelay(delayBuf, readPos1);

                val = s0 * w0 + s1 * w1;

                // Advance phase and write pointer once per full audio frame
                if (ch == channels - 1 || channels <= 1)
                {
                    float deltaPhase = (1.0f - _smoothedPitchRatio) / PitchWindowSize;
                    _pitchPhase += deltaPhase;
                    if (_pitchPhase >= 1.0f) _pitchPhase -= 1.0f;
                    else if (_pitchPhase < 0.0f) _pitchPhase += 1.0f;

                    _pitchWritePos = (_pitchWritePos + 1) & PitchDelayBufferMask;
                }
            }
            else
            {
                // Bypass pitch delay line when pitch ratio is ~1.0 (natural voice)
                if (ch == channels - 1 || channels <= 1)
                {
                    _pitchWritePos = (_pitchWritePos + 1) & PitchDelayBufferMask;
                }
            }
        }
        else
        {
            _smoothedPitchRatio = 1.0f;
        }

        // 7.1. Stereo Echo (Karaoke Delay)
        if (_micEcho)
        {
            int delaySamples = Math.Clamp((int)(_micEchoDelayMs * sampleRate / 1000.0f), 10, 95990);
            int readIdx = (_echoWritePos - delaySamples + 96000) % 96000;
            float[] echoBuf = (ch % 2 == 0) ? _echoBufferCh0 : _echoBufferCh1;
            float echoSample = echoBuf[readIdx];
            echoBuf[_echoWritePos] = val + echoSample * _micEchoFeedback * 0.85f;
            val = val * (1.0f - _micEchoWetMix * 0.4f) + echoSample * _micEchoWetMix;

            if (ch == channels - 1 || channels <= 1)
            {
                _echoWritePos = (_echoWritePos + 1) % 96000;
            }
        }

        // 7.2. Plate & Hall Reverb (Schroeder-Moorer)
        if (_micReverb)
        {
            float reverbOut = 0.0f;
            float feedback = Math.Clamp(0.7f + _micReverbRoomSize * 0.28f, 0.5f, 0.98f);
            float damping = Math.Clamp(_micReverbDamping, 0.0f, 0.9f);

            // 4 Comb Filters in parallel
            for (int k = 0; k < 4; k++)
            {
                var buf = _combBuffers[k];
                int idx = _combIndices[k];
                float output = buf[idx];
                _combFilterStores[k] = (output * (1.0f - damping)) + (_combFilterStores[k] * damping);
                buf[idx] = val + (_combFilterStores[k] * feedback);
                _combIndices[k] = (idx + 1) % buf.Length;
                reverbOut += output;
            }

            reverbOut *= 0.25f;

            // 2 All-Pass Filters in series
            for (int k = 0; k < 2; k++)
            {
                var buf = _allpassBuffers[k];
                int idx = _allpassIndices[k];
                float bufOut = buf[idx];
                float apFeedback = 0.5f;
                float apIn = reverbOut;
                reverbOut = -apIn + bufOut;
                buf[idx] = apIn + (bufOut * apFeedback);
                _allpassIndices[k] = (idx + 1) % buf.Length;
            }

            val = val * (1.0f - _micReverbWetMix * 0.4f) + reverbOut * _micReverbWetMix;
        }

        // 8. Gain (-50 dB to +50 dB) and Volume
        float micGainLinear = MathF.Pow(10.0f, _micGainDb / 20.0f);
        val *= (_micVolume * micGainLinear);

        // 9. Soft Limiter (chống méo clipping)
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
        UpdateVocalEqCoefficients(sampleRate);

        if (bitsPerSample == 32)
        {
            Span<float> samples = MemoryMarshal.Cast<byte, float>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                int ch = channels > 1 ? (i % channels) : 0;
                samples[i] = ProcessMicSample(samples[i], ch, channels, sampleRate);

                if (ch == 0)
                {
                    _latestMicAudio[_micAudioWritePos] = samples[i];
                    _micAudioWritePos = (_micAudioWritePos + 1) % 48000;
                }
            }
        }
        else if (bitsPerSample == 16)
        {
            Span<short> samples = MemoryMarshal.Cast<byte, short>(data);
            for (int i = 0; i < samples.Length; i++)
            {
                int ch = channels > 1 ? (i % channels) : 0;
                float inVal = samples[i] / 32768.0f;
                float outVal = ProcessMicSample(inVal, ch, channels, sampleRate);
                samples[i] = (short)Math.Clamp((int)MathF.Round(outVal * 32767.0f), -32768, 32767);

                if (ch == 0)
                {
                    _latestMicAudio[_micAudioWritePos] = outVal;
                    _micAudioWritePos = (_micAudioWritePos + 1) % 48000;
                }
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

    /// <summary>
    /// Thread-safe, non-blocking asynchronous audio writer to prevent disk I/O bottlenecks and audio dropouts.
    /// </summary>
    private sealed class AsyncAudioWriter : IDisposable
    {
        private readonly WaveFileWriter _writer;
        private readonly BlockingCollection<(byte[] data, int length)> _queue = new(500);
        private readonly Thread _workerThread;
        private volatile bool _isRunning = true;

        public AsyncAudioWriter(string path, WaveFormat format)
        {
            _writer = new WaveFileWriter(path, format);
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = $"AsyncAudioWriter_{Path.GetFileNameWithoutExtension(path)}"
            };
            _workerThread.Start();
        }

        public void Enqueue(ReadOnlySpan<byte> buffer)
        {
            if (!_isRunning || _queue.IsAddingCompleted || buffer.IsEmpty) return;
            byte[] copy = new byte[buffer.Length];
            buffer.CopyTo(copy);
            _queue.TryAdd((copy, buffer.Length));
        }

        private void WorkerLoop()
        {
            while (_isRunning || _queue.Count > 0)
            {
                try
                {
                    if (_queue.TryTake(out var item, 100))
                    {
                        _writer.Write(item.data, 0, item.length);
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _queue.CompleteAdding();
            try { _workerThread.Join(3000); } catch { }
            try { _writer.Flush(); } catch { }
            try { _writer.Dispose(); } catch { }
            _queue.Dispose();
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
            if (_format.BitsPerSample == 32 && _format.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                var span = MemoryMarshal.Cast<byte, float>(buffer);
                for (int i = 0; i < span.Length; i += _format.Channels)
                {
                    _phase += 0.001f;
                    float dither = MathF.Sin(_phase) * 1e-5f; // -100 dBFS inaudible dither
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
}
