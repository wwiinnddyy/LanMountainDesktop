using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public sealed record ComponentEditorSelectionOption(
    string Value,
    string LabelKey,
    string FallbackLabel);

public sealed class ToggleIntervalComponentEditorOptions
{
    public required string DescriptionKey { get; init; }
    public required string DescriptionFallback { get; init; }
    public required string ToggleLabelKey { get; init; }
    public required string ToggleLabelFallback { get; init; }
    public string ToggleDescriptionKey { get; init; } = string.Empty;
    public string ToggleDescriptionFallback { get; init; } = string.Empty;
    public required string IntervalLabelKey { get; init; }
    public required string IntervalLabelFallback { get; init; }
    public required Func<ComponentSettingsSnapshot, bool> GetEnabled { get; init; }
    public required Action<ComponentSettingsSnapshot, bool> SetEnabled { get; init; }
    public required Func<ComponentSettingsSnapshot, int> GetInterval { get; init; }
    public required Action<ComponentSettingsSnapshot, int> SetInterval { get; init; }
    public required int DefaultInterval { get; init; }
    public IReadOnlyList<string> ChangedKeys { get; init; } = Array.Empty<string>();
    public string ExtraSelectorLabelKey { get; init; } = string.Empty;
    public string ExtraSelectorLabelFallback { get; init; } = string.Empty;
    public IReadOnlyList<ComponentEditorSelectionOption> ExtraOptions { get; init; } = Array.Empty<ComponentEditorSelectionOption>();
    public Func<ComponentSettingsSnapshot, string>? GetExtraValue { get; init; }
    public Action<ComponentSettingsSnapshot, string>? SetExtraValue { get; init; }
}

public partial class ToggleIntervalComponentEditor : ComponentEditorViewBase
{
    private static readonly IReadOnlyList<int> SupportedIntervals = RefreshIntervalCatalog.SupportedIntervalsMinutes;
    private readonly ToggleIntervalComponentEditorOptions _options;
    private bool _suppressEvents;

    public ToggleIntervalComponentEditor()
        : this(null, new ToggleIntervalComponentEditorOptions
        {
            DescriptionKey = "component.editor.desc",
            DescriptionFallback = "Configure this component.",
            ToggleLabelKey = "component.editor.toggle",
            ToggleLabelFallback = "Enabled",
            IntervalLabelKey = "component.editor.interval",
            IntervalLabelFallback = "Refresh interval",
            DefaultInterval = 15,
            GetEnabled = _ => true,
            SetEnabled = (_, _) => { },
            GetInterval = _ => 15,
            SetInterval = (_, _) => { }
        })
    {
    }

    public ToggleIntervalComponentEditor(
        DesktopComponentEditorContext? context,
        ToggleIntervalComponentEditorOptions options)
        : base(context)
    {
        _options = options;
        InitializeComponent();
        BuildOptions();
        ApplyState();
    }

    private void BuildOptions()
    {
        IntervalComboBox.Items.Clear();
        foreach (var minutes in SupportedIntervals)
        {
            var item = new ComboBoxItem
            {
                Tag = minutes.ToString(),
                Content = L("refresh.frequency." + RefreshIntervalCatalog.ToLocalizationKeySuffix(minutes), RefreshIntervalCatalog.ToEnglishFallbackLabel(minutes))
            };
            item.Classes.Add("component-editor-select-item");
            IntervalComboBox.Items.Add(item);
        }

        ExtraSelectorComboBox.Items.Clear();
        foreach (var option in _options.ExtraOptions)
        {
            var item = new ComboBoxItem
            {
                Tag = option.Value,
                Content = L(option.LabelKey, option.FallbackLabel)
            };
            item.Classes.Add("component-editor-select-item");
            ExtraSelectorComboBox.Items.Add(item);
        }
    }

    private void ApplyState()
    {
        ToggleLabelTextBlock.Text = L(_options.ToggleLabelKey, _options.ToggleLabelFallback);
        ToggleDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(_options.ToggleDescriptionKey)
            ? L("component.editor.instance_scope", "Changes are stored per component instance.")
            : L(_options.ToggleDescriptionKey, _options.ToggleDescriptionFallback);
        IntervalLabelTextBlock.Text = L(_options.IntervalLabelKey, _options.IntervalLabelFallback);
        ExtraSelectorLabelTextBlock.Text = L(_options.ExtraSelectorLabelKey, _options.ExtraSelectorLabelFallback);
        ExtraSelectorCard.IsVisible = _options.ExtraOptions.Count > 0;

        _suppressEvents = true;
        try
        {
            var snapshot = LoadSnapshot();
            var enabled = _options.GetEnabled(snapshot);
            EnabledToggleSwitch.IsChecked = enabled;
            IntervalCard.IsVisible = enabled;

            var normalizedInterval = RefreshIntervalCatalog.Normalize(_options.GetInterval(snapshot), _options.DefaultInterval);
            IntervalComboBox.SelectedItem = IntervalComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is string tag && int.TryParse(tag, out var minutes) && minutes == normalizedInterval);

            if (_options.GetExtraValue is not null && _options.ExtraOptions.Count > 0)
            {
                var extraValue = _options.GetExtraValue(snapshot);
                ExtraSelectorComboBox.SelectedItem = ExtraSelectorComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string, extraValue, StringComparison.OrdinalIgnoreCase))
                    ?? ExtraSelectorComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void OnEnabledChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        IntervalCard.IsVisible = EnabledToggleSwitch.IsChecked == true;
        SaveState();
    }

    private void OnIntervalSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        SaveState();
    }

    private void OnExtraSelectionChanged(object? sender, SelectionChangedEventArgs e)
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
        var snapshot = LoadSnapshot();
        _options.SetEnabled(snapshot, EnabledToggleSwitch.IsChecked == true);
        _options.SetInterval(snapshot, GetSelectedInterval());

        if (_options.SetExtraValue is not null &&
            ExtraSelectorComboBox.SelectedItem is ComboBoxItem extraItem &&
            extraItem.Tag is string extraValue)
        {
            _options.SetExtraValue(snapshot, extraValue);
        }

        SaveSnapshot(snapshot, _options.ChangedKeys.ToArray());
    }

    private int GetSelectedInterval()
    {
        if (IntervalComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var minutes))
        {
            return RefreshIntervalCatalog.Normalize(minutes, _options.DefaultInterval);
        }

        return _options.DefaultInterval;
    }
}
