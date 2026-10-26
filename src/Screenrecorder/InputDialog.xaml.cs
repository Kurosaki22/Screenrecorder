using System.Windows;

namespace Screenrecorder;

public partial class InputDialog : Window
{
    InputDialog() => InitializeComponent();

    /// <summary>Returns the entered text, or null when cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt, string text, bool multiline = false)
    {
        var d = new InputDialog { Owner = owner, Title = title };
        d.Prompt.Text = prompt;
        d.Input.Text = text;
        if (multiline)
        {
            d.Input.AcceptsReturn = true;
            d.Input.Height = 160;
            d.Input.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        }
        d.Input.SelectAll();
        return d.ShowDialog() == true ? d.Input.Text : null;
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
