using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class CnrDailyNewsSettingsWindow : UserControl
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public CnrDailyNewsSettingsWindow()
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

        var enabled = componentSnapshot.CnrDailyNewsAutoRotateEnabled;
        var interval = NormalizeInterval(componentSnapshot.CnrDailyNewsAutoRotateIntervalMinutes);

        _suppressEvents = true;
        AutoRotateCheckBox.IsChecked = enabled;
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("cnrnews.settings.title", "CNR news settings");
        DescriptionTextBlock.Text = L("cnrnews.settings.desc", "Configure auto-rotation and refresh interval.");
        AutoRotateLabelTextBlock.Text = L("cnrnews.settings.auto_rotate_label", "Auto-rotation");
        AutoRotateCheckBox.Content = L("cnrnews.settings.auto_rotate_enabled", "Enable auto-rotation");
        FrequencyLabelTextBlock.Text = L("cnrnews.settings.frequency_label", "Rotation interval");
        ApplyFrequencyLocalization();
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
        var snapshot = _componentSettingsService.Load();
        snapshot.CnrDailyNewsAutoRotateEnabled = AutoRotateCheckBox.IsChecked == true;
        snapshot.CnrDailyNewsAutoRotateIntervalMinutes = GetSelectedInterval();
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
        return RefreshIntervalCatalog.Normalize(minutes, 60);
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
