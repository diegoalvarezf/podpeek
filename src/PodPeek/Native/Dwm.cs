using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PodPeek.Native;

/// <summary>Efectos de ventana de Windows 11: fondo translúcido con desenfoque, esquinas redondeadas y modo oscuro.</summary>
static class Dwm
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWCP_ROUND = 2;
    const int DWMSBT_TRANSIENTWINDOW = 3; // acrílico: el desenfoque de los menús y popups de Windows 11

    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>El acrílico de sistema llegó con Windows 11 22H2 (compilación 22621).</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static void Apply(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        if (IsWindows11)
        {
            value = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }
        if (SupportsBackdrop)
        {
            value = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
        }
    }

    /// <summary>Fuera de Alt+Tab y sin robar el foco a lo que estés haciendo.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool AppsUseDarkTheme => ReadThemeFlag("AppsUseLightTheme") == 0;
    public static bool TaskbarIsDark => ReadThemeFlag("SystemUsesLightTheme") == 0;

    static int ReadThemeFlag(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Personalize);
        return key?.GetValue(name) is int v ? v : 1;
    }
}
