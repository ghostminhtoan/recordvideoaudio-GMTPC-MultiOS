using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace RecordVideoAudio.GMTPC.Services;

public class RealtimeAudioMonitor : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _speakerDevice;
    private MMDevice? _micDevice;
    private bool _initialized;

    private readonly object _lock = new();

    public RealtimeAudioMonitor()
    {
        InitializeDevices();
    }

    private void InitializeDevices()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            InitWindowsDevices();
        }
        catch
        {
            _initialized = false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void InitWindowsDevices(string? speakerDeviceId = null, string? micDeviceId = null)
    {
        MMDevice? newSpeaker = null;
        MMDevice? newMic = null;

        try
        {
            _enumerator ??= new MMDeviceEnumerator();

            if (!string.IsNullOrEmpty(speakerDeviceId) && speakerDeviceId != "default")
            {
                try { newSpeaker = _enumerator.GetDevice(speakerDeviceId); } catch { }
            }
            newSpeaker ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch { }

        try
        {
            _enumerator ??= new MMDeviceEnumerator();

            if (!string.IsNullOrEmpty(micDeviceId) && micDeviceId != "default")
            {
                try { newMic = _enumerator.GetDevice(micDeviceId); } catch { }
            }
            newMic ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        catch { }

        MMDevice? oldSpeaker;
        MMDevice? oldMic;

        lock (_lock)
        {
            oldSpeaker = _speakerDevice;
            oldMic = _micDevice;

            _speakerDevice = newSpeaker;
            _micDevice = newMic;
            _initialized = (_speakerDevice != null || _micDevice != null);
        }

        // Dispose previous COM endpoints outside lock to prevent deadlocks and access violation on active polling threads
        try { oldSpeaker?.Dispose(); } catch { }
        try { oldMic?.Dispose(); } catch { }
    }

    public void UpdateSelectedDevices(string? speakerDeviceId, string? micDeviceId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            InitWindowsDevices(speakerDeviceId, micDeviceId);
        }
        catch { }
    }

    public (double speakerLevel, double micLevel) GetCurrentLevels(bool speakerEnabled, double speakerVolume, bool micEnabled, double micVolume)
    {
        return GetCurrentLevels(speakerEnabled, speakerVolume, 0.0, micEnabled, micVolume, 0.0, false, -36.0);
    }

    public (double speakerLevel, double micLevel) GetCurrentLevels(
        bool speakerEnabled, double speakerVolume, double speakerGainDb,
        bool micEnabled, double micVolume, double micGainDb,
        bool micNoiseGate = false, double micNoiseGateThresholdDb = -36.0)
    {
        if (!_initialized || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (0, 0);

        try
        {
            return GetWindowsLevels(speakerEnabled, speakerVolume, speakerGainDb, micEnabled, micVolume, micGainDb, micNoiseGate, micNoiseGateThresholdDb);
        }
        catch
        {
            return (0, 0);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private (double speakerLevel, double micLevel) GetWindowsLevels(
        bool speakerEnabled, double speakerVolume, double speakerGainDb,
        bool micEnabled, double micVolume, double micGainDb,
        bool micNoiseGate, double micNoiseGateThresholdDb)
    {
        double speaker = 0;
        double mic = 0;

        MMDevice? currentSpeaker;
        MMDevice? currentMic;

        lock (_lock)
        {
            currentSpeaker = _speakerDevice;
            currentMic = _micDevice;
        }

        if (speakerEnabled && currentSpeaker != null)
        {
            try
            {
                var meter = currentSpeaker.AudioMeterInformation;
                if (meter != null)
                {
                    float peak = meter.MasterPeakValue;
                    double gainMultiplier = Math.Pow(10.0, speakerGainDb / 20.0);
                    speaker = Math.Min(100.0, Math.Max(0.0, peak * 100.0 * (speakerVolume / 100.0) * gainMultiplier));
                }
            }
            catch { }
        }

        if (micEnabled && currentMic != null)
        {
            try
            {
                var meter = currentMic.AudioMeterInformation;
                if (meter != null)
                {
                    float peak = meter.MasterPeakValue;
                    double gateLinear = Math.Pow(10.0, micNoiseGateThresholdDb / 20.0);
                    if (micNoiseGate && peak < gateLinear)
                    {
                        mic = 0;
                    }
                    else
                    {
                        double gainMultiplier = Math.Pow(10.0, micGainDb / 20.0);
                        mic = Math.Min(100.0, Math.Max(0.0, peak * 100.0 * (micVolume / 100.0) * gainMultiplier));
                    }
                }
            }
            catch { }
        }

        return (speaker, mic);
    }

    public void Dispose()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            DisposeWindows();
        }
        catch { }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void DisposeWindows()
    {
        MMDevice? spk;
        MMDevice? mic;
        MMDeviceEnumerator? enumerator;

        lock (_lock)
        {
            _initialized = false;
            spk = _speakerDevice;
            _speakerDevice = null;
            mic = _micDevice;
            _micDevice = null;
            enumerator = _enumerator;
            _enumerator = null;
        }

        try { spk?.Dispose(); } catch { }
        try { mic?.Dispose(); } catch { }
        try { enumerator?.Dispose(); } catch { }
    }
}
