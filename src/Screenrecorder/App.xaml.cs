using System.Globalization;
using System.Windows;
using Microsoft.Win32;

namespace Screenrecorder;

public partial class App : Application
{
    static ThemeChoice current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = Settings.Load();
        if (settings.Language != "")
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = new CultureInfo(settings.Language);
        ApplyTheme(settings.Theme);
        // "Windows setting" follows the system live: Fluent does it by itself, our palette needs a nudge.
        SystemEvents.UserPreferenceChanged += (_, _) => Current.Dispatcher.Invoke(() => ApplyTheme(current));
        // Explicit: the frame and mini bar are created first and would otherwise become MainWindow (ShutdownMode).
        MainWindow = new MainWindow(settings);
        MainWindow.Show();
    }

    public static void ApplyTheme(ThemeChoice theme)
    {
        current = theme;
        Current.ThemeMode = theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        var light = theme == ThemeChoice.Light || theme == ThemeChoice.System && SystemUsesLightTheme();
        // Found by source: ThemeMode may add its own dictionaries to the same list.
        var dictionaries = Current.Resources.MergedDictionaries;
        var palette = dictionaries.First(d => d.Source?.OriginalString.StartsWith("Themes/") == true);
        dictionaries[dictionaries.IndexOf(palette)] =
            new ResourceDictionary { Source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative) };
    }

    static bool SystemUsesLightTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1;
}
