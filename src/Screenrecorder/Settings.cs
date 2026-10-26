using System.IO;
using System.Text.Json;

namespace Screenrecorder;

public enum AreaMode { Region, PrimaryScreen, SecondaryScreen, AllScreens }

public sealed class Settings
{
    public AreaMode Area { get; set; } = AreaMode.Region;
    public PixelRect Region { get; set; } = new(200, 200, 1280, 720);
    public string Encoder { get; set; } = "h264_nvenc";
    public int Fps { get; set; } = 60;
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; }
    /// <summary>"" = follow Windows, otherwise "de" or "en".</summary>
    public string Language { get; set; } = "";
    /// <summary>User-defined sizes, e.g. "1600x900".</summary>
    public List<string> CustomSizes { get; set; } = [];

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenrecorder", "settings.json");

    public static string OutputFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screenrecorder");

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

    public static bool TryParseSize(string text, out int width, out int height)
    {
        width = height = 0;
        var parts = text.Trim().ToLowerInvariant().Replace('×', 'x').Split('x', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height)
            && width is >= 16 and <= 16384 && height is >= 16 and <= 16384;
    }
}
