using System.Resources;
using System.Runtime.CompilerServices;

namespace Screenrecorder.Resources;

/// <summary>Typed access to Strings.resx (en) / Strings.de.resx; property name = resource key.</summary>
public static class Strings
{
    static readonly ResourceManager Manager = new("Screenrecorder.Resources.Strings", typeof(Strings).Assembly);

    static string Get([CallerMemberName] string key = "") => Manager.GetString(key) ?? key;

    public static string Menu => Get();
    public static string Record => Get();
    public static string Stop => Get();
    public static string Area => Get();
    public static string Open => Get();
    public static string Codecs => Get();
    public static string Sound => Get();
    public static string AllScreens => Get();
    public static string PrimaryScreen => Get();
    public static string SecondaryScreen => Get();
    public static string SelectArea => Get();
    public static string ResizeArea => Get();
    public static string AddArea => Get();
    public static string EditAreas => Get();
    public static string EditAreasPrompt => Get();
    public static string SizePrompt => Get();
    public static string InvalidSize => Get();
    public static string SystemSound => Get();
    public static string Microphone => Get();
    public static string DetectingCodecs => Get();
    public static string NoCodecs => Get();
    public static string FfmpegMissing => Get();
    public static string RecordingFailed => Get();
    public static string NoSecondScreen => Get();
    public static string Language => Get();
    public static string LanguageSystem => Get();
    public static string RestartHint => Get();
    public static string Ok => Get();
    public static string Cancel => Get();
    public static string HotkeyHint => Get();
    public static string Recording => Get();
    public static string Settings => Get();
    public static string Pause => Get();
    public static string Resume => Get();
    public static string Paused => Get();
    public static string HotkeyTaken => Get();
    public static string CategoryRecording => Get();
    public static string CategoryHotkeys => Get();
    public static string CategoryOutput => Get();
    public static string CategoryAppearance => Get();
    public static string CategoryInfo => Get();
    public static string Quality => Get();
    public static string QualitySmaller => Get();
    public static string QualityBetter => Get();
    public static string QualityHint => Get();
    public static string ShowCursor => Get();
    public static string HotkeyRecordLabel => Get();
    public static string HotkeyPauseLabel => Get();
    public static string HotkeySelectAreaLabel => Get();
    public static string HotkeyHelp => Get();
    public static string HotkeyDuplicate => Get();
    public static string OutputFolder => Get();
    public static string FileName => Get();
    public static string StartNumber => Get();
    public static string FileNamePreview => Get();
    public static string InvalidFolder => Get();
    public static string InvalidStartNumber => Get();
    public static string TokenYear => Get();
    public static string TokenMonth => Get();
    public static string TokenDay => Get();
    public static string TokenHour => Get();
    public static string TokenMinute => Get();
    public static string TokenSecond => Get();
    public static string TokenMillisecond => Get();
    public static string TokenNumber => Get();
    public static string Theme => Get();
    public static string ThemeLight => Get();
    public static string ThemeDark => Get();
    public static string Reset => Get();
    public static string Version => Get();
    public static string FfmpegInUse => Get();
}
