using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherWidgetSettingsWindow : UserControl, IComponentPlacementContextAware, IComponentSettingsStoreAware
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly AppSettingsService _appSettingsService = new();
    private IComponentInstanceSettingsStore _componentSettingsStore = new ComponentSettingsService();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";
    private string _componentId = BuiltInComponentIds.DesktopWeather;
    private string _placementId = string.Empty;

    public event EventHandler? SettingsChanged;

    public WeatherWidgetSettingsWindow()
    {
        InitializeComponent();
        InitializeFrequencyOptions();
        LoadState();
        ApplyLocalization();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopWeather
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        LoadState();
        ApplyLocalization();
    }

    public void SetComponentSettingsStore(IComponentInstanceSettingsStore settingsStore)
    {
        _componentSettingsStore = settingsStore ?? new ComponentSettingsService();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        var enabled = componentSnapshot.WeatherAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.WeatherAutoRefreshIntervalMinutes);

        _suppressEvents = true;
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("weather.widget.settings.title", "Weather widget settings");
        DescriptionTextBlock.Text = L("weather.widget.settings.desc", "Configure auto refresh and refresh interval for all weather widgets.");
        AutoRefreshLabelTextBlock.Text = L("weather.widget.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("weather.widget.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("weather.widget.settings.frequency_label", "Refresh interval");
        ApplyFrequencyLocalization();
    }

    private void OnAutoRefreshChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var enabled = AutoRefreshCheckBox.IsChecked == true;
        FrequencyCardBorder.IsVisible = enabled;
        SaveState();
    }

    private void OnFrequencySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        SaveState();
    }

    private void SaveState()
    {
        var snapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
        snapshot.WeatherAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.WeatherAutoRefreshIntervalMinutes = GetSelectedInterval();
        _componentSettingsStore.SaveForComponent(_componentId, _placementId, snapshot);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private int GetSelectedInterval()
    {
        if (FrequencyComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagText &&
            int.TryParse(tagText, out var minutes))
        {
            return NormalizeInterval(minutes);
        }

        return 12;
    }

    private void SelectInterval(int intervalMinutes)
    {
        var selected = FrequencyComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string tagText &&
                int.TryParse(tagText, out var minutes) &&
                minutes == intervalMinutes);
        FrequencyComboBox.SelectedItem = selected ?? FrequencyComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private static int NormalizeInterval(int minutes)
    {
        return RefreshIntervalCatalog.Normalize(minutes, 12);
    }

    private void InitializeFrequencyOptions()
    {
        FrequencyComboBox.Items.Clear();
        foreach (var minutes in SupportedIntervals)
        {
            FrequencyComboBox.Items.Add(new ComboBoxItem
            {
                Tag = minutes.ToString(),
                Content = RefreshIntervalCatalog.ToEnglishFallbackLabel(minutes)
            });
        }
    }

    private void ApplyFrequencyLocalization()
    {
        foreach (var item in FrequencyComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is not string tagText ||
                !int.TryParse(tagText, out var minutes))
            {
                continue;
            }

            var key = $"refresh.frequency.{RefreshIntervalCatalog.ToLocalizationKeySuffix(minutes)}";
            item.Content = L(key, RefreshIntervalCatalog.ToEnglishFallbackLabel(minutes));
        }
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
