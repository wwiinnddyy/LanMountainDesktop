using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private string _languageCode;
    private string _defaultRestartMessage = string.Empty;

    public SettingsWindowViewModel()
    {
        _localizationService = new();
        _languageCode = "zh-CN";
    }

    public SettingsWindowViewModel(LocalizationService localizationService, string languageCode)
    {
        _localizationService = localizationService;
        _languageCode = languageCode;
    }

    private string L(string key) => _localizationService.GetString(_languageCode, key, key);

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _currentPageTitle = string.Empty;

    [ObservableProperty]
    private bool _isPageTitleVisible = true;

    [ObservableProperty]
    private string? _currentPageDescription;

    [ObservableProperty]
    private string? _currentPageId;

    [ObservableProperty]
    private bool _isRestartRequested;

    [ObservableProperty]
    private string _restartMessage = string.Empty;

    [ObservableProperty]
    private string _restartTitle = string.Empty;

    [ObservableProperty]
    private string _restartButtonText = string.Empty;

    [ObservableProperty]
    private string _restartDialogTitle = string.Empty;

    [ObservableProperty]
    private string _restartDialogPrimaryText = string.Empty;

    [ObservableProperty]
    private string _restartDialogCloseText = string.Empty;

    [ObservableProperty]
    private string? _drawerTitle;

    [ObservableProperty]
    private string _drawerFallbackTitle = string.Empty;

    [ObservableProperty]
    private bool _isDrawerOpen;

    public SettingsWindowViewModel Initialize()
    {
        RefreshLanguage(_languageCode);
        CurrentPageTitle = Title;
        return this;
    }

    public void RefreshLanguage(string? languageCode)
    {
        _languageCode = _localizationService.NormalizeLanguageCode(languageCode);
        Title = L("settings.title");
        RestartTitle = L("settings.restart_dock.title");
        RestartButtonText = L("settings.restart_dock.button");
        RestartDialogTitle = L("settings.restart_dialog.title");
        RestartDialogPrimaryText = L("settings.restart_dialog.restart");
        RestartDialogCloseText = _localizationService.GetString(
            _languageCode,
            "settings.restart_dialog.later",
            L("settings.restart_dialog.cancel"));
        DrawerFallbackTitle = L("settings.window.drawer_default");

        var nextDefaultRestartMessage = L("settings.restart_dock.description");
        if (string.IsNullOrWhiteSpace(RestartMessage) || string.Equals(RestartMessage, _defaultRestartMessage, StringComparison.Ordinal))
        {
            RestartMessage = nextDefaultRestartMessage;
        }

        _defaultRestartMessage = nextDefaultRestartMessage;
    }

    public string GetDefaultRestartMessage() => _defaultRestartMessage;

    public ObservableCollection<SettingsPageDescriptor> Pages { get; } = [];
}

public sealed class SelectionOption
{
    public SelectionOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }

    public string Label { get; }
}

public sealed class ThemeSeedCandidateOption
{
    public ThemeSeedCandidateOption(string value, string label, Color color, bool isSelected)
    {
        Value = value;
        Label = label;
        Color = color;
        IsSelected = isSelected;
        Brush = new SolidColorBrush(color);
    }

    public string Value { get; }

    public string Label { get; }

    public Color Color { get; }

    public bool IsSelected { get; }

    public IBrush Brush { get; }
}

public sealed class TimeZoneOption
{
    public TimeZoneOption(string? id, string label)
    {
        Id = id;
        Label = label;
    }

    public string? Id { get; }

    public string Label { get; }
}

public sealed partial class GeneralSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly TimeZoneService _timeZoneService;
    private readonly LocalizationService _localizationService = new();
    private readonly string _startupRenderMode;
    private string _languageCode;
    private bool _isInitializing;

    public GeneralSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _timeZoneService = settingsFacade.Region.GetTimeZoneService();
        _startupRenderMode = Program.StartupRenderMode;

        var regionState = _settingsFacade.Region.Get();
        _languageCode = _localizationService.NormalizeLanguageCode(regionState.LanguageCode);

        Languages = CreateLanguageOptions();
        RenderModes = CreateRenderModeOptions();
        TimeZones = CreateTimeZoneOptions();
        RefreshLocalizedText();

        _isInitializing = true;
        SelectedLanguage = Languages.FirstOrDefault(option =>
            string.Equals(option.Value, regionState.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? Languages[0];
        SelectedTimeZone = TimeZones.FirstOrDefault(option =>
            string.Equals(option.Id, regionState.TimeZoneId, StringComparison.OrdinalIgnoreCase))
            ?? TimeZones[0];

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var normalizedRenderMode = AppRenderingModeHelper.Normalize(appSnapshot.AppRenderMode);
        SelectedRenderMode = RenderModes.FirstOrDefault(option =>
            string.Equals(option.Value, normalizedRenderMode, StringComparison.OrdinalIgnoreCase))
            ?? RenderModes[0];
        _isInitializing = false;

        RefreshPreview();
    }

    public event Action? RestartRequested;

    public IReadOnlyList<SelectionOption> Languages { get; }

    public IReadOnlyList<SelectionOption> RenderModes { get; }

    public IReadOnlyList<TimeZoneOption> TimeZones { get; }

    [ObservableProperty]
    private SelectionOption _selectedLanguage = new("zh-CN", "中文");

    [ObservableProperty]
    private TimeZoneOption _selectedTimeZone = new(null, "Follow system default");

    [ObservableProperty]
    private SelectionOption _selectedRenderMode = new(AppRenderingModeHelper.Default, "Default");

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _basicHeader = string.Empty;

    [ObservableProperty]
    private string _runtimeHeader = string.Empty;

    [ObservableProperty]
    private string _runtimeDescription = string.Empty;

    [ObservableProperty]
    private string _languageHeader = string.Empty;

    [ObservableProperty]
    private string _timeZoneHeader = string.Empty;

    [ObservableProperty]
    private string _timeZoneDescription = string.Empty;

    [ObservableProperty]
    private string _renderModeHeader = string.Empty;

    [ObservableProperty]
    private string _previewHeader = string.Empty;

    [ObservableProperty]
    private string _previewTimeLabel = string.Empty;

    [ObservableProperty]
    private string _previewDateLabel = string.Empty;

    [ObservableProperty]
    private string _previewTimeText = string.Empty;

    [ObservableProperty]
    private string _previewDateText = string.Empty;

    [ObservableProperty]
    private string _renderModeRestartMessage = string.Empty;

    partial void OnSelectedLanguageChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        // 更新语言代码并刷新UI文本
        _languageCode = _localizationService.NormalizeLanguageCode(value.Value);
        RefreshLocalizedText();
        RefreshPreview();
        
        // 保存设置
        _settingsFacade.Region.Save(new RegionSettingsState(
            value.Value,
            NormalizeTimeZoneId(SelectedTimeZone?.Id)));
    }

    partial void OnSelectedTimeZoneChanged(TimeZoneOption value)
    {
        RefreshPreview();
        if (_isInitializing || value is null)
        {
            return;
        }

        _settingsFacade.Region.Save(new RegionSettingsState(
            SelectedLanguage.Value,
            NormalizeTimeZoneId(value.Id)));
    }

    partial void OnSelectedRenderModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var normalizedRenderMode = AppRenderingModeHelper.Normalize(value.Value);
        appSnapshot.AppRenderMode = normalizedRenderMode;
        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            appSnapshot,
            changedKeys: [nameof(AppSettingsSnapshot.AppRenderMode)]);

        var restartRequired = !string.Equals(_startupRenderMode, normalizedRenderMode, StringComparison.OrdinalIgnoreCase);
        PendingRestartStateService.SetPending(PendingRestartStateService.RenderModeReason, restartRequired);
        if (restartRequired)
        {
            RestartRequested?.Invoke();
        }
    }

    private IReadOnlyList<SelectionOption> CreateLanguageOptions()
    {
        return
        [
            new SelectionOption("zh-CN", L("settings.region.language_zh", "中文")),
            new SelectionOption("en-US", L("settings.region.language_en", "English"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateRenderModeOptions()
    {
        return
        [
            new SelectionOption(AppRenderingModeHelper.Default, L("settings.about.render_mode.default", "Default")),
            new SelectionOption(AppRenderingModeHelper.Software, L("settings.about.render_mode.software", "Software")),
            new SelectionOption(AppRenderingModeHelper.AngleEgl, L("settings.about.render_mode.angle_egl", "Angle EGL")),
            new SelectionOption(AppRenderingModeHelper.Wgl, L("settings.about.render_mode.wgl", "WGL")),
            new SelectionOption(AppRenderingModeHelper.Vulkan, L("settings.about.render_mode.vulkan", "Vulkan"))
        ];
    }

    private IReadOnlyList<TimeZoneOption> CreateTimeZoneOptions()
    {
        return _timeZoneService
            .GetAllTimeZones()
            .Select(zone => new TimeZoneOption(zone.Id, _timeZoneService.GetTimeZoneDisplayName(zone)))
            .Prepend(new TimeZoneOption(null, L("settings.region.follow_system", "Follow system default")))
            .ToList();
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.general.title", "General");
        PageDescription = L("settings.general.description", "Core language, time zone, and runtime behavior.");
        BasicHeader = L("settings.general.basic_header", "Basic Settings");
        RuntimeHeader = L("settings.general.runtime_header", "Runtime");
        RuntimeDescription = L(
            "settings.about.render_mode_desc",
            "Choose the rendering backend. Restart the app after changing this option.");
        LanguageHeader = L("settings.region.language_header", "Language");
        TimeZoneHeader = L("settings.region.timezone_header", "Time Zone");
        TimeZoneDescription = L(
            "settings.region.timezone_desc",
            "Select a time zone. Clock and calendar widgets will follow this zone.");
        RenderModeHeader = L("settings.about.render_mode_header", "App Rendering Mode");
        PreviewHeader = L("settings.general.preview_header", "Date & Time Preview");
        PreviewTimeLabel = L("settings.general.preview_time_label", "Time");
        PreviewDateLabel = L("settings.general.preview_date_label", "Date");
        RenderModeRestartMessage = L(
            "settings.general.render_mode_restart_message",
            "Rendering mode changes require restarting the app.");
    }

    private void RefreshPreview()
    {
        var culture = ResolveCulture(SelectedLanguage?.Value ?? _languageCode);
        var timeZone = ResolveSelectedTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        PreviewTimeText = now.ToString("T", culture);
        PreviewDateText = now.ToString("D", culture);
    }

    private TimeZoneInfo ResolveSelectedTimeZone()
    {
        var timeZoneId = NormalizeTimeZoneId(SelectedTimeZone?.Id);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private string? NormalizeTimeZoneId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private CultureInfo ResolveCulture(string? languageCode)
    {
        var normalizedLanguageCode = _localizationService.NormalizeLanguageCode(languageCode);
        try
        {
            return CultureInfo.GetCultureInfo(normalizedLanguageCode);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("zh-CN");
        }
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class AppearanceSettingsPageViewModel : ViewModelBase
{
    private static readonly Color DefaultSeedColor = Color.Parse("#FF3B82F6");
    private static readonly SolidColorBrush NeutralLightBrushValue = new(Color.Parse("#FFFFFFFF"));
    private static readonly SolidColorBrush NeutralDarkBrushValue = new(Color.Parse("#FF000000"));
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IAppearanceThemeService _appearanceThemeService;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;
    private string? _selectedWallpaperSeed;

    public AppearanceSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        IAppearanceThemeService appearanceThemeService)
    {
        _settingsFacade = settingsFacade;
        _appearanceThemeService = appearanceThemeService;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();
        ThemeColorModes = CreateThemeColorModes();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public event Action<string>? RestartRequested;

    public IReadOnlyList<SelectionOption> ThemeColorModes { get; }

    [ObservableProperty]
    private IReadOnlyList<SelectionOption> _systemMaterialModes = [];

    [ObservableProperty]
    private bool _isNightMode;

    [ObservableProperty]
    private string _themeColor = string.Empty;

    [ObservableProperty]
    private Color _customSeedPickerValue = DefaultSeedColor;

    partial void OnCustomSeedPickerValueChanged(Color value)
    {
        if (_isInitializing ||
            !string.Equals(SelectedThemeColorMode?.Value, ThemeAppearanceValues.ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UpdatePreview(BuildPendingState(usePickerSeed: true));
    }

    [ObservableProperty]
    private bool _useSystemChrome;

    [ObservableProperty]
    private double _globalCornerRadiusScale = GlobalAppearanceSettings.DefaultCornerRadiusScale;

    [ObservableProperty]
    private SelectionOption _selectedThemeColorMode = new(ThemeAppearanceValues.ColorModeSeedMonet, "User theme color Monet");

    [ObservableProperty]
    private SelectionOption _selectedSystemMaterialMode = new(ThemeAppearanceValues.MaterialNone, "None");

    [ObservableProperty]
    private bool _isThemeColorEditable;

    [ObservableProperty]
    private bool _isWallpaperMode;

    [ObservableProperty]
    private bool _showNeutralPreview;

    [ObservableProperty]
    private bool _showMonetPreview;

    [ObservableProperty]
    private bool _isWallpaperSeedSelectable;

    [ObservableProperty]
    private string _themeColorSourceDescription = string.Empty;

    [ObservableProperty]
    private string _systemMaterialDescription = string.Empty;

    [ObservableProperty]
    private IBrush _primarySwatchBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IBrush _secondarySwatchBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IBrush _tertiarySwatchBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IBrush _neutralSwatchBrush = new SolidColorBrush(Color.Parse("#FFF2F4F7"));

    [ObservableProperty]
    private IBrush _seedSwatchBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IReadOnlyList<ThemeSeedCandidateOption> _wallpaperSeedCandidates = [];

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _nightModeLabel = string.Empty;

    [ObservableProperty]
    private string _useSystemChromeLabel = string.Empty;

    [ObservableProperty]
    private string _themeColorLabel = string.Empty;

    [ObservableProperty]
    private string _themeColorModeLabel = string.Empty;

    [ObservableProperty]
    private string _systemMaterialLabel = string.Empty;

    [ObservableProperty]
    private string _globalCornerRadiusLabel = string.Empty;

    [ObservableProperty]
    private string _globalCornerRadiusDescription = string.Empty;

    [ObservableProperty]
    private string _themeHeader = string.Empty;

    [ObservableProperty]
    private string _themeSourceNeutralText = string.Empty;

    [ObservableProperty]
    private string _themeSourceUserColorText = string.Empty;

    [ObservableProperty]
    private string _themeSourceWallpaperText = string.Empty;

    [ObservableProperty]
    private string _themeSourceDefaultDescription = string.Empty;

    [ObservableProperty]
    private string _themeSourceUserColorDescription = string.Empty;

    [ObservableProperty]
    private string _themeSourceWallpaperDescription = string.Empty;

    [ObservableProperty]
    private string _themeSourceWallpaperAppDescription = string.Empty;

    [ObservableProperty]
    private string _themeSourceWallpaperSystemDescription = string.Empty;

    [ObservableProperty]
    private string _themeSourceWallpaperFallbackDescription = string.Empty;

    [ObservableProperty]
    private string _systemMaterialNoneText = string.Empty;

    [ObservableProperty]
    private string _systemMaterialMicaText = string.Empty;

    [ObservableProperty]
    private string _systemMaterialAcrylicText = string.Empty;

    [ObservableProperty]
    private string _systemMaterialSwitchableDescription = string.Empty;

    [ObservableProperty]
    private string _systemMaterialFixedDescription = string.Empty;

    [ObservableProperty]
    private string _appearanceRestartMessage = string.Empty;

    [ObservableProperty]
    private string _previewPrimaryLabel = string.Empty;

    [ObservableProperty]
    private string _previewSecondaryLabel = string.Empty;

    [ObservableProperty]
    private string _previewTertiaryLabel = string.Empty;

    [ObservableProperty]
    private string _previewNeutralLabel = string.Empty;

    [ObservableProperty]
    private string _previewSeedLabel = string.Empty;

    [ObservableProperty]
    private string _previewNeutralLightLabel = string.Empty;

    [ObservableProperty]
    private string _previewNeutralDarkLabel = string.Empty;

    [ObservableProperty]
    private string _seedApplyButtonText = string.Empty;

    [ObservableProperty]
    private string _wallpaperSeedFlyoutTitle = string.Empty;

    [ObservableProperty]
    private string _wallpaperSeedCurrentText = string.Empty;

    public IBrush NeutralLightPreviewBrush => NeutralLightBrushValue;

    public IBrush NeutralDarkPreviewBrush => NeutralDarkBrushValue;

    public void Load()
    {
        var theme = _settingsFacade.Theme.Get();
        var liveSnapshot = _appearanceThemeService.GetCurrent();
        RefreshMaterialModeOptions(liveSnapshot);

        _isInitializing = true;
        try
        {
            ApplySavedState(theme);
        }
        finally
        {
            _isInitializing = false;
        }

        UpdatePreview(theme);
    }

    partial void OnIsNightModeChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        PersistCurrentState(restartRequired: false);
    }

    partial void OnUseSystemChromeChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        PersistCurrentState(restartRequired: false);
    }

    partial void OnGlobalCornerRadiusScaleChanged(double value)
    {
        if (_isInitializing)
        {
            return;
        }

        var normalized = GlobalAppearanceSettings.NormalizeCornerRadiusScale(value);
        if (Math.Abs(normalized - value) > 0.0001d)
        {
            _isInitializing = true;
            try
            {
                GlobalCornerRadiusScale = normalized;
            }
            finally
            {
                _isInitializing = false;
            }

            return;
        }

        PersistCurrentState(restartRequired: false);
    }

    partial void OnSelectedThemeColorModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        PersistCurrentState(restartRequired: true);
    }

    partial void OnSelectedSystemMaterialModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        PersistCurrentState(restartRequired: true);
    }

    [RelayCommand]
    private void ApplyCustomSeed()
    {
        if (!IsThemeColorEditable)
        {
            return;
        }

        ThemeColor = CustomSeedPickerValue.ToString();
        PersistCurrentState(restartRequired: false);
    }

    public void CancelCustomSeedPreview()
    {
        if (_isInitializing)
        {
            return;
        }

        SyncCustomSeedPickerWithSavedThemeColor();
        UpdatePreview(BuildPendingState(usePickerSeed: false));
    }

    public void SelectWallpaperSeed(string value)
    {
        if (!IsWallpaperMode || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _selectedWallpaperSeed = value;
        PersistCurrentState(restartRequired: true);
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.appearance.title", "Appearance");
        PageDescription = L("settings.appearance.description", "Adjust theme source, material background, and window chrome.");
        ThemeHeader = L("settings.appearance.theme_header", "Theme");
        NightModeLabel = L("settings.color.enable_night_mode_toggle", "Enable night mode");
        UseSystemChromeLabel = L("settings.color.use_system_chrome_toggle", "Use system window chrome");
        ThemeColorLabel = L("settings.color.theme_color_label", "Theme Accent Color");
        ThemeColorModeLabel = L("settings.appearance.theme_color_mode_label", "Theme color source");
        SystemMaterialLabel = L("settings.appearance.system_material_label", "System material");
        GlobalCornerRadiusLabel = L("settings.appearance.corner_radius.label", "Global corner radius");
        GlobalCornerRadiusDescription = L("settings.appearance.corner_radius.description", "Adjust the shared radius scale used by cards, panels, and component containers.");
        ThemeSourceNeutralText = L("settings.appearance.theme_color_mode.neutral", "Default neutral");
        ThemeSourceUserColorText = L("settings.appearance.theme_color_mode.user", "User theme color Monet");
        ThemeSourceWallpaperText = L("settings.appearance.theme_color_mode.wallpaper", "Wallpaper Monet");
        ThemeSourceDefaultDescription = L("settings.appearance.theme_color_mode_desc.neutral", "Use the standard light and dark neutral surfaces.");
        ThemeSourceUserColorDescription = L("settings.appearance.theme_color_mode_desc.user", "Use the selected theme color as the Monet seed.");
        ThemeSourceWallpaperDescription = L("settings.appearance.theme_color_mode_desc.wallpaper", "Use the current wallpaper palette. App wallpaper is preferred, then system wallpaper.");
        ThemeSourceWallpaperAppDescription = L("settings.appearance.theme_color_preview.app", "Currently previewing colors extracted from the app wallpaper.");
        ThemeSourceWallpaperSystemDescription = L("settings.appearance.theme_color_preview.system", "Currently previewing colors extracted from the system wallpaper.");
        ThemeSourceWallpaperFallbackDescription = L("settings.appearance.theme_color_preview.fallback", "No usable wallpaper was found. The app is using a fallback accent.");
        SystemMaterialNoneText = L("settings.appearance.system_material.none", "None");
        SystemMaterialMicaText = L("settings.appearance.system_material.mica", "Mica");
        SystemMaterialAcrylicText = L("settings.appearance.system_material.acrylic", "Acrylic");
        SystemMaterialSwitchableDescription = L("settings.appearance.system_material_desc.switchable", "Apply the selected material to windows, Dock, status bar, and component hosts.");
        SystemMaterialFixedDescription = L("settings.appearance.system_material_desc.fixed", "Your current system only exposes the available material modes listed here.");
        AppearanceRestartMessage = L(
            "settings.appearance.restart_message",
            "Theme source and system material changes require restarting the app.");
        PreviewPrimaryLabel = L("settings.appearance.preview.primary", "Primary");
        PreviewSecondaryLabel = L("settings.appearance.preview.secondary", "Secondary");
        PreviewTertiaryLabel = L("settings.appearance.preview.tertiary", "Tertiary");
        PreviewNeutralLabel = L("settings.appearance.preview.neutral", "Neutral");
        PreviewSeedLabel = L("settings.appearance.preview.seed", "Seed");
        PreviewNeutralLightLabel = L("settings.appearance.preview.neutral_light", "White");
        PreviewNeutralDarkLabel = L("settings.appearance.preview.neutral_dark", "Black");
        SeedApplyButtonText = L("settings.appearance.preview.apply_seed", "Apply");
        WallpaperSeedFlyoutTitle = L("settings.appearance.preview.wallpaper_candidates", "Wallpaper seed candidates");
        WallpaperSeedCurrentText = L("settings.appearance.preview.wallpaper_current", "Current");
    }

    private void RefreshMaterialModeOptions(AppearanceThemeSnapshot snapshot)
    {
        SystemMaterialModes = snapshot.AvailableSystemMaterialModes
            .Select(value => new SelectionOption(value, ResolveMaterialModeLabel(value)))
            .ToList();
        SystemMaterialDescription = snapshot.CanChangeSystemMaterial
            ? SystemMaterialSwitchableDescription
            : SystemMaterialFixedDescription;
    }

    private void ApplySavedState(ThemeAppearanceSettingsState theme)
    {
        IsNightMode = theme.IsNightMode;
        ThemeColor = theme.ThemeColor ?? string.Empty;
        UseSystemChrome = theme.UseSystemChrome;
        GlobalCornerRadiusScale = GlobalAppearanceSettings.NormalizeCornerRadiusScale(theme.GlobalCornerRadiusScale);
        _selectedWallpaperSeed = theme.SelectedWallpaperSeed;
        SyncCustomSeedPickerWithSavedThemeColor();

        var savedThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(theme.ThemeColorMode, theme.ThemeColor);
        var savedSystemMaterialMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(theme.SystemMaterialMode);
        SelectedThemeColorMode = ThemeColorModes.FirstOrDefault(option =>
            string.Equals(option.Value, savedThemeColorMode, StringComparison.OrdinalIgnoreCase))
            ?? ThemeColorModes[0];
        SelectedSystemMaterialMode = SystemMaterialModes.FirstOrDefault(option =>
            string.Equals(option.Value, savedSystemMaterialMode, StringComparison.OrdinalIgnoreCase))
            ?? SystemMaterialModes[0];
    }

    private void PersistCurrentState(bool restartRequired)
    {
        var pendingState = BuildPendingState(usePickerSeed: false);
        _settingsFacade.Theme.Save(pendingState);
        var savedState = _settingsFacade.Theme.Get();

        _isInitializing = true;
        try
        {
            ApplySavedState(savedState);
        }
        finally
        {
            _isInitializing = false;
        }

        RefreshMaterialModeOptions(_appearanceThemeService.GetCurrent());
        UpdatePreview(savedState);

        if (restartRequired)
        {
            RestartRequested?.Invoke(AppearanceRestartMessage);
        }
    }

    private ThemeAppearanceSettingsState BuildPendingState(bool usePickerSeed)
    {
        var themeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(SelectedThemeColorMode?.Value, ThemeColor);
        var themeColor = themeColorMode == ThemeAppearanceValues.ColorModeSeedMonet
            ? (usePickerSeed ? CustomSeedPickerValue.ToString() : string.IsNullOrWhiteSpace(ThemeColor) ? null : ThemeColor)
            : string.IsNullOrWhiteSpace(ThemeColor) ? null : ThemeColor;

        return new ThemeAppearanceSettingsState(
            IsNightMode,
            themeColor,
            UseSystemChrome,
            GlobalAppearanceSettings.NormalizeCornerRadiusScale(GlobalCornerRadiusScale),
            themeColorMode,
            ThemeAppearanceValues.NormalizeSystemMaterialMode(SelectedSystemMaterialMode?.Value),
            _selectedWallpaperSeed);
    }

    private void UpdatePreview(ThemeAppearanceSettingsState pendingState)
    {
        var preview = _appearanceThemeService.BuildPreview(pendingState);
        var normalizedMode = preview.ThemeColorMode;

        ShowNeutralPreview = normalizedMode == ThemeAppearanceValues.ColorModeDefaultNeutral;
        ShowMonetPreview = !ShowNeutralPreview;
        IsThemeColorEditable = normalizedMode == ThemeAppearanceValues.ColorModeSeedMonet;
        IsWallpaperMode = normalizedMode == ThemeAppearanceValues.ColorModeWallpaperMonet;

        PrimarySwatchBrush = new SolidColorBrush(preview.MonetPalette.Primary);
        SecondarySwatchBrush = new SolidColorBrush(preview.MonetPalette.Secondary);
        TertiarySwatchBrush = new SolidColorBrush(preview.MonetPalette.Tertiary);
        NeutralSwatchBrush = new SolidColorBrush(preview.MonetPalette.Neutral);
        SeedSwatchBrush = new SolidColorBrush(preview.EffectiveSeedColor);

        if (IsWallpaperMode)
        {
            WallpaperSeedCandidates = preview.WallpaperSeedCandidates
                .Select((color, index) => new ThemeSeedCandidateOption(
                    color.ToString(),
                    $"{PreviewSeedLabel} {index + 1}",
                    color,
                    string.Equals(color.ToString(), _selectedWallpaperSeed, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (WallpaperSeedCandidates.Count > 0 &&
                !string.Equals(_selectedWallpaperSeed, preview.EffectiveSeedColor.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _selectedWallpaperSeed = preview.EffectiveSeedColor.ToString();
                WallpaperSeedCandidates = preview.WallpaperSeedCandidates
                    .Select((color, index) => new ThemeSeedCandidateOption(
                        color.ToString(),
                        $"{PreviewSeedLabel} {index + 1}",
                        color,
                        string.Equals(color.ToString(), _selectedWallpaperSeed, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
            }

            IsWallpaperSeedSelectable = WallpaperSeedCandidates.Count > 1;
            ThemeColorSourceDescription = preview.ResolvedSeedSource switch
            {
                "app_wallpaper" or "app_video" or "app_solid" => ThemeSourceWallpaperAppDescription,
                "system_wallpaper" => ThemeSourceWallpaperSystemDescription,
                _ => ThemeSourceWallpaperFallbackDescription
            };
        }
        else
        {
            WallpaperSeedCandidates = [];
            IsWallpaperSeedSelectable = false;
            ThemeColorSourceDescription = normalizedMode switch
            {
                ThemeAppearanceValues.ColorModeDefaultNeutral => ThemeSourceDefaultDescription,
                _ => ThemeSourceUserColorDescription
            };
        }
    }

    private string ResolveMaterialModeLabel(string value)
    {
        return ThemeAppearanceValues.NormalizeSystemMaterialMode(value) switch
        {
            ThemeAppearanceValues.MaterialMica => SystemMaterialMicaText,
            ThemeAppearanceValues.MaterialAcrylic => SystemMaterialAcrylicText,
            _ => SystemMaterialNoneText
        };
    }

    private void SyncCustomSeedPickerWithSavedThemeColor()
    {
        CustomSeedPickerValue = !string.IsNullOrWhiteSpace(ThemeColor) && Color.TryParse(ThemeColor, out var parsedColor)
            ? parsedColor
            : DefaultSeedColor;
    }

    private IReadOnlyList<SelectionOption> CreateThemeColorModes()
    {
        return
        [
            new SelectionOption(ThemeAppearanceValues.ColorModeDefaultNeutral, ThemeSourceNeutralText),
            new SelectionOption(ThemeAppearanceValues.ColorModeSeedMonet, ThemeSourceUserColorText),
            new SelectionOption(ThemeAppearanceValues.ColorModeWallpaperMonet, ThemeSourceWallpaperText)
        ];
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class ComponentsSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public ComponentsSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        SpacingPresets = CreateSpacingPresets();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> SpacingPresets { get; }

    [ObservableProperty]
    private int _shortSideCells;

    [ObservableProperty]
    private int _edgeInsetPercent;

    [ObservableProperty]
    private SelectionOption _selectedSpacingPreset = new("Relaxed", "Relaxed");

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _componentsHeader = string.Empty;

    [ObservableProperty]
    private string _shortSideCellsLabel = string.Empty;

    [ObservableProperty]
    private string _edgeInsetPercentLabel = string.Empty;

    [ObservableProperty]
    private string _spacingPresetLabel = string.Empty;

    [ObservableProperty]
    private double _globalCornerRadiusScale = GlobalAppearanceSettings.DefaultCornerRadiusScale;

    [ObservableProperty]
    private string _componentRadiusHeader = string.Empty;

    [ObservableProperty]
    private string _globalCornerRadiusLabel = string.Empty;

    [ObservableProperty]
    private string _globalCornerRadiusDescription = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Grid.Get();
        ShortSideCells = state.ShortSideCells;
        EdgeInsetPercent = state.EdgeInsetPercent;
        var spacingPreset = _settingsFacade.Grid.NormalizeSpacingPreset(state.SpacingPreset);
        SelectedSpacingPreset = SpacingPresets.FirstOrDefault(option =>
            string.Equals(option.Value, spacingPreset, StringComparison.OrdinalIgnoreCase))
            ?? SpacingPresets[1];

        var theme = _settingsFacade.Theme.Get();
        GlobalCornerRadiusScale = GlobalAppearanceSettings.NormalizeCornerRadiusScale(theme.GlobalCornerRadiusScale);
    }

    partial void OnShortSideCellsChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnEdgeInsetPercentChanged(int value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnSelectedSpacingPresetChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveGrid();
    }

    partial void OnGlobalCornerRadiusScaleChanged(double value)
    {
        if (_isInitializing)
        {
            return;
        }

        var normalized = GlobalAppearanceSettings.NormalizeCornerRadiusScale(value);
        if (Math.Abs(normalized - value) > 0.0001d)
        {
            _isInitializing = true;
            try
            {
                GlobalCornerRadiusScale = normalized;
            }
            finally
            {
                _isInitializing = false;
            }

            return;
        }

        SaveComponentCornerRadius();
    }

    private void SaveGrid()
    {
        _settingsFacade.Grid.Save(new GridSettingsState(
            Math.Clamp(ShortSideCells, 6, 96),
            _settingsFacade.Grid.NormalizeSpacingPreset(SelectedSpacingPreset.Value),
            Math.Clamp(EdgeInsetPercent, 0, 30)));
    }

    private void SaveComponentCornerRadius()
    {
        var theme = _settingsFacade.Theme.Get();
        _settingsFacade.Theme.Save(new ThemeAppearanceSettingsState(
            theme.IsNightMode,
            theme.ThemeColor,
            theme.UseSystemChrome,
            GlobalAppearanceSettings.NormalizeCornerRadiusScale(GlobalCornerRadiusScale),
            theme.ThemeColorMode,
            theme.SystemMaterialMode,
            theme.SelectedWallpaperSeed));
    }

    private IReadOnlyList<SelectionOption> CreateSpacingPresets()
    {
        return
        [
            new SelectionOption("Compact", L("settings.components.spacing_compact", "Compact")),
            new SelectionOption("Relaxed", L("settings.components.spacing_relaxed", "Relaxed"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.components.title", "Components");
        PageDescription = L("settings.components.description", "Adjust component layout and corner design.");
        ComponentsHeader = L("settings.components.header", "Grid Settings");
        ShortSideCellsLabel = L("settings.components.short_side_label", "Short Side Cells");
        EdgeInsetPercentLabel = L("settings.components.edge_inset_label", "Screen Inset");
        SpacingPresetLabel = L("settings.components.spacing_label", "Component Spacing");
        ComponentRadiusHeader = L("settings.components.corner_radius.header", "Corner Design");
        GlobalCornerRadiusLabel = L("settings.components.corner_radius.label", "Component Corner Radius");
        GlobalCornerRadiusDescription = L("settings.components.corner_radius.description", "Adjust the shared corner radius used by component containers, and expand the internal safe area with it.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class InstalledPluginItemViewModel : ViewModelBase
{
    public InstalledPluginItemViewModel(InstalledPluginInfo info)
    {
        PluginId = info.Manifest.Id;
        Name = info.Manifest.Name;
        Version = info.Manifest.Version ?? "-";
        Description = info.Manifest.Description;
        ErrorMessage = info.ErrorMessage;
        IsLoaded = info.IsLoaded;
        IsPackage = info.IsPackage;
        IsEnabled = info.IsEnabled;
    }

    public string PluginId { get; }

    public string Name { get; }

    public string Version { get; }

    public string? Description { get; }

    public string? ErrorMessage { get; }

    public bool IsLoaded { get; }

    public bool IsPackage { get; }

    [ObservableProperty]
    private bool _isEnabled;
}

public sealed partial class PluginsSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;

    public PluginsSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();
        StatusMessage = L(
            "settings.plugins.initial_status",
            "Refresh plugin state to see the latest installed and marketplace entries.");
    }

    public event Action? RestartRequested;

    public ObservableCollection<InstalledPluginItemViewModel> InstalledPlugins { get; } = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _installedHeader = string.Empty;

    [ObservableProperty]
    private string _deleteButtonText = string.Empty;

    [ObservableProperty]
    private string _emptyInstalledText = string.Empty;

    [ObservableProperty]
    private string _restartRequiredMessage = string.Empty;

    public async Task InitializeAsync()
    {
        if (InstalledPlugins.Count > 0)
        {
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            InstalledPlugins.Clear();
            foreach (var plugin in _settingsFacade.PluginManagement.GetInstalledPlugins())
            {
                InstalledPlugins.Add(new InstalledPluginItemViewModel(plugin));
            }

            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.refresh_success_installed_format",
                    "Loaded {0} installed plugins."),
                InstalledPlugins.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePlugin(InstalledPluginItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_settingsFacade.PluginManagement.SetPluginEnabled(item.PluginId, item.IsEnabled))
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.toggle_result_format",
                    "Plugin '{0}' was {1} for the next launch. Restart the app to apply page and widget changes."),
                item.Name,
                item.IsEnabled
                    ? L("settings.plugins.toggle_state_enabled", "enabled")
                    : L("settings.plugins.toggle_state_disabled", "disabled"));
            RestartRequested?.Invoke();
        }
        else
        {
            item.IsEnabled = !item.IsEnabled;
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.plugins.toggle_unchanged_format", "Plugin '{0}' did not change."),
                item.Name);
        }
    }

    [RelayCommand]
    private void DeletePlugin(InstalledPluginItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_settingsFacade.PluginManagement.DeleteInstalledPlugin(item.PluginId))
        {
            InstalledPlugins.Remove(item);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "settings.plugins.delete_success_format",
                    "Plugin '{0}' was staged for deletion. Restart the app to finish removing it."),
                item.Name);
            RestartRequested?.Invoke();
        }
        else
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.plugins.delete_failed_name_format", "Failed to remove plugin '{0}'."),
                item.Name);
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.plugins.title", "Plugins");
        PageDescription = L("settings.plugins.description", "Manage installed plugins and review their runtime state.");
        RefreshButtonText = L("settings.plugins.refresh_button", "Refresh Plugins");
        InstalledHeader = L("settings.plugins.installed_header", "Installed Plugins");
        DeleteButtonText = L("settings.plugins.delete_button_short", "Delete");
        EmptyInstalledText = L("settings.plugins.empty", "No plugins found.");
        RestartRequiredMessage = L("settings.plugins.restart_required", "Plugin changes take effect after restart.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class AboutSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;

    public AboutSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();

        VersionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        CodenameText = _settingsFacade.ApplicationInfo.GetAppCodenameText();
        var backendInfo = _settingsFacade.ApplicationInfo.GetRenderBackendInfo();
        RenderBackendText = string.IsNullOrWhiteSpace(backendInfo.ImplementationTypeName)
            ? backendInfo.ActualBackend
            : $"{backendInfo.ActualBackend} ({backendInfo.ImplementationTypeName})";
    }

    [ObservableProperty]
    private string _versionText = "-";

    [ObservableProperty]
    private string _codenameText = "-";

    [ObservableProperty]
    private string _renderBackendText = "-";

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _appInfoHeader = string.Empty;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _codenameLabel = string.Empty;

    [ObservableProperty]
    private string _renderBackendLabel = string.Empty;

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.about.title", "About");
        PageDescription = L("settings.about.description", "Application details.");
        AppInfoHeader = L("settings.about.app_info_header", "Application Information");
        VersionLabel = L("settings.about.version_label", "Version");
        CodenameLabel = L("settings.about.codename_label", "Codename");
        RenderBackendLabel = L("settings.about.render_backend_label", "Render Backend");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class UpdateSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly UpdateWorkflowService _updateWorkflowService;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private readonly Version _currentVersion;
    private bool _isInitializing;
    private UpdateCheckResult? _lastCheckResult;

    public IReadOnlyList<SelectionOption> UpdateChannelOptions { get; }

    public IReadOnlyList<SelectionOption> UpdateSourceOptions { get; }

    public IReadOnlyList<SelectionOption> UpdateModeOptions { get; }

    public IReadOnlyList<SelectionOption> DownloadThreadOptions { get; }

    public UpdateSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        UpdateWorkflowService? updateWorkflowService = null)
    {
        _settingsFacade = settingsFacade;
        _updateWorkflowService = updateWorkflowService ?? HostUpdateWorkflowServiceProvider.GetOrCreate();
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        RefreshLocalizedText();
        UpdateChannelOptions = CreateUpdateChannelOptions();
        UpdateSourceOptions = CreateUpdateSourceOptions();
        UpdateModeOptions = CreateUpdateModeOptions();
        DownloadThreadOptions = CreateDownloadThreadOptions();

        var versionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        _currentVersion = Version.TryParse(versionText, out var parsedVersion)
            ? parsedVersion
            : new Version(0, 0, 0);

        CurrentVersionText = versionText;
        LoadStateFromSettings();
    }

    [ObservableProperty]
    private string _selectedUpdateChannelValue = UpdateSettingsValues.ChannelStable;

    [ObservableProperty]
    private string _selectedUpdateSourceValue = UpdateSettingsValues.DownloadSourceGitHub;

    [ObservableProperty]
    private string _selectedUpdateModeValue = UpdateSettingsValues.ModeDownloadThenConfirm;

    [ObservableProperty]
    private string _currentVersionText = "-";

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgressValue;

    [ObservableProperty]
    private bool _isDownloadProgressVisible;

    [ObservableProperty]
    private string _downloadProgressText = string.Empty;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _statusCardTitle = string.Empty;

    [ObservableProperty]
    private string _statusCardDescription = string.Empty;

    [ObservableProperty]
    private string _preferencesHeader = string.Empty;

    [ObservableProperty]
    private string _preferencesDescription = string.Empty;

    [ObservableProperty]
    private string _updateChannelLabel = string.Empty;

    [ObservableProperty]
    private string _updateSourceLabel = string.Empty;

    [ObservableProperty]
    private string _updateModeLabel = string.Empty;

    [ObservableProperty]
    private string _currentVersionLabel = string.Empty;

    [ObservableProperty]
    private string _latestVersionLabel = string.Empty;

    [ObservableProperty]
    private string _publishedAtLabel = string.Empty;

    [ObservableProperty]
    private string _lastCheckedLabel = string.Empty;

    [ObservableProperty]
    private string _checkForUpdatesButtonText = string.Empty;

    [ObservableProperty]
    private string _downloadButtonText = string.Empty;

    [ObservableProperty]
    private string _installNowButtonText = string.Empty;

    [ObservableProperty]
    private string _latestVersionText = string.Empty;

    [ObservableProperty]
    private string _publishedAtText = string.Empty;

    [ObservableProperty]
    private string _lastCheckedText = string.Empty;

    [ObservableProperty]
    private bool _isLatestVersionVisible;

    [ObservableProperty]
    private bool _isPublishedAtVisible;

    [ObservableProperty]
    private bool _isLastCheckedVisible;

    [ObservableProperty]
    private bool _hasPendingInstaller;

    [ObservableProperty]
    private double _downloadThreadsSliderValue = UpdateSettingsValues.DefaultDownloadThreads;

    [ObservableProperty]
    private string _selectedUpdateChannelDescription = string.Empty;

    [ObservableProperty]
    private string _selectedUpdateModeDescription = string.Empty;

    [ObservableProperty]
    private string _selectedUpdateSourceDescription = string.Empty;

    [ObservableProperty]
    private string _downloadThreadsLabel = string.Empty;

    [ObservableProperty]
    private string _downloadThreadsDescription = string.Empty;

    [ObservableProperty]
    private string _stableChannelText = string.Empty;

    [ObservableProperty]
    private string _previewChannelText = string.Empty;

    [ObservableProperty]
    private string _gitHubSourceText = string.Empty;

    [ObservableProperty]
    private string _ghProxySourceText = string.Empty;

    [ObservableProperty]
    private string _manualModeText = string.Empty;

    [ObservableProperty]
    private string _downloadThenConfirmModeText = string.Empty;

    [ObservableProperty]
    private string _silentOnExitModeText = string.Empty;

    [ObservableProperty]
    private SelectionOption? _selectedUpdateChannelOption;

    [ObservableProperty]
    private SelectionOption? _selectedUpdateSourceOption;

    [ObservableProperty]
    private SelectionOption? _selectedUpdateModeOption;

    [ObservableProperty]
    private SelectionOption? _selectedDownloadThreadsOption;

    [ObservableProperty]
    private string _downloadThreadsText = UpdateSettingsValues.DefaultDownloadThreads.ToString(CultureInfo.CurrentCulture);

    public bool IsStableChannelSelected =>
        string.Equals(SelectedUpdateChannelValue, UpdateSettingsValues.ChannelStable, StringComparison.OrdinalIgnoreCase);

    public bool IsPreviewChannelSelected =>
        string.Equals(SelectedUpdateChannelValue, UpdateSettingsValues.ChannelPreview, StringComparison.OrdinalIgnoreCase);

    public bool IsGitHubSourceSelected =>
        string.Equals(SelectedUpdateSourceValue, UpdateSettingsValues.DownloadSourceGitHub, StringComparison.OrdinalIgnoreCase);

    public bool IsGhProxySourceSelected =>
        string.Equals(SelectedUpdateSourceValue, UpdateSettingsValues.DownloadSourceGhProxy, StringComparison.OrdinalIgnoreCase);

    public bool IsManualModeSelected =>
        string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase);

    public bool IsDownloadThenConfirmModeSelected =>
        string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeDownloadThenConfirm, StringComparison.OrdinalIgnoreCase);

    public bool IsSilentOnExitModeSelected =>
        string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase);

    public bool IsDownloadButtonVisible =>
        !HasPendingInstaller &&
        _lastCheckResult is { Success: true, IsUpdateAvailable: true, PreferredAsset: not null };

    public bool IsInstallButtonVisible => HasPendingInstaller;

    public string DownloadThreadsValueText =>
        UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue)).ToString(CultureInfo.CurrentCulture);

    private bool IsBusy => IsCheckingForUpdates || IsDownloading;

    partial void OnSelectedUpdateChannelOptionChanged(SelectionOption? value)
    {
        if (value is not null &&
            !string.Equals(SelectedUpdateChannelValue, value.Value, StringComparison.OrdinalIgnoreCase))
        {
            SelectedUpdateChannelValue = value.Value;
        }
    }

    partial void OnSelectedUpdateSourceOptionChanged(SelectionOption? value)
    {
        if (value is not null &&
            !string.Equals(SelectedUpdateSourceValue, value.Value, StringComparison.OrdinalIgnoreCase))
        {
            SelectedUpdateSourceValue = value.Value;
        }
    }

    partial void OnSelectedUpdateModeOptionChanged(SelectionOption? value)
    {
        if (value is not null &&
            !string.Equals(SelectedUpdateModeValue, value.Value, StringComparison.OrdinalIgnoreCase))
        {
            SelectedUpdateModeValue = value.Value;
        }
    }

    partial void OnSelectedDownloadThreadsOptionChanged(SelectionOption? value)
    {
        if (value is null || !int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return;
        }

        ApplyDownloadThreadsValue(parsed, !_isInitializing);
    }

    partial void OnSelectedUpdateChannelValueChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        _lastCheckResult = null;
        if (!HasPendingInstaller)
        {
            LatestVersionText = string.Empty;
            PublishedAtText = string.Empty;
            IsLatestVersionVisible = false;
            IsPublishedAtVisible = false;
        }

        SaveUpdateSettings();
        UpdateStatus = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.update.status_channel_changed_format", "Update channel switched to {0}. Please check again."),
            string.Equals(value, UpdateSettingsValues.ChannelPreview, StringComparison.OrdinalIgnoreCase)
                ? L("settings.update.channel_preview", "Preview")
                : L("settings.update.channel_stable", "Stable"));
        SelectedUpdateChannelDescription = BuildUpdateChannelDescription(value);
        SyncSelectedOptions();
        RefreshActionState();
    }

    partial void OnSelectedUpdateSourceValueChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
        SelectedUpdateSourceDescription = BuildUpdateSourceDescription(value);
        UpdateStatus = L("settings.update.status_preferences_saved", "Update preferences saved.");
        SyncSelectedOptions();
    }

    partial void OnSelectedUpdateModeValueChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
        SelectedUpdateModeDescription = BuildUpdateModeDescription(value);
        UpdateStatus = HasPendingInstaller
            ? BuildPendingReadyStatus()
            : L("settings.update.status_preferences_saved", "Update preferences saved.");
        SyncSelectedOptions();
        RefreshActionState();
    }

    partial void OnDownloadThreadsSliderValueChanged(double value)
    {
        var normalized = UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(value));
        if (Math.Abs(value - normalized) > double.Epsilon)
        {
            DownloadThreadsSliderValue = normalized;
            return;
        }

        OnPropertyChanged(nameof(DownloadThreadsValueText));
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
        UpdateStatus = L("settings.update.status_preferences_saved", "Update preferences saved.");
        SyncSelectedOptions();
    }

    partial void OnDownloadThreadsTextChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        if (!TryParseDownloadThreads(value, out var parsed))
        {
            return;
        }

        ApplyDownloadThreadsValue(parsed, true);
    }

    partial void OnHasPendingInstallerChanged(bool value)
    {
        RefreshActionState();
        if (!value)
        {
            UpdateStatus = L("settings.update.status_ready", "Ready to check for updates.");
        }
    }

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadLatestReleaseCommand.NotifyCanExecuteChanged();
        InstallPendingUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadLatestReleaseCommand.NotifyCanExecuteChanged();
        InstallPendingUpdateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectStableChannel()
    {
        SelectedUpdateChannelValue = UpdateSettingsValues.ChannelStable;
    }

    [RelayCommand]
    private void SelectPreviewChannel()
    {
        SelectedUpdateChannelValue = UpdateSettingsValues.ChannelPreview;
    }

    [RelayCommand]
    private void SelectGitHubSource()
    {
        SelectedUpdateSourceValue = UpdateSettingsValues.DownloadSourceGitHub;
    }

    [RelayCommand]
    private void SelectGhProxySource()
    {
        SelectedUpdateSourceValue = UpdateSettingsValues.DownloadSourceGhProxy;
    }

    [RelayCommand]
    private void SelectManualMode()
    {
        SelectedUpdateModeValue = UpdateSettingsValues.ModeManual;
    }

    [RelayCommand]
    private void SelectDownloadThenConfirmMode()
    {
        SelectedUpdateModeValue = UpdateSettingsValues.ModeDownloadThenConfirm;
    }

    [RelayCommand]
    private void SelectSilentOnExitMode()
    {
        SelectedUpdateModeValue = UpdateSettingsValues.ModeSilentOnExit;
    }

    private void SaveUpdateSettings()
    {
        var current = _settingsFacade.Update.Get();
        _settingsFacade.Update.Save(current with
        {
            IncludePrereleaseUpdates = string.Equals(
                SelectedUpdateChannelValue,
                UpdateSettingsValues.ChannelPreview,
                StringComparison.OrdinalIgnoreCase),
            UpdateChannel = SelectedUpdateChannelValue,
            UpdateMode = SelectedUpdateModeValue,
            UpdateDownloadSource = SelectedUpdateSourceValue,
            UpdateDownloadThreads = UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue))
        });
    }

    private bool CanCheckForUpdates() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            IsCheckingForUpdates = true;
            IsDownloadProgressVisible = false;
            DownloadProgressValue = 0;
            DownloadProgressText = L("settings.update.download_progress_idle", "Download progress: -");
            UpdateStatus = L("settings.update.status_checking", "Checking GitHub releases...");

            var result = await _updateWorkflowService.CheckForUpdatesAsync(_currentVersion);
            _lastCheckResult = result.Success ? result : null;
            RefreshLastCheckedFromSettings();

            if (!result.Success)
            {
                UpdateStatus = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("settings.update.status_check_failed", "Failed to check for updates.")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("settings.update.status_check_failed_format", "Update check failed: {0}"),
                        result.ErrorMessage);
                return;
            }

            ApplyCheckResultDisplay(result);
            if (!result.IsUpdateAvailable)
            {
                return;
            }

            if (result.PreferredAsset is null)
            {
                UpdateStatus = L(
                    "settings.update.status_asset_missing",
                    "A new release is available, but no compatible installer was found.");
                return;
            }

            if (!string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase))
            {
                await DownloadLatestReleaseCoreAsync(result, invokedFromCheck: true);
                return;
            }

            UpdateStatus = string.Format(
                CultureInfo.CurrentCulture,
                L("settings.update.status_available_format", "New version {0} is available. Click Download & Install."),
                result.LatestVersionText);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanDownloadLatestRelease() => !IsBusy && IsDownloadButtonVisible;

    [RelayCommand(CanExecute = nameof(CanDownloadLatestRelease))]
    private async Task DownloadLatestReleaseAsync()
    {
        await DownloadLatestReleaseCoreAsync(_lastCheckResult, invokedFromCheck: false);
    }

    private bool CanInstallPendingUpdate() => !IsBusy && HasPendingInstaller;

    [RelayCommand(CanExecute = nameof(CanInstallPendingUpdate))]
    private void InstallPendingUpdate()
    {
        var result = _updateWorkflowService.LaunchPendingInstallerNow();
        if (result.Success)
        {
            UpdateStatus = L(
                "settings.update.status_installer_started",
                "Installer started. The app will close for update.");
            HasPendingInstaller = false;
            return;
        }

        UpdateStatus = result.UserCancelledElevation
            ? L(
                "settings.update.status_elevation_cancelled",
                "Administrator permission was not granted. Update was cancelled.")
            : string.Format(
                CultureInfo.CurrentCulture,
                L("settings.update.status_launch_failed_format", "Failed to start installer: {0}"),
                result.ErrorMessage ?? L("settings.update.status_installer_missing", "Installer file was not found after download."));
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.update.title", "Update");
        PageDescription = L("settings.update.description", "Update checks and release channel preferences.");
        StatusCardTitle = L("settings.update.status_card_title", "Update Status");
        StatusCardDescription = L("settings.update.status_card_description", "Check for updates and review the latest release information.");
        PreferencesHeader = L("settings.update.preferences_header", "Update Preferences");
        PreferencesDescription = L("settings.update.preferences_description", "Choose your release channel, download source, behavior, and download speed.");
        UpdateChannelLabel = L("settings.update.channel_label", "Update Channel");
        UpdateSourceLabel = L("settings.update.source_label", "Download Source");
        UpdateModeLabel = L("settings.update.mode_label", "Update Mode");
        DownloadThreadsLabel = L("settings.update.download_threads_label", "Download Threads");
        DownloadThreadsDescription = L("settings.update.download_threads_desc", "Choose how many parallel download threads are used for application updates.");
        CheckForUpdatesButtonText = L("settings.update.check_button", "Check for Updates");
        DownloadButtonText = L("settings.update.download_install_button", "Download & Install");
        InstallNowButtonText = L("settings.update.install_now_button", "Install Now");
        CurrentVersionLabel = L("settings.update.current_version_label", "Current Version");
        LatestVersionLabel = L("settings.update.latest_version_label", "Latest Release");
        PublishedAtLabel = L("settings.update.published_at_label", "Published At");
        LastCheckedLabel = L("settings.update.last_checked_label", "Last Checked");
        StableChannelText = L("settings.update.channel_stable", "Stable");
        PreviewChannelText = L("settings.update.channel_preview", "Preview");
        GitHubSourceText = L("settings.update.source_github", "GitHub");
        GhProxySourceText = L("settings.update.source_ghproxy", "gh-proxy");
        ManualModeText = L("settings.update.mode_manual", "Manual Update");
        DownloadThenConfirmModeText = L("settings.update.mode_download_then_confirm", "Silent Download");
        SilentOnExitModeText = L("settings.update.mode_silent_on_exit", "Silent Install");
        SelectedUpdateChannelDescription = BuildUpdateChannelDescription(SelectedUpdateChannelValue);
        SelectedUpdateModeDescription = BuildUpdateModeDescription(SelectedUpdateModeValue);
        SelectedUpdateSourceDescription = BuildUpdateSourceDescription(SelectedUpdateSourceValue);
    }

    private void LoadStateFromSettings()
    {
        var update = _settingsFacade.Update.Get();
        _isInitializing = true;
        SelectedUpdateChannelValue = UpdateSettingsValues.NormalizeChannel(update.UpdateChannel, update.IncludePrereleaseUpdates);
        SelectedUpdateSourceValue = UpdateSettingsValues.NormalizeDownloadSource(update.UpdateDownloadSource);
        SelectedUpdateModeValue = UpdateSettingsValues.NormalizeMode(update.UpdateMode);
        DownloadThreadsSliderValue = UpdateSettingsValues.NormalizeDownloadThreads(update.UpdateDownloadThreads);
        DownloadThreadsText = ((int)Math.Round(DownloadThreadsSliderValue)).ToString(CultureInfo.CurrentCulture);
        _isInitializing = false;

        SyncSelectedOptions();
        RefreshLastCheckedFromSettings();
        ApplyPendingState(update);
        if (!HasPendingInstaller)
        {
            UpdateStatus = L("settings.update.status_idle", "No update check has been performed yet.");
        }

        RefreshActionState();
    }

    private void RefreshLastCheckedFromSettings()
    {
        var update = _settingsFacade.Update.Get();
        LastCheckedText = FormatTimestamp(update.LastUpdateCheckUtcMs);
        IsLastCheckedVisible = !string.IsNullOrWhiteSpace(LastCheckedText);
    }

    private void ApplyPendingState(UpdateSettingsState update)
    {
        var pending = _updateWorkflowService.GetPendingUpdate();
        HasPendingInstaller = pending is not null;
        if (pending is null)
        {
            return;
        }

        LatestVersionText = pending.VersionText;
        IsLatestVersionVisible = !string.IsNullOrWhiteSpace(LatestVersionText);
        PublishedAtText = pending.PublishedAt is null ? string.Empty : FormatTimestamp(pending.PublishedAt.Value.ToUnixTimeMilliseconds());
        IsPublishedAtVisible = !string.IsNullOrWhiteSpace(PublishedAtText);
        UpdateStatus = BuildPendingReadyStatus();
    }

    private void ApplyCheckResultDisplay(UpdateCheckResult result)
    {
        if (result.IsUpdateAvailable)
        {
            LatestVersionText = result.LatestVersionText;
            IsLatestVersionVisible = !string.IsNullOrWhiteSpace(LatestVersionText);
            PublishedAtText = result.Release is null || result.Release.PublishedAt == DateTimeOffset.MinValue
                ? string.Empty
                : FormatTimestamp(result.Release.PublishedAt.ToUnixTimeMilliseconds());
            IsPublishedAtVisible = !string.IsNullOrWhiteSpace(PublishedAtText);
            return;
        }

        LatestVersionText = string.Empty;
        PublishedAtText = string.Empty;
        IsLatestVersionVisible = false;
        IsPublishedAtVisible = false;
        UpdateStatus = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.update.status_up_to_date_format", "You are up to date ({0})."),
            result.CurrentVersionText);
    }

    private async Task DownloadLatestReleaseCoreAsync(UpdateCheckResult? result, bool invokedFromCheck)
    {
        if (result is null || !result.Success || !result.IsUpdateAvailable || result.PreferredAsset is null)
        {
            return;
        }

        try
        {
            IsDownloading = true;
            IsDownloadProgressVisible = true;
            DownloadProgressValue = 0;
            DownloadProgressText = L("settings.update.download_progress_idle", "Download progress: -");
            UpdateStatus = L("settings.update.status_downloading", "Downloading installer...");

            var progress = new Progress<double>(value =>
            {
                DownloadProgressValue = Math.Clamp(value * 100d, 0d, 100d);
                DownloadProgressText = string.Format(
                    CultureInfo.CurrentCulture,
                    L("settings.update.download_progress_format", "Download progress: {0:F0}%"),
                    DownloadProgressValue);
            });

            var downloadResult = await _updateWorkflowService.DownloadReleaseAsync(result, progress);
            if (!downloadResult.Success)
            {
                UpdateStatus = string.Format(
                    CultureInfo.CurrentCulture,
                    L("settings.update.status_download_failed_format", "Download failed: {0}"),
                    downloadResult.ErrorMessage ?? L("settings.update.status_check_failed", "Failed to check for updates."));
                return;
            }

            ApplyPendingState(_settingsFacade.Update.Get());
            UpdateStatus = BuildPendingReadyStatus();
            if (!invokedFromCheck)
            {
                _lastCheckResult = result;
            }
        }
        finally
        {
            IsDownloading = false;
            IsDownloadProgressVisible = false;
        }
    }

    private string BuildPendingReadyStatus()
    {
        return string.Equals(SelectedUpdateModeValue, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase)
            ? L("settings.update.status_downloaded_exit", "Update downloaded. It will be installed when you exit the app.")
            : L("settings.update.status_downloaded_confirm", "Update downloaded. Review it and choose when to install.");
    }

    private string BuildUpdateModeDescription(string? value)
    {
        return UpdateSettingsValues.NormalizeMode(value) switch
        {
            UpdateSettingsValues.ModeManual => L(
                "settings.update.mode_manual_desc",
                "Only check for updates. You decide when downloads and installation happen."),
            UpdateSettingsValues.ModeSilentOnExit => L(
                "settings.update.mode_silent_on_exit_desc",
                "Download updates in the background and install them the next time you exit the app."),
            _ => L(
                "settings.update.mode_download_then_confirm_desc",
                "Download updates in the background and ask for confirmation before installing them.")
        };
    }

    private string BuildUpdateChannelDescription(string? value)
    {
        return UpdateSettingsValues.NormalizeChannel(value) switch
        {
            UpdateSettingsValues.ChannelPreview => L(
                "settings.update.channel_preview_desc",
                "Preview builds may contain newer features but can be less stable."),
            _ => L(
                "settings.update.channel_stable_desc",
                "Stable builds prioritize reliability and are recommended for most users.")
        };
    }

    private string BuildUpdateSourceDescription(string? value)
    {
        return UpdateSettingsValues.NormalizeDownloadSource(value) switch
        {
            UpdateSettingsValues.DownloadSourceGhProxy => L(
                "settings.update.source_ghproxy_desc",
                "Use the gh-proxy mirror when downloading GitHub release assets."),
            _ => L(
                "settings.update.source_github_desc",
                "Download release assets directly from GitHub.")
        };
    }

    private string FormatTimestamp(long? utcMs)
    {
        if (utcMs is not > 0)
        {
            return string.Empty;
        }

        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(utcMs.Value)
                .ToLocalTime()
                .ToString("g", CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private void RefreshActionState()
    {
        OnPropertyChanged(nameof(IsDownloadButtonVisible));
        OnPropertyChanged(nameof(IsInstallButtonVisible));
        OnPropertyChanged(nameof(DownloadThreadsValueText));
    }

    private IReadOnlyList<SelectionOption> CreateUpdateChannelOptions()
    {
        return
        [
            new SelectionOption(UpdateSettingsValues.ChannelStable, StableChannelText),
            new SelectionOption(UpdateSettingsValues.ChannelPreview, PreviewChannelText)
        ];
    }

    private IReadOnlyList<SelectionOption> CreateUpdateSourceOptions()
    {
        return
        [
            new SelectionOption(UpdateSettingsValues.DownloadSourceGitHub, GitHubSourceText),
            new SelectionOption(UpdateSettingsValues.DownloadSourceGhProxy, GhProxySourceText)
        ];
    }

    private IReadOnlyList<SelectionOption> CreateUpdateModeOptions()
    {
        return
        [
            new SelectionOption(UpdateSettingsValues.ModeManual, ManualModeText),
            new SelectionOption(UpdateSettingsValues.ModeDownloadThenConfirm, DownloadThenConfirmModeText),
            new SelectionOption(UpdateSettingsValues.ModeSilentOnExit, SilentOnExitModeText)
        ];
    }

    private IReadOnlyList<SelectionOption> CreateDownloadThreadOptions()
    {
        return Enumerable
            .Range(UpdateSettingsValues.MinDownloadThreads, UpdateSettingsValues.MaxDownloadThreads)
            .Select(value => new SelectionOption(
                value.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.CurrentCulture)))
            .ToList();
    }

    private void SyncSelectedOptions()
    {
        SelectedUpdateChannelOption = UpdateChannelOptions.FirstOrDefault(option =>
            string.Equals(option.Value, SelectedUpdateChannelValue, StringComparison.OrdinalIgnoreCase));
        SelectedUpdateSourceOption = UpdateSourceOptions.FirstOrDefault(option =>
            string.Equals(option.Value, SelectedUpdateSourceValue, StringComparison.OrdinalIgnoreCase));
        SelectedUpdateModeOption = UpdateModeOptions.FirstOrDefault(option =>
            string.Equals(option.Value, SelectedUpdateModeValue, StringComparison.OrdinalIgnoreCase));
        SelectedDownloadThreadsOption = DownloadThreadOptions.FirstOrDefault(option =>
            string.Equals(
                option.Value,
                UpdateSettingsValues.NormalizeDownloadThreads((int)Math.Round(DownloadThreadsSliderValue)).ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyDownloadThreadsValue(int value, bool saveChanges)
    {
        var normalized = UpdateSettingsValues.NormalizeDownloadThreads(value);
        var normalizedText = normalized.ToString(CultureInfo.CurrentCulture);

        var previousInitializing = _isInitializing;
        _isInitializing = true;
        DownloadThreadsSliderValue = normalized;
        DownloadThreadsText = normalizedText;
        _isInitializing = previousInitializing;
        SyncSelectedOptions();

        if (saveChanges)
        {
            SaveUpdateSettings();
            UpdateStatus = L("settings.update.status_preferences_saved", "Update preferences saved.");
        }
    }

    private static bool TryParseDownloadThreads(string? value, out int parsed)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            return true;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed class PluginGeneratedSettingsPageViewModel
{
    public PluginGeneratedSettingsPageViewModel(
        ISettingsService settingsService,
        string pluginId,
        PluginSettingsSectionRegistration section,
        PluginLocalizer localizer)
    {
        SettingsService = settingsService;
        PluginId = pluginId;
        Section = section;
        Localizer = localizer;
        Title = localizer.GetString(section.TitleLocalizationKey, section.TitleLocalizationKey);
        Description = string.IsNullOrWhiteSpace(section.DescriptionLocalizationKey)
            ? null
            : localizer.GetString(section.DescriptionLocalizationKey, section.DescriptionLocalizationKey);
    }

    public ISettingsService SettingsService { get; }

    public string PluginId { get; }

    public PluginSettingsSectionRegistration Section { get; }

    public PluginLocalizer Localizer { get; }

    public string Title { get; }

    public string? Description { get; }
}
