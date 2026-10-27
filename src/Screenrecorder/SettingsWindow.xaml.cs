using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Screenrecorder.Resources;

namespace Screenrecorder;

/// <summary>Options dialog in the style of oCam's settings. Edits a copy; Result is valid after OK.</summary>
public partial class SettingsWindow : Window
{
    static readonly (string code, string name)[] Languages = [("", Strings.LanguageSystem), ("de", "Deutsch"), ("en", "English")];

    public Settings Result { get; }

    public SettingsWindow(Settings copy, string? ffmpegPath)
    {
        Result = copy;
        InitializeComponent();
        foreach (var (_, name) in Languages) LanguageBox.Items.Add(name);
        foreach (var fps in Settings.FpsPresets) FpsBox.Items.Add(fps.ToString());
        VersionText.Text = string.Format(Strings.Version, typeof(App).Assembly.GetName().Version?.ToString(3));
        FfmpegText.Text = string.Format(Strings.FfmpegInUse, ffmpegPath ?? "–");
        LicenseBox.Text = Licenses();
        Load(copy);
    }

    void Load(Settings s)
    {
        QualitySlider.Value = s.Quality;
        FpsBox.Text = s.Fps.ToString();
        ShowCursorBox.IsChecked = s.ShowCursor;
        CountdownBox.IsChecked = s.Countdown;
        LimitMinutesBox.Text = s.LimitMinutes.ToString();
        LimitMbBox.Text = s.LimitMb.ToString();
        HotkeyRecordBox.Text = s.HotkeyRecord;
        HotkeyPauseBox.Text = s.HotkeyPause;
        HotkeySelectAreaBox.Text = s.HotkeySelectArea;
        HotkeyScreenshotBox.Text = s.HotkeyScreenshot;
        OutputFolderBox.Text = s.OutputFolder;
        FileNameBox.Text = s.FileNamePattern;
        StartNumberBox.Text = s.NextNumber.ToString();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.code == s.Language));
        (s.Theme switch { ThemeChoice.Light => ThemeLight, ThemeChoice.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
    }

    void Categories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pages is null) return; // fires during InitializeComponent
        for (var i = 0; i < Pages.Children.Count; i++)
            Pages.Children[i].Visibility = i == Categories.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        QualityValue.Text = $"{(int)Math.Round(e.NewValue)} %";

    /// <summary>Captures the pressed key combination instead of typing text.</summary>
    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Escape or Key.Tab) return; // keep cancel and focus navigation working
        e.Handled = true;
        if (key is Key.Delete or Key.Back)
            box.Text = "";
        else if (!Hotkey.IsModifier(key))
            box.Text = Hotkey.Format(Keyboard.Modifiers, key);
    }

    void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(OutputFolderBox.Text) ? OutputFolderBox.Text : "" };
        if (dialog.ShowDialog(this) == true) OutputFolderBox.Text = dialog.FolderName;
    }

    void Tokens_Click(object sender, RoutedEventArgs e)
    {
        string[] names = [Strings.TokenYear, Strings.TokenMonth, Strings.TokenDay, Strings.TokenHour, Strings.TokenMinute,
            Strings.TokenSecond, Strings.TokenMillisecond, Strings.TokenNumber];
        var menu = new ContextMenu { PlacementTarget = TokenButton, Placement = PlacementMode.Bottom };
        for (var i = 0; i < FileNamer.Tokens.Length; i++)
        {
            var token = FileNamer.Tokens[i];
            var item = new MenuItem { Header = $"{token}  {names[i]}" };
            item.Click += (_, _) =>
            {
                var caret = FileNameBox.CaretIndex;
                FileNameBox.Text = FileNameBox.Text.Insert(caret, token);
                FileNameBox.CaretIndex = caret + token.Length;
                FileNameBox.Focus();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void FileName_Changed(object sender, TextChangedEventArgs e)
    {
        if (FileNamePreview is null) return; // fires during InitializeComponent
        var number = int.TryParse(StartNumberBox.Text, out var n) ? n : 1;
        FileNamePreview.Text = string.Format(Strings.FileNamePreview, FileNamer.Format(FileNameBox.Text, DateTime.Now, number) + ".mp4");
    }

    void Reset_Click(object sender, RoutedEventArgs e) => Load(new Settings());

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        var hotkeys = new[] { HotkeyRecordBox.Text, HotkeyPauseBox.Text, HotkeySelectAreaBox.Text, HotkeyScreenshotBox.Text };
        var duplicate = hotkeys.Where(h => h != "").GroupBy(h => h).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            Fail(1, string.Format(Strings.HotkeyDuplicate, duplicate.Key));
            return;
        }
        if (!Settings.TryParseFps(FpsBox.Text, out var fps))
        {
            Fail(0, string.Format(Strings.InvalidFps, FpsBox.Text));
            return;
        }
        if (!int.TryParse(LimitMinutesBox.Text, out var limitMinutes) || limitMinutes < 0
            || !int.TryParse(LimitMbBox.Text, out var limitMb) || limitMb < 0)
        {
            Fail(0, Strings.InvalidLimit);
            return;
        }
        if (!int.TryParse(StartNumberBox.Text, out var number) || number < 0)
        {
            Fail(2, Strings.InvalidStartNumber);
            return;
        }
        try
        {
            Directory.CreateDirectory(OutputFolderBox.Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Fail(2, $"{Strings.InvalidFolder}{Environment.NewLine}{ex.Message}");
            return;
        }

        Result.Quality = (int)Math.Round(QualitySlider.Value);
        Result.Fps = fps;
        Result.ShowCursor = ShowCursorBox.IsChecked == true;
        Result.Countdown = CountdownBox.IsChecked == true;
        (Result.LimitMinutes, Result.LimitMb) = (limitMinutes, limitMb);
        (Result.HotkeyRecord, Result.HotkeyPause, Result.HotkeySelectArea, Result.HotkeyScreenshot) = (hotkeys[0], hotkeys[1], hotkeys[2], hotkeys[3]);
        Result.OutputFolder = OutputFolderBox.Text;
        Result.FileNamePattern = FileNameBox.Text.Trim() == "" ? FileNamer.DefaultPattern : FileNameBox.Text;
        Result.NextNumber = number;
        Result.Language = Languages[Math.Max(0, LanguageBox.SelectedIndex)].code;
        Result.Theme = ThemeLight.IsChecked == true ? ThemeChoice.Light : ThemeDark.IsChecked == true ? ThemeChoice.Dark : ThemeChoice.System;
        DialogResult = true;
    }

    void Fail(int category, string message)
    {
        Categories.SelectedIndex = category;
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>License texts embedded at build time (LGPL requires shipping them with the program).</summary>
    static string Licenses()
    {
        var assembly = typeof(App).Assembly;
        var texts = new[] { "LICENSE", "THIRD-PARTY-NOTICES.md", "FFMPEG-VERSION.txt", "FFMPEG-LICENSE.txt" }
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name);
                return stream is null ? null : $"===== {name} ====={Environment.NewLine}{new StreamReader(stream).ReadToEnd().Trim()}";
            })
            .Where(t => t is not null);
        return string.Join(Environment.NewLine + Environment.NewLine, texts);
    }
}
