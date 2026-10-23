using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Taitai.Desktop;
internal static class NativeServices
{
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Bounds rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [StructLayout(LayoutKind.Sequential)] struct Bounds { public int Left, Top, Right, Bottom; }
    public static bool FullscreenOnPetMonitor(Window pet)
    {
        var foreground = GetForegroundWindow(); if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var pid); if (pid == (uint)Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassName(foreground, name, 128);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        // Maximized captioned windows can cover monitor bounds when the taskbar auto-hides.
        if (IsZoomed(foreground) && (GetWindowLong(foreground, -16) & 0x00C00000) != 0) return false;
        var screen = Forms.Screen.FromHandle(foreground);
        if (screen.DeviceName != Forms.Screen.FromHandle(new WindowInteropHelper(pet).Handle).DeviceName) return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var b = screen.Bounds; return rect.Left <= b.Left + 2 && rect.Top <= b.Top + 2 && rect.Right >= b.Right - 2 && rect.Bottom >= b.Bottom - 2;
    }

    public static void ApplyTopmost(Window window, bool value, bool raise = false)
    {
        if (window.Topmost != value) window.Topmost = value;
        if (!window.IsVisible) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var nativeTopmost = (GetWindowLong(hwnd, -20) & 8) != 0;
        if (nativeTopmost != value || (value && raise))
            SetWindowPos(hwnd, new IntPtr(value ? -1 : -2), 0, 0, 0, 0, 0x0213);
    }
}
