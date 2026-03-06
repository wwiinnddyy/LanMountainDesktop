using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class BaiduHotSearchSettingsWindow : UserControl
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public BaiduHotSearchSettingsWindow()
    {
        InitializeComponent();
        InitializeFrequencyOptions();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        var sourceType = BaiduHotSearchSourceTypes.Normalize(componentSnapshot.BaiduHotSearchSourceType);
        var enabled = componentSnapshot.BaiduHotSearchAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.BaiduHotSearchAutoRefreshIntervalMinutes);

        _suppressEvents = true;
        SelectSourceType(sourceType);
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("baiduhot.settings.title", "Baidu hot search settings");
        DescriptionTextBlock.Text = L("baiduhot.settings.desc", "Configure source, auto refresh and refresh interval.");
        SourceLabelTextBlock.Text = L("baiduhot.settings.source_label", "Data source");
        SourceOfficialItem.Content = L("baiduhot.settings.source_official", "Official Source");
        SourceThirdPartyRssItem.Content = L("baiduhot.settings.source_rss", "Third-party RSS");
        AutoRefreshLabelTextBlock.Text = L("baiduhot.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("baiduhot.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("baiduhot.settings.frequency_label", "Refresh interval");
        ApplyFrequencyLocalization();
    }

    private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        SaveState();
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
        var snapshot = _componentSettingsService.Load();
        snapshot.BaiduHotSearchSourceType = GetSelectedSourceType();
        snapshot.BaiduHotSearchAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.BaiduHotSearchAutoRefreshIntervalMinutes = GetSelectedInterval();
        _componentSettingsService.Save(snapshot);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedSourceType()
    {
        if (SourceComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string sourceTag)
        {
            return BaiduHotSearchSourceTypes.Normalize(sourceTag);
        }

        return BaiduHotSearchSourceTypes.Official;
    }

    private int GetSelectedInterval()
    {
        if (FrequencyComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagText &&
            int.TryParse(tagText, out var minutes))
        {
            return NormalizeInterval(minutes);
        }

        return 15;
    }

    private void SelectSourceType(string sourceType)
    {
        var normalizedSourceType = BaiduHotSearchSourceTypes.Normalize(sourceType);
        var selected = SourceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string sourceTag &&
                string.Equals(BaiduHotSearchSourceTypes.Normalize(sourceTag), normalizedSourceType, StringComparison.OrdinalIgnoreCase));
        SourceComboBox.SelectedItem = selected ?? SourceComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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
        return RefreshIntervalCatalog.Normalize(minutes, 15);
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
