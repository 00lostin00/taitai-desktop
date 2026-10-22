using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace Taitai.Desktop;
public sealed class AppSettings
{
    public bool Topmost { get; set; } = true;
    public string LayerMode { get; set; } = "follow";
    public bool IdleInteraction { get; set; } = true;
    public string InteractionFrequency { get; set; } = "normal";
    public bool FullscreenAvoidance { get; set; } = true;
    public bool GlobalHotkey { get; set; } = true;
    public bool ResetNotice { get; set; }
    public bool StartAtLogin { get; set; }
    public bool BatterySaver { get; set; }
    public bool ShowChatStatus { get; set; } = true;
    public string? ChatUrl { get; set; }
    public string? ChatTitle { get; set; }
    public string PetName { get; set; } = "苔苔";
    public bool ShowName { get; set; } = true;
    public bool FollowPointer { get; set; } = true;
    public bool LowQuotaNotice { get; set; } = true;
    public bool EdgeTuck { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public double Scale { get; set; } = 1;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string? CodexBinary { get; set; }
    public HashSet<string> NotifiedWindows { get; set; } = new();
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaitaiDesktop");
    static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public static AppSettings Load()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            value.Scale = double.IsFinite(value.Scale) ? Math.Clamp(value.Scale, .75, 1.4) : 1;
            value.PetName = string.IsNullOrWhiteSpace(value.PetName) ? "苔苔" : value.PetName.Trim()[..Math.Min(12, value.PetName.Trim().Length)];
            value.NotifiedWindows ??= new(); return value;
        }
        catch { return new(); }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(DataDirectory); var tmp = FilePath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(tmp, FilePath, true); } catch { }
    }
}
