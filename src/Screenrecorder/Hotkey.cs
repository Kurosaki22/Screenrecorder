using System.Windows.Input;

namespace Screenrecorder;

/// <summary>Hotkeys stored as text like "F2" or "Ctrl+Shift+F5".</summary>
public static class Hotkey
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                default:
                    if (key != Key.None || !Enum.TryParse(part, ignoreCase: true, out key) || IsModifier(key)) return false;
                    break;
            }
        }
        return key != Key.None;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Keys that can't be a hotkey on their own: modifiers and the lock keys.</summary>
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System
        or Key.NumLock or Key.CapsLock or Key.Scroll;

    /// <summary>Arguments for Win32 RegisterHotKey.</summary>
    public static (uint modifiers, uint vk) ToWin32(ModifierKeys modifiers, Key key)
    {
        uint m = MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) m |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) m |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) m |= MOD_SHIFT;
        return (m, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}
