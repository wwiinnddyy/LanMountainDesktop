using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirApp.ComponentTemplate;

/// <summary>
/// Desktop component widget implementation.
/// </summary>
public sealed class MyWidget : AirAppWidgetBase
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _timeText;
    private readonly DispatcherTimer _timer;

    public MyWidget()
    {
        // Create UI
        _titleText = new TextBlock
        {
            Text = "My Widget",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _timeText = new TextBlock
        {
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_titleText);
        panel.Children.Add(_timeText);

        Content = panel;

        // Setup timer to update time
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => UpdateTime();
    }

    protected override void OnAttachedCore()
    {
        Context.Logger.Info($"Widget attached: {Context.ComponentId} at {Context.PlacementId}");
        UpdateTime();
        _timer.Start();
    }

    protected override void OnDetachedCore()
    {
        Context.Logger.Info($"Widget detached: {Context.ComponentId}");
        _timer.Stop();
    }

    protected override void OnAppearanceChangedCore(AirAppAppearanceSnapshot snapshot)
    {
        // Respond to theme changes
        _titleText.Foreground = new SolidColorBrush(snapshot.ForegroundColor);
        _timeText.Foreground = new SolidColorBrush(snapshot.AccentColor);

        Context.Logger.Info($"Appearance changed: DarkMode={snapshot.IsDarkMode}");
    }

    private void UpdateTime()
    {
        _timeText.Text = DateTime.Now.ToString("HH:mm:ss");
    }
}
