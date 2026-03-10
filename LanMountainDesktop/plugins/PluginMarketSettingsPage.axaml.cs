using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginMarketSettingsPage : UserControl
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private PluginMarketEmbeddedView? _pluginMarketView;

    public PluginMarketSettingsPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => RefreshFromRuntime();
    }

    public void RefreshFromRuntime()
    {
        PluginMarketPanelTitleTextBlock.Text = L("settings.plugin_market.title", "Plugin Market");
        PluginMarketPanelSubtitleTextBlock.Text = L(
            "settings.plugin_market.subtitle",
            "Browse plugins from the official LanAirApp source and stage installs.");

        var runtime = (Application.Current as App)?.PluginRuntimeService;
        if (runtime is null)
        {
            PluginMarketContentHost.Content = CreateUnavailableState();
            return;
        }

        if (_pluginMarketView is null)
        {
            _pluginMarketView = new PluginMarketEmbeddedView(runtime);
        }

        _pluginMarketView.RefreshLocalization();
        _pluginMarketView.RefreshInstalledSnapshot();

        if (!ReferenceEquals(PluginMarketContentHost.Content, _pluginMarketView))
        {
            PluginMarketContentHost.Content = _pluginMarketView;
        }
    }

    private Control CreateUnavailableState()
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14000000")),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = L(
                    "settings.plugin_market.unavailable",
                    "Plugin runtime is not available, so the official market cannot be opened right now."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = PluginMarketPanelSubtitleTextBlock.Foreground
            }
        };
    }

    private string L(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        return _localizationService.GetString(snapshot.LanguageCode, key, fallback);
    }
}
