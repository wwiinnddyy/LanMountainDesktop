using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class AnalogClockWidgetSettingsWindow : UserControl, IComponentPlacementContextAware, IComponentSettingsStoreAware
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
    private IComponentInstanceSettingsStore _componentSettingsStore = new ComponentSettingsService();
    private readonly LocalizationService _localizationService = new();
    private readonly TimeZoneService _timeZoneService = new();
    private bool _suppressEvents;
    private string _languageCode = "zh-CN";
    private string _componentId = BuiltInComponentIds.DesktopClock;
    private string _placementId = string.Empty;
    private IReadOnlyList<TimeZoneInfo> _allTimeZones = Array.Empty<TimeZoneInfo>();
    private string _selectedTimeZoneId = string.Empty;
    private string _secondHandMode = ClockSecondHandMode.Tick;

    public event EventHandler? SettingsChanged;

    public AnalogClockWidgetSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
        PopulateTimeZoneComboBox();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopClock
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        LoadState();
        ApplyLocalization();
        PopulateTimeZoneComboBox();
    }

    public void SetComponentSettingsStore(IComponentInstanceSettingsStore settingsStore)
    {
        _componentSettingsStore = settingsStore ?? new ComponentSettingsService();
        LoadState();
        ApplyLocalization();
        PopulateTimeZoneComboBox();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);
        _selectedTimeZoneId = string.IsNullOrWhiteSpace(componentSnapshot.DesktopClockTimeZoneId)
            ? "China Standard Time"
            : componentSnapshot.DesktopClockTimeZoneId.Trim();
        _secondHandMode = ClockSecondHandMode.Normalize(componentSnapshot.DesktopClockSecondHandMode);

        _allTimeZones = _timeZoneService
            .GetAllTimeZones()
            .OrderBy(zone => zone.GetUtcOffset(DateTime.UtcNow))
            .ThenBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("desktop_clock.settings.title", "时钟设置");
        DescriptionTextBlock.Text = L("desktop_clock.settings.desc", "为单时钟选择时区。");
        TimeZoneLabelTextBlock.Text = L("desktop_clock.settings.timezone_label", "时区");
        SecondHandModeLabelTextBlock.Text = L("desktop_clock.settings.second_mode_label", "秒针方式");
        SecondHandTickRadioButton.Content = L("clock.second_mode.tick", "跳针");
        SecondHandSweepRadioButton.Content = L("clock.second_mode.sweep", "扫针");
    }

    private void PopulateTimeZoneComboBox()
    {
        _suppressEvents = true;
        try
        {
            TimeZoneComboBox.Items.Clear();
            foreach (var timeZone in _allTimeZones)
            {
                TimeZoneComboBox.Items.Add(new ComboBoxItem
                {
                    Tag = timeZone.Id,
                    Content = GetLocalizedTimeZoneDisplayName(timeZone)
                });
            }

            var normalizedId = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(
                new[] { _selectedTimeZoneId },
                _allTimeZones)[0];
            _selectedTimeZoneId = normalizedId;

            var selected = TimeZoneComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, normalizedId, StringComparison.OrdinalIgnoreCase));

            TimeZoneComboBox.SelectedItem = selected ?? TimeZoneComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();

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
        var selectedId = (TimeZoneComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var normalizedId = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(
            new[] { selectedId ?? _selectedTimeZoneId },
            _allTimeZones)[0];
        _selectedTimeZoneId = normalizedId;
        _secondHandMode = GetSelectedSecondHandMode();

        var snapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
        snapshot.DesktopClockTimeZoneId = normalizedId;
        snapshot.DesktopClockSecondHandMode = _secondHandMode;
        _componentSettingsStore.SaveForComponent(_componentId, _placementId, snapshot);

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedSecondHandMode()
    {
        return SecondHandSweepRadioButton.IsChecked == true
            ? ClockSecondHandMode.Sweep
            : ClockSecondHandMode.Tick;
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
