using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class ClockComponentEditor : ComponentEditorViewBase
{
    private readonly TimeZoneService _timeZoneService = new();
    private bool _suppressEvents;

    public ClockComponentEditor()
        : this(null)
    {
    }

    public ClockComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        if (context is not null)
        {
            _timeZoneService.CurrentTimeZone = context.SettingsFacade.Region.GetTimeZoneService().CurrentTimeZone;
        }

        InitializeComponent();
        ApplyState();
    }

    private void ApplyState()
    {
        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Clock";
        DescriptionTextBlock.Text = L("clock.settings.desc", "配置时区和秒针动画。");
        TimeZoneLabelTextBlock.Text = L("clock.settings.timezone", "时区");
        SecondHandLabelTextBlock.Text = L("clock.settings.second_mode_label", "秒针方式");
        TickRadioButton.Content = L("clock.second_mode.tick", "跳针");
        SweepRadioButton.Content = L("clock.second_mode.sweep", "扫针");

        var snapshot = LoadSnapshot();
        var configuredTimeZoneId = string.IsNullOrWhiteSpace(snapshot.DesktopClockTimeZoneId)
            ? TimeZoneInfo.Local.Id
            : snapshot.DesktopClockTimeZoneId.Trim();

        _suppressEvents = true;
        TimeZoneComboBox.Items.Clear();
        foreach (var timeZone in _timeZoneService.GetAllTimeZones()
                     .OrderBy(zone => zone.GetUtcOffset(DateTime.UtcNow))
                     .ThenBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ComboBoxItem
            {
                Tag = timeZone.Id,
                Content = FormatTimeZone(timeZone)
            };
            item.Classes.Add("component-editor-select-item");
            TimeZoneComboBox.Items.Add(item);
        }

        TimeZoneComboBox.SelectedItem = TimeZoneComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, configuredTimeZoneId, StringComparison.OrdinalIgnoreCase))
            ?? TimeZoneComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        var secondHandMode = ClockSecondHandMode.Normalize(snapshot.DesktopClockSecondHandMode);
        TickRadioButton.IsChecked = string.Equals(secondHandMode, ClockSecondHandMode.Tick, StringComparison.OrdinalIgnoreCase);
        SweepRadioButton.IsChecked = string.Equals(secondHandMode, ClockSecondHandMode.Sweep, StringComparison.OrdinalIgnoreCase);
        _suppressEvents = false;
    }

    private void OnTimeZoneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        SaveState();
    }

    private void OnSecondHandChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        _suppressEvents = true;
        if (sender == TickRadioButton)
        {
            TickRadioButton.IsChecked = true;
            SweepRadioButton.IsChecked = false;
        }
        else if (sender == SweepRadioButton)
        {
            SweepRadioButton.IsChecked = true;
            TickRadioButton.IsChecked = false;
        }

        if (TickRadioButton.IsChecked != true && SweepRadioButton.IsChecked != true)
        {
            TickRadioButton.IsChecked = true;
        }

        _suppressEvents = false;
        SaveState();
    }

    private void SaveState()
    {
        var snapshot = LoadSnapshot();
        snapshot.DesktopClockTimeZoneId = TimeZoneComboBox.SelectedItem is ComboBoxItem item && item.Tag is string timeZoneId
            ? timeZoneId
            : TimeZoneInfo.Local.Id;
        snapshot.DesktopClockSecondHandMode = SweepRadioButton.IsChecked == true
            ? ClockSecondHandMode.Sweep
            : ClockSecondHandMode.Tick;

        SaveSnapshot(
            snapshot,
            nameof(ComponentSettingsSnapshot.DesktopClockTimeZoneId),
            nameof(ComponentSettingsSnapshot.DesktopClockSecondHandMode));
    }

    private static string FormatTimeZone(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var totalMinutes = Math.Abs((int)offset.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {timeZone.StandardName}";
    }
}
