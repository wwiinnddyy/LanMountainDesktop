using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class StatusBarSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public StatusBarSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        ClockFormats = CreateClockFormats();
        ClockPositions = CreateClockPositions();
        TextCapsulePositions = CreateTextCapsulePositions();
        SpacingModes = CreateSpacingModes();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> ClockFormats { get; }

    public IReadOnlyList<SelectionOption> ClockPositions { get; }

    public IReadOnlyList<SelectionOption> TextCapsulePositions { get; }

    public IReadOnlyList<SelectionOption> SpacingModes { get; }

    [ObservableProperty]
    private bool _showClock = true;

    [ObservableProperty]
    private SelectionOption _selectedClockFormat = new("HourMinuteSecond", "Hour:Minute:Second");

    [ObservableProperty]
    private bool _clockTransparentBackground;

    [ObservableProperty]
    private SelectionOption _selectedClockPosition = new("Left", "Left");

    [ObservableProperty]
    private SelectionOption _selectedSpacingMode = new("Relaxed", "Relaxed");

    [ObservableProperty]
    private int _customSpacingPercent = 12;

    [ObservableProperty]
    private bool _isCustomSpacingVisible;

    [ObservableProperty]
    private string _componentsHeader = string.Empty;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _clockHeader = string.Empty;

    [ObservableProperty]
    private string _clockDescription = string.Empty;

    [ObservableProperty]
    private string _clockFormatLabel = string.Empty;

    [ObservableProperty]
    private string _clockTransparentBackgroundLabel = string.Empty;

    [ObservableProperty]
    private string _clockTransparentBackgroundDescription = string.Empty;

    [ObservableProperty]
    private string _clockPositionLabel = string.Empty;

    [ObservableProperty]
    private string _textCapsuleHeader = string.Empty;

    [ObservableProperty]
    private string _textCapsuleDescription = string.Empty;

    [ObservableProperty]
    private bool _showTextCapsule;

    [ObservableProperty]
    private string _textCapsuleContent = "**Hello** World!";

    [ObservableProperty]
    private SelectionOption _selectedTextCapsulePosition = new("Right", "Right");

    [ObservableProperty]
    private bool _textCapsuleTransparentBackground;

    [ObservableProperty]
    private string _textCapsulePositionLabel = string.Empty;

    [ObservableProperty]
    private string _textCapsuleContentLabel = string.Empty;

    [ObservableProperty]
    private string _textCapsuleTransparentBackgroundLabel = string.Empty;

    [ObservableProperty]
    private string _spacingHeader = string.Empty;

    [ObservableProperty]
    private string _spacingDescription = string.Empty;

    [ObservableProperty]
    private string _customSpacingLabel = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.StatusBar.Get();

        ShowClock = state.TopStatusComponentIds.Any(id =>
            string.Equals(id, BuiltInComponentIds.Clock, StringComparison.OrdinalIgnoreCase));

        var clockFormat = string.IsNullOrWhiteSpace(state.ClockDisplayFormat)
            ? "HourMinuteSecond"
            : state.ClockDisplayFormat;
        SelectedClockFormat = ClockFormats.FirstOrDefault(option =>
            string.Equals(option.Value, clockFormat, StringComparison.OrdinalIgnoreCase))
            ?? ClockFormats[1];
        ClockTransparentBackground = state.ClockTransparentBackground;

        var clockPosition = NormalizeClockPosition(state.ClockPosition);
        SelectedClockPosition = ClockPositions.FirstOrDefault(option =>
            string.Equals(option.Value, clockPosition, StringComparison.OrdinalIgnoreCase))
            ?? ClockPositions[0];

        // 文字胶囊设置
        ShowTextCapsule = state.ShowTextCapsule;
        TextCapsuleContent = state.TextCapsuleContent ?? "**Hello** World!";
        var textCapsulePosition = NormalizeTextCapsulePosition(state.TextCapsulePosition);
        SelectedTextCapsulePosition = TextCapsulePositions.FirstOrDefault(option =>
            string.Equals(option.Value, textCapsulePosition, StringComparison.OrdinalIgnoreCase))
            ?? TextCapsulePositions[2]; // 默认靠右
        TextCapsuleTransparentBackground = state.TextCapsuleTransparentBackground;

        var spacingMode = NormalizeSpacingMode(state.SpacingMode);
        SelectedSpacingMode = SpacingModes.FirstOrDefault(option =>
            string.Equals(option.Value, spacingMode, StringComparison.OrdinalIgnoreCase))
            ?? SpacingModes[1];
        CustomSpacingPercent = Math.Clamp(state.CustomSpacingPercent, 0, 30);
        IsCustomSpacingVisible = string.Equals(SelectedSpacingMode.Value, "Custom", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnShowClockChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnSelectedClockFormatChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        Save();
    }

    partial void OnClockTransparentBackgroundChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnSelectedClockPositionChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        Save();
    }

    partial void OnShowTextCapsuleChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnTextCapsuleContentChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnSelectedTextCapsulePositionChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        Save();
    }

    partial void OnTextCapsuleTransparentBackgroundChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        Save();
    }

    partial void OnSelectedSpacingModeChanged(SelectionOption value)
    {
        IsCustomSpacingVisible = string.Equals(value?.Value, "Custom", StringComparison.OrdinalIgnoreCase);
        if (_isInitializing || value is null)
        {
            return;
        }

        Save();
    }

    partial void OnCustomSpacingPercentChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 30);
        if (normalized != value)
        {
            CustomSpacingPercent = normalized;
            return;
        }

        if (_isInitializing || !IsCustomSpacingVisible)
        {
            return;
        }

        Save();
    }

    private void Save()
    {
        var state = _settingsFacade.StatusBar.Get();
        var topComponents = state.TopStatusComponentIds
            .Where(id => !string.Equals(id, BuiltInComponentIds.Clock, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ShowClock)
        {
            topComponents.Add(BuiltInComponentIds.Clock);
        }

        _settingsFacade.StatusBar.Save(new StatusBarSettingsState(
            topComponents,
            state.PinnedTaskbarActions,
            state.EnableDynamicTaskbarActions,
            state.TaskbarLayoutMode,
            SelectedClockFormat.Value,
            ClockTransparentBackground,
            SelectedClockPosition.Value,
            ShowTextCapsule,
            TextCapsuleContent ?? "**Hello** World!",
            SelectedTextCapsulePosition?.Value ?? "Right",
            TextCapsuleTransparentBackground,
            NormalizeSpacingMode(SelectedSpacingMode.Value),
            Math.Clamp(CustomSpacingPercent, 0, 30)));
    }

    private IReadOnlyList<SelectionOption> CreateClockFormats()
    {
        return
        [
            new SelectionOption("HourMinute", L("settings.status_bar.clock_format.hm", "Hour:Minute")),
            new SelectionOption("HourMinuteSecond", L("settings.status_bar.clock_format.hms", "Hour:Minute:Second"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateClockPositions()
    {
        return
        [
            new SelectionOption("Left", L("settings.status_bar.clock_position.left", "Left")),
            new SelectionOption("Center", L("settings.status_bar.clock_position.center", "Center")),
            new SelectionOption("Right", L("settings.status_bar.clock_position.right", "Right"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateTextCapsulePositions()
    {
        return
        [
            new SelectionOption("Left", L("settings.status_bar.text_capsule_position.left", "Left")),
            new SelectionOption("Center", L("settings.status_bar.text_capsule_position.center", "Center")),
            new SelectionOption("Right", L("settings.status_bar.text_capsule_position.right", "Right"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateSpacingModes()
    {
        return
        [
            new SelectionOption("Compact", L("settings.status_bar.spacing_mode_compact", "Compact")),
            new SelectionOption("Relaxed", L("settings.status_bar.spacing_mode_relaxed", "Relaxed")),
            new SelectionOption("Custom", L("settings.status_bar.spacing_mode_custom", "Custom"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.status_bar.title", "Status Bar");
        PageDescription = L("settings.status_bar.description", "Choose which single-height components appear on the top status bar.");
        ComponentsHeader = L("settings.status_bar.title", "Status Bar");
        ClockHeader = L("settings.status_bar.clock_header", "Clock Component");
        ClockDescription = L("settings.status_bar.clock_description", "Display a clock on the top status bar.");
        ClockFormatLabel = L("settings.status_bar.clock_format_label", "Clock format");
        ClockTransparentBackgroundLabel = L("settings.status_bar.clock_transparent_background_label", "Transparent background");
        ClockTransparentBackgroundDescription = L("settings.status_bar.clock_transparent_background_desc", "Remove the capsule background and keep only the clock text.");
        ClockPositionLabel = L("settings.status_bar.clock_position_label", "Clock position");
        TextCapsuleHeader = L("settings.status_bar.text_capsule_header", "Text Capsule");
        TextCapsuleDescription = L("settings.status_bar.text_capsule_description", "Display custom text with Markdown support on the status bar.");
        TextCapsulePositionLabel = L("settings.status_bar.text_capsule_position_label", "Text capsule position");
        TextCapsuleContentLabel = L("settings.status_bar.text_capsule_content_label", "Text content (Markdown supported)");
        TextCapsuleTransparentBackgroundLabel = L("settings.status_bar.text_capsule_transparent_background_label", "Transparent background");
        SpacingHeader = L("settings.status_bar.spacing_header", "Component Spacing");
        SpacingDescription = L("settings.status_bar.spacing_desc", "Adjust spacing between status bar components.");
        CustomSpacingLabel = L("settings.status_bar.spacing_custom_label", "Custom spacing (%)");
    }

    private string NormalizeSpacingMode(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Compact", StringComparison.OrdinalIgnoreCase) => "Compact",
            _ when string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase) => "Custom",
            _ => "Relaxed"
        };
    }

    private static string NormalizeClockPosition(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase) => "Center",
            _ when string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase) => "Right",
            _ => "Left"
        };
    }

    private static string NormalizeTextCapsulePosition(string? value)
    {
        return value switch
        {
            _ when string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase) => "Left",
            _ when string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase) => "Center",
            _ => "Right"
        };
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
