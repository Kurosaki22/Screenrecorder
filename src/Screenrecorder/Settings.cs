using System.IO;
using System.Text.Json;

namespace Screenrecorder;

public enum AreaMode { Region, PrimaryScreen, SecondaryScreen, AllScreens, Window }

public enum ThemeChoice { System, Light, Dark }

public sealed class Settings
{
    public AreaMode Area { get; set; } = AreaMode.Region;
    public PixelRect Region { get; set; } = new(200, 200, 1280, 720);
    public string Encoder { get; set; } = "h264_nvenc";
    public int Fps { get; set; } = 60;
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    /// <summary>Mixer volume per source in dB (−40 … +12).</summary>
    public double SystemGainDb { get; set; }
    public double MicGainDb { get; set; }
    /// <summary>"" = follow Windows, otherwise "de" or "en".</summary>
    public string Language { get; set; } = "";
    /// <summary>User-defined sizes, e.g. "1600x900".</summary>
    public List<string> CustomSizes { get; set; } = [];
    /// <summary>1 = smallest file … 100 = best quality; see VideoEncoder.QualityArgs.</summary>
    public int Quality { get; set; } = 80;
    public bool ShowCursor { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public string HotkeyRecord { get; set; } = "F2";
    public string HotkeyPause { get; set; } = "F3";
    public string HotkeySelectArea { get; set; } = "F4";
    // F8: F5/F6 would globally take reload / address bar away from browsers while the app runs.
    public string HotkeyScreenshot { get; set; } = "F8";
    /// <summary>3-2-1 countdown before a recording starts.</summary>
    public bool Countdown { get; set; }
    /// <summary>Stop automatically after this many minutes / megabytes; 0 = no limit.</summary>
    public int LimitMinutes { get; set; }
    public int LimitMb { get; set; }
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public string FileNamePattern { get; set; } = FileNamer.DefaultPattern;
    /// <summary>Value of the &lt;Nr&gt; placeholder for the next recording.</summary>
    public int NextNumber { get; set; } = 1;

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenrecorder", "settings.json");

    public static string DefaultOutputFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screenrecorder");

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(); // missing or corrupt file: start with defaults
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Frame rates offered in the settings; any whole number 1–240 may be typed in.</summary>
    public static readonly int[] FpsPresets = [24, 25, 30, 48, 50, 60, 72, 90, 100, 120, 144, 165, 240];

    public static bool TryParseFps(string text, out int fps) =>
        int.TryParse(text.Trim(), out fps) && fps is >= 1 and <= 240;

    public static bool TryParseSize(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Trim().ToLowerInvariant().Replace('×', 'x').Split('x', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width is >= 16 and <= 16384 && height is >= 16 and <= 16384;
    }
}
