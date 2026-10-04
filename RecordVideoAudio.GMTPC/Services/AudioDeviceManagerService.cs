using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using RecordVideoAudio.GMTPC.Models;

namespace RecordVideoAudio.GMTPC.Services;

public class AudioDeviceManagerService : IDisposable
{
    // Windows COM fields stored as object? with non-inlined methods for clean runtime isolation
    private object? _enumerator;           // MMDeviceEnumerator on Windows
    private object? _notificationClient;   // MMDeviceNotificationClient on Windows
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private bool _isDisposed;

    public event Action<List<AudioDeviceInfo>, List<AudioDeviceInfo>>? DevicesRefreshed;
    public event Action<string>? DeviceHotplugTraceLogged;

    public AudioDeviceManagerService()
    {
        InitializeHotplugListener();
    }

    private void InitializeHotplugListener()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            InitHotplugWindows();
        }
        catch (Exception ex)
        {
            DeviceHotplugTraceLogged?.Invoke($"[Lỗi khởi tạo Hotplug Audio] {ex.Message}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitHotplugWindows()
    {
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        _enumerator = enumerator;
        // Use nonblocking audio thread callbacks, we marshal and debounce ourselves
        var client = enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notificationClient = client;

        client.DeviceAdded += OnDeviceAdded;
        client.DeviceRemoved += OnDeviceRemoved;
        client.DeviceStateChanged += OnDeviceStateChanged;
        client.DefaultDeviceChanged += OnDefaultDeviceChanged;
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e)
    {
        string name = TryGetFriendlyName(e.DeviceId, out var flow);
        string flowText = flow == DataFlow.Capture ? "Microphone" : "Loa/Tai nghe";
        string msg = $"🔌 Đã kết nối thiết bị {flowText}: {name}";
        TriggerHotplugEvent(msg);
    }

    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e)
    {
        string msg = $"⚠️ Đã ngắt kết nối thiết bị âm thanh: {GetShortDeviceId(e.DeviceId)}";
        TriggerHotplugEvent(msg);
    }

    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        string name = TryGetFriendlyName(e.DeviceId, out _);
        string stateStr = e.NewState switch
        {
            DeviceState.Active => "Sẵn sàng (Active)",
            DeviceState.Disabled => "Vô hiệu hóa (Disabled)",
            DeviceState.NotPresent => "Không hiện diện (Not Present)",
            DeviceState.Unplugged => "Đã rút giắc cắm (Unplugged)",
            _ => e.NewState.ToString()
        };

        string msg = $"🔄 Trạng thái thiết bị '{name}' -> {stateStr}";
        TriggerHotplugEvent(msg);
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Role != Role.Multimedia && e.Role != Role.Console) return;

        string name = TryGetFriendlyName(e.DeviceId, out _);
        string flowText = e.Flow == DataFlow.Capture ? "Microphone" : "Loa/Tai nghe";
        string msg = $"🎧 Thiết bị mặc định Windows ({flowText}) chuyển sang: {name}";
        TriggerHotplugEvent(msg);
    }

    private void TriggerHotplugEvent(string message)
    {
        DeviceHotplugTraceLogged?.Invoke(message);

        // Debounce refresh to avoid rapid repeated COM scans
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try
                {
                    RefreshDevices();
                }
                catch { }
            }, null, 250, Timeout.Infinite);
        }
    }

    public (List<AudioDeviceInfo> speakers, List<AudioDeviceInfo> microphones) GetDevices()
    {
        var speakers = new List<AudioDeviceInfo>();
        var mics = new List<AudioDeviceInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            speakers.Add(new AudioDeviceInfo
            {
                Id = "default",
                Name = "🔊 [Mặc định] Thiết bị phát hệ thống",
                IsDefault = true,
                Flow = AudioDeviceFlow.Render
            });
            mics.Add(new AudioDeviceInfo
            {
                Id = "default",
                Name = "🎙️ [Mặc định] Micro hệ thống",
                IsDefault = true,
                Flow = AudioDeviceFlow.Capture
            });
            return (speakers, mics);
        }

        try
        {
            GetWindowsDevices(speakers, mics);
        }
        catch (Exception ex)
        {
            DeviceHotplugTraceLogged?.Invoke($"[Lỗi quét thiết bị âm thanh] {ex.Message}");
        }

        return (speakers, mics);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void GetWindowsDevices(List<AudioDeviceInfo> speakers, List<AudioDeviceInfo> mics)
    {
        using var enumerator = new MMDeviceEnumerator();

        // 1. Get default render endpoint
        string defaultRenderId = string.Empty;
        string defaultRenderName = "Hệ thống";
        try
        {
            var defRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defRender != null)
            {
                defaultRenderId = defRender.ID;
                defaultRenderName = defRender.FriendlyName;
            }
        }
        catch { }

        speakers.Add(new AudioDeviceInfo
        {
            Id = "default",
            Name = $"🔊 [Mặc định] {defaultRenderName}",
            IsDefault = true,
            Flow = AudioDeviceFlow.Render
        });

        try
        {
            var renderEndpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in renderEndpoints)
            {
                try
                {
                    speakers.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = string.Equals(device.ID, defaultRenderId, StringComparison.OrdinalIgnoreCase),
                        Flow = AudioDeviceFlow.Render
                    });
                }
                catch { }
            }
        }
        catch { }

        // 2. Get default capture endpoint
        string defaultCaptureId = string.Empty;
        string defaultCaptureName = "Hệ thống";
        try
        {
            var defCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            if (defCapture != null)
            {
                defaultCaptureId = defCapture.ID;
                defaultCaptureName = defCapture.FriendlyName;
            }
        }
        catch { }

        mics.Add(new AudioDeviceInfo
        {
            Id = "default",
            Name = $"🎙️ [Mặc định] {defaultCaptureName}",
            IsDefault = true,
            Flow = AudioDeviceFlow.Capture
        });

        try
        {
            var captureEndpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in captureEndpoints)
            {
                try
                {
                    mics.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = string.Equals(device.ID, defaultCaptureId, StringComparison.OrdinalIgnoreCase),
                        Flow = AudioDeviceFlow.Capture
                    });
                }
                catch { }
            }
        }
        catch { }
    }

    public void RefreshDevices()
    {
        var (speakers, mics) = GetDevices();
        DevicesRefreshed?.Invoke(speakers, mics);
    }

    private string TryGetFriendlyName(string deviceId, out DataFlow flow)
    {
        flow = DataFlow.All;
        if (string.IsNullOrEmpty(deviceId) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetShortDeviceId(deviceId);

        try
        {
            return GetWindowsFriendlyName(deviceId, ref flow);
        }
        catch { }

        return GetShortDeviceId(deviceId);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private string GetWindowsFriendlyName(string deviceId, ref DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var dev = enumerator.GetDevice(deviceId);
        if (dev != null)
        {
            flow = dev.DataFlow;
            return dev.FriendlyName;
        }
        return GetShortDeviceId(deviceId);
    }

    private static string GetShortDeviceId(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return "Unknown";
        int lastBrace = deviceId.LastIndexOf('{');
        return lastBrace >= 0 ? deviceId[lastBrace..] : deviceId;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                DisposeWindows();
            }
            catch { }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisposeWindows()
    {
        try
        {
            if (_notificationClient is MMDeviceNotificationClient client)
            {
                client.DeviceAdded -= OnDeviceAdded;
                client.DeviceRemoved -= OnDeviceRemoved;
                client.DeviceStateChanged -= OnDeviceStateChanged;
                client.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                client.Dispose();
                _notificationClient = null;
            }
        }
        catch { }

        try
        {
            if (_enumerator is IDisposable enumDisp)
            {
                enumDisp.Dispose();
                _enumerator = null;
            }
        }
        catch { }
    }
}
