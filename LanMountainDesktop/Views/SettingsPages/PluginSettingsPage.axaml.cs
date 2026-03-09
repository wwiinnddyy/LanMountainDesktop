using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginSettingsPage : UserControl
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();

    public PluginSettingsPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => RefreshFromRuntime();
    }

    public void RefreshFromRuntime()
    {
        var runtime = (Application.Current as App)?.PluginRuntimeService;
        if (runtime is null)
        {
            PluginSystemStatusTextBlock.Text = L("settings.plugins.runtime_unavailable", "Plugin runtime is not available.");
            PluginRuntimeSummaryPanel.Children.Clear();
            PluginCatalogItemsHost.Children.Clear();
            PluginRestartHintTextBlock.IsVisible = false;
            return;
        }

        BuildRuntimeSummary(runtime);
        BuildPluginCatalog(runtime);
    }

    private void BuildRuntimeSummary(PluginRuntimeService runtime)
    {
        var failures = runtime.LoadResults.Where(result => !result.IsSuccess).ToArray();
        var enabledCount = runtime.Catalog.Count(entry => entry.IsEnabled);
        PluginSystemStatusTextBlock.Text = F(
            "settings.plugins.summary_format",
            "Detected {0} plugin(s); enabled {1}; loaded {2}; settings pages {3}; widgets {4}; failures {5}.",
            runtime.Catalog.Count,
            enabledCount,
            runtime.LoadedPlugins.Count,
            runtime.SettingsPages.Count,
            runtime.DesktopComponents.Count,
            failures.Length);

        PluginRuntimeSummaryPanel.Children.Clear();
        foreach (var plugin in runtime.Catalog.OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase))
        {
            var status = plugin.IsEnabled
                ? plugin.IsLoaded
                    ? L("settings.plugins.state.enabled", "Enabled")
                    : L("settings.plugins.state.enabled_failed", "Enabled / failed to load")
                : L("settings.plugins.state.disabled", "Disabled");
            PluginRuntimeSummaryPanel.Children.Add(CreateSummaryLine(
                F(
                    "settings.plugins.summary_item_format",
                    "{0}  v{1}  |  {2}",
                    plugin.Manifest.Name,
                    plugin.Manifest.Version ?? "dev",
                    status)));
        }
    }

    private void BuildPluginCatalog(PluginRuntimeService runtime)
    {
        PluginCatalogItemsHost.Children.Clear();

        var plugins = runtime.Catalog
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PluginCatalogEmptyTextBlock.IsVisible = plugins.Count == 0;
        PluginRestartHintTextBlock.IsVisible = plugins.Count > 0;

        foreach (var plugin in plugins)
        {
            PluginCatalogItemsHost.Children.Add(CreatePluginCatalogItem(runtime, plugin));
        }
    }

    private Control CreatePluginCatalogItem(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        var title = new TextBlock
        {
            Text = entry.Manifest.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var subtitle = new TextBlock
        {
            Text = BuildPluginSubtitle(entry),
            Foreground = PluginSystemDescriptionTextBlock.Foreground,
            TextWrapping = TextWrapping.Wrap
        };

        var enabledToggle = new ToggleSwitch
        {
            IsChecked = entry.IsEnabled,
            OnContent = L("settings.plugins.toggle_on", "Enabled"),
            OffContent = L("settings.plugins.toggle_off", "Disabled"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        enabledToggle.Checked += (_, _) => OnPluginEnableChanged(runtime, entry, true);
        enabledToggle.Unchecked += (_, _) => OnPluginEnableChanged(runtime, entry, false);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children = { title, subtitle }
                },
                enabledToggle
            }
        };
        Grid.SetColumn(enabledToggle, 1);

        var details = new TextBlock
        {
            Text = BuildPluginDetails(entry),
            Foreground = PluginSystemDescriptionTextBlock.Foreground,
            TextWrapping = TextWrapping.Wrap
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14000000")),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { header, details }
            }
        };
    }

    private void OnPluginEnableChanged(PluginRuntimeService runtime, PluginCatalogEntry entry, bool isEnabled)
    {
        runtime.SetPluginEnabled(entry.Manifest.Id, isEnabled);
        BuildRuntimeSummary(runtime);
        BuildPluginCatalog(runtime);
        PluginSystemStatusTextBlock.Text = F(
            "settings.plugins.toggle_result_format",
            "Plugin '{0}' was {1} for the next launch. Restart the app to apply page and widget changes.",
            entry.Manifest.Name,
            isEnabled
                ? L("settings.plugins.toggle_state_enabled", "enabled")
                : L("settings.plugins.toggle_state_disabled", "disabled"));
    }

    private string BuildPluginSubtitle(PluginCatalogEntry entry)
    {
        var source = entry.IsPackage
            ? L("settings.plugins.source_package", ".laapp package")
            : L("settings.plugins.source_manifest", "Loose manifest");
        var state = entry.IsEnabled
            ? entry.IsLoaded
                ? L("settings.plugins.state.loaded", "Loaded")
                : L("settings.plugins.state.load_failed", "Load failed")
            : L("settings.plugins.state.disabled", "Disabled");
        return F(
            "settings.plugins.subtitle_format",
            "{0} | {1} | {2}",
            state,
            source,
            entry.Manifest.Id);
    }

    private string BuildPluginDetails(PluginCatalogEntry entry)
    {
        var detail = F(
            "settings.plugins.detail_format",
            "Settings pages: {0} | Widgets: {1}",
            entry.SettingsPageCount,
            entry.WidgetCount);
        return string.IsNullOrWhiteSpace(entry.ErrorMessage)
            ? detail
            : detail + Environment.NewLine + entry.ErrorMessage;
    }

    private TextBlock CreateSummaryLine(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = PluginSystemDescriptionTextBlock.Foreground
        };
    }

    private string L(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        return _localizationService.GetString(snapshot.LanguageCode, key, fallback);
    }

    private string F(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args);
    }
}




