using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class Stcn24ForumSettingsWindow : UserControl
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public Stcn24ForumSettingsWindow()
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

        var enabled = componentSnapshot.Stcn24ForumAutoRefreshEnabled;
        var interval = NormalizeInterval(componentSnapshot.Stcn24ForumAutoRefreshIntervalMinutes);
        var sourceType = Stcn24ForumSourceTypes.Normalize(componentSnapshot.Stcn24ForumSourceType);

        _suppressEvents = true;
        AutoRefreshCheckBox.IsChecked = enabled;
        SelectSourceType(sourceType);
        SelectInterval(interval);
        FrequencyCardBorder.IsVisible = enabled;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("stcn24.settings.title", "STCN 24 settings");
        DescriptionTextBlock.Text = L("stcn24.settings.desc", "Configure information source, auto refresh and refresh interval.");
        SourceLabelTextBlock.Text = L("stcn24.settings.source_label", "Information source");
        SourceLatestCreatedItem.Content = L("stcn24.settings.source_latest_created", "Latest posts");
        SourceLatestActivityItem.Content = L("stcn24.settings.source_latest_activity", "Latest activity");
        SourceMostRepliesItem.Content = L("stcn24.settings.source_most_replies", "Most replies");
        SourceEarliestCreatedItem.Content = L("stcn24.settings.source_earliest_created", "Earliest posts");
        SourceEarliestActivityItem.Content = L("stcn24.settings.source_earliest_activity", "Earliest activity");
        SourceLeastRepliesItem.Content = L("stcn24.settings.source_least_replies", "Least replies");
        SourceFrontpageLatestItem.Content = L("stcn24.settings.source_frontpage_latest", "Frontpage latest");
        SourceFrontpageEarliestItem.Content = L("stcn24.settings.source_frontpage_earliest", "Frontpage earliest");
        AutoRefreshLabelTextBlock.Text = L("stcn24.settings.auto_refresh_label", "Auto refresh");
        AutoRefreshCheckBox.Content = L("stcn24.settings.auto_refresh_enabled", "Enable auto refresh");
        FrequencyLabelTextBlock.Text = L("stcn24.settings.frequency_label", "Refresh interval");
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
        snapshot.Stcn24ForumSourceType = GetSelectedSourceType();
        snapshot.Stcn24ForumAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
        snapshot.Stcn24ForumAutoRefreshIntervalMinutes = GetSelectedInterval();
        _componentSettingsService.Save(snapshot);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedSourceType()
    {
        if (SourceComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string sourceTag)
        {
            return Stcn24ForumSourceTypes.Normalize(sourceTag);
        }

        return Stcn24ForumSourceTypes.LatestCreated;
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

    private void SelectSourceType(string sourceType)
    {
        var normalizedSourceType = Stcn24ForumSourceTypes.Normalize(sourceType);
        var selected = SourceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item =>
                item.Tag is string sourceTag &&
                string.Equals(Stcn24ForumSourceTypes.Normalize(sourceTag), normalizedSourceType, StringComparison.OrdinalIgnoreCase));
        SourceComboBox.SelectedItem = selected ?? SourceComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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
