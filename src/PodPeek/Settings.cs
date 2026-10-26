using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PodPeek;

/// <summary>Preferencias en %APPDATA%\PodPeek\settings.json.</summary>
sealed class Settings
{
    public bool PauseOnRemove { get; set; } = true;
    public bool AutoDefaultDevice { get; set; } = true;
    public bool ShowOnConnect { get; set; } = true;
    public int LowBatteryPercent { get; set; } = 20;

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodPeek");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Arranque con Windows: entrada en HKCU\...\Run (solo para este usuario, sin permisos de administrador)
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("PodPeek") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("PodPeek", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("PodPeek", throwOnMissingValue: false);
        }
    }
}

static class Log
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PodPeek");
    static readonly string FilePath = Path.Combine(Dir, "podpeek.log");
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
