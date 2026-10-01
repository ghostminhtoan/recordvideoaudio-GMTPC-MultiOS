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
    private void InitWindowsDevices()
    {
        _enumerator = new MMDeviceEnumerator();
        
        try
        {
            _speakerDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch { }

        try
        {
            _micDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
        catch { }

        _initialized = true;
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

        double speaker = 0;
        double mic = 0;

        if (speakerEnabled && _speakerDevice != null)
        {
            try
            {
                float peak = _speakerDevice.AudioMeterInformation.MasterPeakValue;
                double gainMultiplier = Math.Pow(10.0, speakerGainDb / 20.0);
                speaker = Math.Min(100.0, Math.Max(0.0, peak * 100.0 * (speakerVolume / 100.0) * gainMultiplier));
            }
            catch
            {
                InitializeDevices();
            }
        }

        if (micEnabled && _micDevice != null)
        {
            try
            {
                float peak = _micDevice.AudioMeterInformation.MasterPeakValue;
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
            catch
            {
                InitializeDevices();
            }
        }

        return (speaker, mic);
    }

    public void Dispose()
    {
        _speakerDevice?.Dispose();
        _micDevice?.Dispose();
        _enumerator?.Dispose();
    }
}
