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
        catch
        {
            _initialized = false;
        }
    }

    public (double speakerLevel, double micLevel) GetCurrentLevels(bool speakerEnabled, double speakerVolume, bool micEnabled, double micVolume)
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
                // peak is 0.0 to 1.0, scale to 0-100 and apply user volume slider
                speaker = Math.Min(100.0, Math.Max(0.0, peak * 100.0 * (speakerVolume / 100.0)));
            }
            catch
            {
                // Re-initialize if device was disconnected or changed
                InitializeDevices();
            }
        }

        if (micEnabled && _micDevice != null)
        {
            try
            {
                float peak = _micDevice.AudioMeterInformation.MasterPeakValue;
                mic = Math.Min(100.0, Math.Max(0.0, peak * 100.0 * (micVolume / 100.0)));
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
