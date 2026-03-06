using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class BilibiliHotSearchSettingsWindow : UserControl
{
    private static readonly int[] SupportedIntervals = [5, 10, 15, 30, 60, 180];

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public BilibiliHotSearchSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        var enabled = componentSnapshot.BilibiliHotSearchAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.BilibiliHotSearchAutoRefreshIntervalMinutes);

        _suppressEvents = true;
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("bilihot.settings.title", "Bilibili hot search settings");
        DescriptionTextBlock.Text = L("bilihot.settings.desc", "Configure auto refresh and refresh interval.");
        AutoRefreshLabelTextBlock.Text = L("bilihot.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("bilihot.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("bilihot.settings.frequency_label", "Refresh interval");
        Frequency5mItem.Content = L("bilihot.settings.frequency_5m", "5 min");
        Frequency10mItem.Content = L("bilihot.settings.frequency_10m", "10 min");
        Frequency15mItem.Content = L("bilihot.settings.frequency_15m", "15 min");
        Frequency30mItem.Content = L("bilihot.settings.frequency_30m", "30 min");
        Frequency1hItem.Content = L("bilihot.settings.frequency_1h", "1 hour");
        Frequency3hItem.Content = L("bilihot.settings.frequency_3h", "3 hours");
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
        snapshot.BilibiliHotSearchAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.BilibiliHotSearchAutoRefreshIntervalMinutes = GetSelectedInterval();
        _componentSettingsService.Save(snapshot);
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

        return 15;
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
        if (minutes <= 0)
        {
            return 15;
        }

        if (SupportedIntervals.Contains(minutes))
        {
            return minutes;
        }

        return SupportedIntervals
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(15);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
