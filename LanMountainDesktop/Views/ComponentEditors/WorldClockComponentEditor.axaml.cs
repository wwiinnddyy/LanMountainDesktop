using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class WorldClockComponentEditor : ComponentEditorViewBase
{
    private readonly TimeZoneService _timeZoneService = new();
    private readonly ComboBox[] _timeZoneCombos;
    private bool _suppressEvents;

    public WorldClockComponentEditor()
        : this(null)
    {
    }

    public WorldClockComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        if (context is not null)
        {
            _timeZoneService.CurrentTimeZone = context.SettingsFacade.Region.GetTimeZoneService().CurrentTimeZone;
        }

        InitializeComponent();
        _timeZoneCombos = [ClockOneComboBox, ClockTwoComboBox, ClockThreeComboBox, ClockFourComboBox];
        ApplyState();
    }

    private void ApplyState()
    {
        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "World Clock";
        DescriptionTextBlock.Text = L("worldclock.settings.desc", "分别为四个时钟选择时区。");
        ClockOneLabelTextBlock.Text = L("worldclock.settings.clock_1", "时钟 1");
        ClockTwoLabelTextBlock.Text = L("worldclock.settings.clock_2", "时钟 2");
        ClockThreeLabelTextBlock.Text = L("worldclock.settings.clock_3", "时钟 3");
        ClockFourLabelTextBlock.Text = L("worldclock.settings.clock_4", "时钟 4");
        SecondHandLabelTextBlock.Text = L("worldclock.settings.second_mode_label", "秒针方式");
        TickRadioButton.Content = L("clock.second_mode.tick", "跳针");
        SweepRadioButton.Content = L("clock.second_mode.sweep", "扫针");

        var allTimeZones = _timeZoneService.GetAllTimeZones()
            .OrderBy(zone => zone.GetUtcOffset(DateTime.UtcNow))
            .ThenBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedIds = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(LoadSnapshot().WorldClockTimeZoneIds, allTimeZones);

        _suppressEvents = true;
        foreach (var combo in _timeZoneCombos)
        {
            combo.Items.Clear();
            foreach (var zone in allTimeZones)
            {
                var item = new ComboBoxItem
                {
                    Tag = zone.Id,
                    Content = FormatTimeZone(zone)
                };
                item.Classes.Add("component-editor-select-item");
                combo.Items.Add(item);
            }
        }

        for (var index = 0; index < _timeZoneCombos.Length; index++)
        {
            var combo = _timeZoneCombos[index];
            var targetId = index < selectedIds.Count ? selectedIds[index] : TimeZoneInfo.Local.Id;
            combo.SelectedItem = combo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, targetId, StringComparison.OrdinalIgnoreCase))
                ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        var secondMode = ClockSecondHandMode.Normalize(LoadSnapshot().WorldClockSecondHandMode);
        TickRadioButton.IsChecked = string.Equals(secondMode, ClockSecondHandMode.Tick, StringComparison.OrdinalIgnoreCase);
        SweepRadioButton.IsChecked = string.Equals(secondMode, ClockSecondHandMode.Sweep, StringComparison.OrdinalIgnoreCase);
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
        snapshot.WorldClockTimeZoneIds = _timeZoneCombos
            .Select(combo => combo.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : TimeZoneInfo.Local.Id)
            .ToList();
        snapshot.WorldClockSecondHandMode = SweepRadioButton.IsChecked == true
            ? ClockSecondHandMode.Sweep
            : ClockSecondHandMode.Tick;

        SaveSnapshot(
            snapshot,
            nameof(ComponentSettingsSnapshot.WorldClockTimeZoneIds),
            nameof(ComponentSettingsSnapshot.WorldClockSecondHandMode));
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
