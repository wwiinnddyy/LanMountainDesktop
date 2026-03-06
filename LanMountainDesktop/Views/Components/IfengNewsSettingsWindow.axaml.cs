using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class IfengNewsSettingsWindow : UserControl
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public IfengNewsSettingsWindow()
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

        var channelType = IfengNewsChannelTypes.Normalize(componentSnapshot.IfengNewsChannelType);
        var enabled = componentSnapshot.IfengNewsAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.IfengNewsAutoRefreshIntervalMinutes);

        _suppressEvents = true;
        SelectChannelType(channelType);
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("ifeng.settings.title", "iFeng news settings");
        DescriptionTextBlock.Text = L("ifeng.settings.desc", "Configure channel, auto refresh and refresh interval.");
        ChannelLabelTextBlock.Text = L("ifeng.settings.channel_label", "News channel");
        ChannelComprehensiveItem.Content = L("ifeng.settings.channel_comprehensive", "Comprehensive");
        ChannelMainlandItem.Content = L("ifeng.settings.channel_mainland", "China Mainland");
        ChannelTaiwanItem.Content = L("ifeng.settings.channel_taiwan", "Taiwan");
        AutoRefreshLabelTextBlock.Text = L("ifeng.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("ifeng.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("ifeng.settings.frequency_label", "Refresh interval");
        ApplyFrequencyLocalization();
    }

    private void OnChannelSelectionChanged(object? sender, SelectionChangedEventArgs e)
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
        snapshot.IfengNewsChannelType = GetSelectedChannelType();
        snapshot.IfengNewsAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.IfengNewsAutoRefreshIntervalMinutes = GetSelectedInterval();
        _componentSettingsService.Save(snapshot);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedChannelType()
    {
        if (ChannelComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string channelTag)
        {
            return IfengNewsChannelTypes.Normalize(channelTag);
        }

        return IfengNewsChannelTypes.Comprehensive;
    }

    private int GetSelectedInterval()
    {
        if (FrequencyComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagText &&
            int.TryParse(tagText, out var minutes))
        {
            return NormalizeInterval(minutes);
        }

        return 20;
    }

    private void SelectChannelType(string channelType)
    {
        var normalizedChannelType = IfengNewsChannelTypes.Normalize(channelType);
        var selected = ChannelComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string channelTag &&
                string.Equals(IfengNewsChannelTypes.Normalize(channelTag), normalizedChannelType, StringComparison.OrdinalIgnoreCase));
        ChannelComboBox.SelectedItem = selected ?? ChannelComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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
        return RefreshIntervalCatalog.Normalize(minutes, 20);
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
