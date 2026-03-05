using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailySentenceSettingsWindow : UserControl
{
    private static readonly int[] SupportedIntervals = [5, 10, 40, 60, 720, 1440];

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public DailySentenceSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var snapshot = _appSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);

        var enabled = snapshot.DailySentenceAutoRotateEnabled;
        var interval = NormalizeInterval(snapshot.DailySentenceAutoRotateIntervalMinutes);

        _suppressEvents = true;
        AutoRotateCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("daily_sentence.settings.title", "Daily sentence settings");
        DescriptionTextBlock.Text = L("daily_sentence.settings.desc", "Configure auto-rotation and refresh interval.");
        AutoRotateLabelTextBlock.Text = L("daily_sentence.settings.auto_rotate_label", "Auto-rotation");
        AutoRotateCheckBox.Content = L("daily_sentence.settings.auto_rotate_enabled", "Enable auto-rotation");
        FrequencyLabelTextBlock.Text = L("daily_sentence.settings.frequency_label", "Rotation interval");
        Frequency5mItem.Content = L("daily_sentence.settings.frequency_5m", "5 min");
        Frequency10mItem.Content = L("daily_sentence.settings.frequency_10m", "10 min");
        Frequency40mItem.Content = L("daily_sentence.settings.frequency_40m", "40 min");
        Frequency1hItem.Content = L("daily_sentence.settings.frequency_1h", "1 hour");
        Frequency12hItem.Content = L("daily_sentence.settings.frequency_12h", "12 hours");
        Frequency24hItem.Content = L("daily_sentence.settings.frequency_24h", "24 hours");
    }

    private void OnAutoRotateChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var enabled = AutoRotateCheckBox.IsChecked == true;
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
        var snapshot = _appSettingsService.Load();
        snapshot.DailySentenceAutoRotateEnabled = AutoRotateCheckBox.IsChecked == true;
        snapshot.DailySentenceAutoRotateIntervalMinutes = GetSelectedInterval();
        _appSettingsService.Save(snapshot);
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

        return 60;
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
            return 60;
        }

        if (SupportedIntervals.Contains(minutes))
        {
            return minutes;
        }

        return SupportedIntervals
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(60);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
