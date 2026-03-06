using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailyWordSettingsWindow : UserControl
{
    private static readonly int[] SupportedIntervals = [30, 60, 180, 360, 720, 1440];

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public DailyWordSettingsWindow()
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

        var enabled = componentSnapshot.DailyWordAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.DailyWordAutoRefreshIntervalMinutes);

        _suppressEvents = true;
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("dailyword.settings.title", "Daily word settings");
        DescriptionTextBlock.Text = L("dailyword.settings.desc", "Configure auto refresh and refresh interval.");
        AutoRefreshLabelTextBlock.Text = L("dailyword.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("dailyword.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("dailyword.settings.frequency_label", "Refresh interval");
        Frequency30mItem.Content = L("dailyword.settings.frequency_30m", "30 min");
        Frequency1hItem.Content = L("dailyword.settings.frequency_1h", "1 hour");
        Frequency3hItem.Content = L("dailyword.settings.frequency_3h", "3 hours");
        Frequency6hItem.Content = L("dailyword.settings.frequency_6h", "6 hours");
        Frequency12hItem.Content = L("dailyword.settings.frequency_12h", "12 hours");
        Frequency24hItem.Content = L("dailyword.settings.frequency_24h", "24 hours");
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
        snapshot.DailyWordAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.DailyWordAutoRefreshIntervalMinutes = GetSelectedInterval();
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

        return 360;
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
            return 360;
        }

        if (SupportedIntervals.Contains(minutes))
        {
            return minutes;
        }

        return SupportedIntervals
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(360);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
