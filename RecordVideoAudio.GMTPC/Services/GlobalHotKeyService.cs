using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RecordVideoAudio.GMTPC.Services;

public class GlobalHotKeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_SHIFT = 0x10;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const int VK_F8 = 0x77;
    private const int VK_F9 = 0x78;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _proc;
    private IntPtr _hookId = IntPtr.Zero;

    // Hotkey settings for Record (Default: Ctrl + Alt + Shift + D5)
    public bool RecordCtrl { get; set; } = true;
    public bool RecordAlt { get; set; } = true;
    public bool RecordShift { get; set; } = true;
    public bool RecordWin { get; set; } = false;
    public int RecordVkCode { get; set; } = 0x35; // D5

    // Hotkey settings for Pause (Default: Ctrl + Alt + Shift + D8)
    public bool PauseCtrl { get; set; } = true;
    public bool PauseAlt { get; set; } = true;
    public bool PauseShift { get; set; } = true;
    public bool PauseWin { get; set; } = false;
    public int PauseVkCode { get; set; } = 0x38; // D8

    public event Action? RecordTogglePressed;
    public event Action? PauseTogglePressed;
    public event Action? F8Pressed; // Backward compatibility
    public event Action? F9Pressed; // Backward compatibility

    public bool IsActive => _hookId != IntPtr.Zero;

    public void UpdateHotKeys(RecordVideoAudio.GMTPC.Models.RecordingConfig config)
    {
        RecordCtrl = config.RecordCtrl;
        RecordAlt = config.RecordAlt;
        RecordShift = config.RecordShift;
        RecordWin = config.RecordWin;
        RecordVkCode = config.RecordVkCode;

        PauseCtrl = config.PauseCtrl;
        PauseAlt = config.PauseAlt;
        PauseShift = config.PauseShift;
        PauseWin = config.PauseWin;
        PauseVkCode = config.PauseVkCode;
    }

    public GlobalHotKeyService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                StartHook();
            }
            catch { }
        }
    }

    [SupportedOSPlatform("windows")]
    private void StartHook()
    {
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule != null)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            // Read modifiers
            bool isCtrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isAlt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool isShift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool isWin = ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);

            // Match Record Hotkey
            bool recordModifiersMatch = (isCtrl == RecordCtrl) && (isAlt == RecordAlt) && (isShift == RecordShift) && (isWin == RecordWin);
            if (vkCode == RecordVkCode && recordModifiersMatch)
            {
                RecordTogglePressed?.Invoke();
                F8Pressed?.Invoke();
            }
            else if (vkCode == VK_F8 && !isCtrl && !isAlt && !isShift && !isWin)
            {
                // Fallback F8
                RecordTogglePressed?.Invoke();
                F8Pressed?.Invoke();
            }

            // Match Pause Hotkey
            bool pauseModifiersMatch = (isCtrl == PauseCtrl) && (isAlt == PauseAlt) && (isShift == PauseShift) && (isWin == PauseWin);
            if (vkCode == PauseVkCode && pauseModifiersMatch)
            {
                PauseTogglePressed?.Invoke();
                F9Pressed?.Invoke();
            }
            else if (vkCode == VK_F9 && !isCtrl && !isAlt && !isShift && !isWin)
            {
                // Fallback F9
                PauseTogglePressed?.Invoke();
                F9Pressed?.Invoke();
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public void Dispose()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        if (_hookId != IntPtr.Zero)
        {
            try
            {
                UnhookWindowsHookEx(_hookId);
            }
            catch { }
            _hookId = IntPtr.Zero;
        }
    }
}
