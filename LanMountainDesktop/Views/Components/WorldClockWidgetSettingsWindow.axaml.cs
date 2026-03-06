using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WorldClockWidgetSettingsWindow : UserControl
{
    private static readonly IReadOnlyDictionary<string, string> ZhTimeZoneNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "中国标准时间",
            ["Asia/Shanghai"] = "中国标准时间",
            ["GMT Standard Time"] = "格林威治标准时间",
            ["Europe/London"] = "格林威治标准时间",
            ["AUS Eastern Standard Time"] = "澳大利亚东部标准时间",
            ["Australia/Sydney"] = "澳大利亚东部标准时间",
            ["Eastern Standard Time"] = "美国东部标准时间",
            ["America/New_York"] = "美国东部标准时间",
            ["Tokyo Standard Time"] = "日本标准时间",
            ["Asia/Tokyo"] = "日本标准时间",
            ["UTC"] = "协调世界时",
            ["Etc/UTC"] = "协调世界时"
        };

    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly TimeZoneService _timeZoneService = new();
    private readonly ComboBox[] _timeZoneComboBoxes;
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";
    private IReadOnlyList<TimeZoneInfo> _allTimeZones = Array.Empty<TimeZoneInfo>();
    private IReadOnlyList<string> _selectedTimeZoneIds = Array.Empty<string>();
    private string _secondHandMode = ClockSecondHandMode.Tick;

    public event EventHandler? SettingsChanged;

    public WorldClockWidgetSettingsWindow()
    {
        InitializeComponent();

        _timeZoneComboBoxes =
        [
            ClockOneTimeZoneComboBox,
            ClockTwoTimeZoneComboBox,
            ClockThreeTimeZoneComboBox,
            ClockFourTimeZoneComboBox
        ];

        LoadState();
        ApplyLocalization();
        PopulateTimeZoneComboBoxes();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        _allTimeZones = _timeZoneService
            .GetAllTimeZones()
            .OrderBy(zone => zone.GetUtcOffset(DateTime.UtcNow))
            .ThenBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selectedTimeZoneIds = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(
            componentSnapshot.WorldClockTimeZoneIds,
            _allTimeZones);
        _secondHandMode = ClockSecondHandMode.Normalize(componentSnapshot.WorldClockSecondHandMode);
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("worldclock.settings.title", "世界时钟设置");
        DescriptionTextBlock.Text = L("worldclock.settings.desc", "分别为四个时钟选择时区。");

        ClockOneLabelTextBlock.Text = L("worldclock.settings.clock_1", "时钟 1");
        ClockTwoLabelTextBlock.Text = L("worldclock.settings.clock_2", "时钟 2");
        ClockThreeLabelTextBlock.Text = L("worldclock.settings.clock_3", "时钟 3");
        ClockFourLabelTextBlock.Text = L("worldclock.settings.clock_4", "时钟 4");
        SecondHandModeLabelTextBlock.Text = L("worldclock.settings.second_mode_label", "秒针方式");
        SecondHandTickRadioButton.Content = L("clock.second_mode.tick", "跳针");
        SecondHandSweepRadioButton.Content = L("clock.second_mode.sweep", "扫针");
    }

    private void PopulateTimeZoneComboBoxes()
    {
        _suppressEvents = true;
        try
        {
            foreach (var comboBox in _timeZoneComboBoxes)
            {
                comboBox.Items.Clear();
                foreach (var timeZone in _allTimeZones)
                {
                    comboBox.Items.Add(new ComboBoxItem
                    {
                        Tag = timeZone.Id,
                        Content = GetLocalizedTimeZoneDisplayName(timeZone)
                    });
                }
            }

            for (var index = 0; index < _timeZoneComboBoxes.Length; index++)
            {
                var comboBox = _timeZoneComboBoxes[index];
                var targetId = index < _selectedTimeZoneIds.Count
                    ? _selectedTimeZoneIds[index]
                    : TimeZoneInfo.Local.Id;

                var selected = comboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string, targetId, StringComparison.OrdinalIgnoreCase));

                comboBox.SelectedItem = selected ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
            }

            var normalizedMode = ClockSecondHandMode.Normalize(_secondHandMode);
            SecondHandTickRadioButton.IsChecked = string.Equals(
                normalizedMode,
                ClockSecondHandMode.Tick,
                StringComparison.OrdinalIgnoreCase);
            SecondHandSweepRadioButton.IsChecked = string.Equals(
                normalizedMode,
                ClockSecondHandMode.Sweep,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _suppressEvents = false;
        }
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

    private void OnSecondHandModeChanged(object? sender, RoutedEventArgs e)
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
        var selectedIds = GetSelectedTimeZoneIds();
        var normalizedIds = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(selectedIds, _allTimeZones);
        _secondHandMode = GetSelectedSecondHandMode();

        var snapshot = _componentSettingsService.Load();
        snapshot.WorldClockTimeZoneIds = normalizedIds.ToList();
        snapshot.WorldClockSecondHandMode = _secondHandMode;
        _componentSettingsService.Save(snapshot);

        _selectedTimeZoneIds = normalizedIds;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedSecondHandMode()
    {
        return SecondHandSweepRadioButton.IsChecked == true
            ? ClockSecondHandMode.Sweep
            : ClockSecondHandMode.Tick;
    }

    private List<string> GetSelectedTimeZoneIds()
    {
        var selectedIds = new List<string>(_timeZoneComboBoxes.Length);
        foreach (var comboBox in _timeZoneComboBoxes)
        {
            if (comboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string timeZoneId &&
                !string.IsNullOrWhiteSpace(timeZoneId))
            {
                selectedIds.Add(timeZoneId.Trim());
                continue;
            }

            selectedIds.Add(TimeZoneInfo.Local.Id);
        }

        return selectedIds;
    }

    private string GetLocalizedTimeZoneDisplayName(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var totalMinutes = Math.Abs((int)offset.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        var displayName = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? ResolveZhDisplayName(timeZone)
            : ResolveEnDisplayName(timeZone);

        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {displayName}";
    }

    private static string ResolveZhDisplayName(TimeZoneInfo timeZone)
    {
        if (ZhTimeZoneNames.TryGetValue(timeZone.Id, out var localizedName))
        {
            return localizedName;
        }

        return string.IsNullOrWhiteSpace(timeZone.StandardName)
            ? timeZone.DisplayName
            : timeZone.StandardName;
    }

    private static string ResolveEnDisplayName(TimeZoneInfo timeZone)
    {
        if (!string.IsNullOrWhiteSpace(timeZone.StandardName))
        {
            return timeZone.StandardName;
        }

        return timeZone.DisplayName;
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
