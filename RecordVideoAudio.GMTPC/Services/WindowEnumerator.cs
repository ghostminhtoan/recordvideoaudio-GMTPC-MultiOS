using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace RecordVideoAudio.GMTPC.Services;

public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public static List<string> GetOpenWindows()
    {
        var windows = new List<string>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return windows;

        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        var sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString().Trim();
                        if (!string.IsNullOrEmpty(title) &&
                            !title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) &&
                            !title.Equals("Default IME", StringComparison.OrdinalIgnoreCase) &&
                            !title.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase) &&
                            !title.Equals("Record Video Audio - MultiOS - GMTPC", StringComparison.OrdinalIgnoreCase) &&
                            !windows.Contains(title))
                        {
                            windows.Add(title);
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        return windows;
    }
}
