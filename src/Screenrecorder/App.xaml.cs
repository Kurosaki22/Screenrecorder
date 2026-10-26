using System.Globalization;
using System.Windows;

namespace Screenrecorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = Settings.Load();
        if (settings.Language != "")
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = new CultureInfo(settings.Language);
        new MainWindow(settings).Show();
    }
}
