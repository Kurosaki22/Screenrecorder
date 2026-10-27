using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Screenrecorder;

/// <summary>OBS-style mixer: per source an enable box, a live level meter and a volume slider with boost.</summary>
public partial class AudioMixer : UserControl
{
    const double FallDbPerSecond = 20, PeakHoldSeconds = 1, TickSeconds = 0.03;

    /// <summary>One source's controls and meter state.</summary>
    sealed class Row(AudioChannel channel, CheckBox box, Slider slider, TextBlock value, Grid meter, Rectangle mask, Rectangle peak)
    {
        public readonly AudioChannel Channel = channel;
        public readonly CheckBox Box = box;
        public readonly Slider Slider = slider;
        public readonly TextBlock Value = value;
        public readonly Grid Meter = meter;
        public readonly Rectangle Mask = mask, Peak = peak;
        public double LevelDb = AudioLevel.FloorDb, HoldDb = AudioLevel.FloorDb;
        public DateTime HoldSince;
    }

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(TickSeconds) };
    Row[] rows = [];

    /// <summary>Raised when a source is switched on or off.</summary>
    public event Action? SourcesChanged;

    public AudioMixer()
    {
        InitializeComponent();
        timer.Tick += (_, _) => Array.ForEach(rows, UpdateMeter);
    }

    /// <summary>settings is a getter because the main window swaps its Settings instance after the options dialog.</summary>
    public void Bind(Func<Settings> settings, AudioChannel system, AudioChannel mic)
    {
        rows =
        [
            new Row(system, SystemBox, SystemSlider, SystemValue, SystemMeter, SystemMask, SystemPeak),
            new Row(mic, MicBox, MicSlider, MicValue, MicMeter, MicMask, MicPeak),
        ];
        var s = settings();
        Setup(rows[0], s.SystemAudio, s.SystemGainDb, on => settings().SystemAudio = on, db => settings().SystemGainDb = db);
        Setup(rows[1], s.Microphone, s.MicGainDb, on => settings().Microphone = on, db => settings().MicGainDb = db);
    }

    void Setup(Row row, bool enabled, double gainDb, Action<bool> setEnabled, Action<double> setGain)
    {
        row.Box.IsChecked = enabled;
        row.Box.Click += (_, _) =>
        {
            setEnabled(row.Box.IsChecked == true);
            SourcesChanged?.Invoke();
        };
        void Show(double db) => row.Value.Text = $"{(db > 0 ? "+" : "")}{db:0.0} dB ({AudioLevel.DbToGain(db) * 100:0} %)";
        row.Slider.ValueChanged += (_, e) =>
        {
            setGain(e.NewValue);
            row.Channel.Gain = AudioLevel.DbToGain(e.NewValue);
            Show(e.NewValue);
        };
        row.Slider.MouseDoubleClick += (_, _) => row.Slider.Value = 0;
        row.Slider.Value = gainDb;
        Show(gainDb);
    }

    /// <summary>Sources can't be added or dropped mid-recording; volume stays adjustable.</summary>
    public void SetRecording(bool recording)
    {
        foreach (var row in rows) row.Box.IsEnabled = !recording;
    }

    public void Start() => timer.Start();

    public void Stop()
    {
        timer.Stop();
        foreach (var row in rows)
        {
            row.LevelDb = row.HoldDb = AudioLevel.FloorDb;
            UpdateMeter(row);
        }
    }

    void UpdateMeter(Row row)
    {
        var now = DateTime.UtcNow;
        var db = AudioLevel.PeakToDb(row.Channel.TakePeak());
        row.LevelDb = Math.Max(db, row.LevelDb - FallDbPerSecond * TickSeconds);
        if (db >= row.HoldDb) (row.HoldDb, row.HoldSince) = (db, now);
        else if ((now - row.HoldSince).TotalSeconds > PeakHoldSeconds)
            row.HoldDb = Math.Max(row.LevelDb, row.HoldDb - FallDbPerSecond * TickSeconds);

        var width = row.Meter.ActualWidth;
        row.Mask.Width = width * (1 - Fraction(row.LevelDb));
        row.Peak.Margin = new Thickness(Math.Max(0, width * Fraction(row.HoldDb) - 2), 0, 0, 0);
        row.Peak.Visibility = row.HoldDb > AudioLevel.FloorDb ? Visibility.Visible : Visibility.Hidden;
    }

    static double Fraction(double db) => (db - AudioLevel.FloorDb) / -AudioLevel.FloorDb;
}
