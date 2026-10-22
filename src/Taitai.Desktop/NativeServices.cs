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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [StructLayout(LayoutKind.Sequential)] struct Bounds { public int Left, Top, Right, Bottom; }
    public static bool FullscreenOnPetMonitor(Window pet)
    {
        var foreground = GetForegroundWindow(); if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var pid); if (pid == (uint)Environment.ProcessId) return false;
        var name = new StringBuilder(128); GetClassName(foreground, name, 128);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        var screen = Forms.Screen.FromHandle(foreground);
        if (screen.DeviceName != Forms.Screen.FromHandle(new WindowInteropHelper(pet).Handle).DeviceName) return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var b = screen.Bounds; return rect.Left <= b.Left + 2 && rect.Top <= b.Top + 2 && rect.Right >= b.Right - 2 && rect.Bottom >= b.Bottom - 2;
    }
}
