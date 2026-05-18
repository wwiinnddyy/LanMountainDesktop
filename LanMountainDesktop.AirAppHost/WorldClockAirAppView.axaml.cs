using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class WorldClockAirAppView : UserControl
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly AirAppLaunchOptions _options;

    public WorldClockAirAppView()
        : this(AirAppLaunchOptions.Parse([]))
    {
    }

    public WorldClockAirAppView(AirAppLaunchOptions options)
    {
        _options = options;
        InitializeComponent();

        SessionTextBlock.Text = string.IsNullOrWhiteSpace(_options.SourcePlacementId)
            ? "World Clock"
            : $"World Clock / {_options.SourcePlacementId}";

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += (_, _) =>
        {
            UpdateTime();
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        UpdateTime();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateTime();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        TimeTextBlock.Text = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        DateTextBlock.Text = now.ToString("yyyy-MM-dd dddd", CultureInfo.CurrentCulture);
        TimeZoneTextBlock.Text = TimeZoneInfo.Local.DisplayName;
    }
}
