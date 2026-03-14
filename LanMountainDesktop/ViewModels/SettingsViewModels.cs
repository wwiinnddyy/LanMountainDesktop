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
        RefreshPreview();
        if (_isInitializing || value is null)
        {
            return;
        }

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
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public AppearanceSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        WallpaperPlacements = CreateWallpaperPlacements();
        ClockFormats = CreateClockFormats();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> WallpaperPlacements { get; }

    public IReadOnlyList<SelectionOption> ClockFormats { get; }

    [ObservableProperty]
    private bool _isNightMode;

    [ObservableProperty]
    private string _themeColor = string.Empty;

    [ObservableProperty]
    private Color _themeColorPickerValue;

    partial void OnThemeColorPickerValueChanged(Color value)
    {
        if (_isInitializing)
        {
            return;
        }

        ThemeColor = value.ToString();
    }

    [ObservableProperty]
    private bool _useSystemChrome;

    [ObservableProperty]
    private string _wallpaperPath = string.Empty;

    [ObservableProperty]
    private SelectionOption _selectedWallpaperPlacement = new("Fill", "Fill");

    [ObservableProperty]
    private bool _showClock = true;

    [ObservableProperty]
    private SelectionOption _selectedClockFormat = new("HourMinuteSecond", "Hour:Minute:Second");

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
    private string _themeHeader = string.Empty;

    [ObservableProperty]
    private string _wallpaperHeader = string.Empty;

    [ObservableProperty]
    private string _wallpaperPathLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperPlacementLabel = string.Empty;

    [ObservableProperty]
    private string _importWallpaperButtonText = string.Empty;

    [ObservableProperty]
    private string _clockHeader = string.Empty;

    [ObservableProperty]
    private string _clockDescription = string.Empty;

    [ObservableProperty]
    private string _clockFormatLabel = string.Empty;

    [ObservableProperty]
    private string _filePickerTitle = string.Empty;

    public void Load()
    {
        var theme = _settingsFacade.Theme.Get();
        IsNightMode = theme.IsNightMode;
        ThemeColor = theme.ThemeColor ?? string.Empty;
        if (Color.TryParse(ThemeColor, out var color))
        {
            ThemeColorPickerValue = color;
        }
        else
        {
            ThemeColorPickerValue = Color.Parse("#FF3B82F6");
        }
        UseSystemChrome = theme.UseSystemChrome;

        var wallpaper = _settingsFacade.Wallpaper.Get();
        WallpaperPath = wallpaper.WallpaperPath ?? string.Empty;
        var wallpaperPlacement = string.IsNullOrWhiteSpace(wallpaper.Placement)
            ? "Fill"
            : wallpaper.Placement;
        SelectedWallpaperPlacement = WallpaperPlacements.FirstOrDefault(option =>
            string.Equals(option.Value, wallpaperPlacement, StringComparison.OrdinalIgnoreCase))
            ?? WallpaperPlacements[0];

        var statusBar = _settingsFacade.StatusBar.Get();
        ShowClock = statusBar.TopStatusComponentIds.Any(id =>
            string.Equals(id, BuiltInComponentIds.Clock, StringComparison.OrdinalIgnoreCase));
        var clockFormat = string.IsNullOrWhiteSpace(statusBar.ClockDisplayFormat)
            ? "HourMinuteSecond"
            : statusBar.ClockDisplayFormat;
        SelectedClockFormat = ClockFormats.FirstOrDefault(option =>
            string.Equals(option.Value, clockFormat, StringComparison.OrdinalIgnoreCase))
            ?? ClockFormats[1];
    }

    public async Task ImportWallpaperAsync(string sourcePath)
    {
        var importedPath = await _settingsFacade.WallpaperMedia.ImportAssetAsync(sourcePath);
        if (!string.IsNullOrWhiteSpace(importedPath))
        {
            WallpaperPath = importedPath;
        }
    }

    partial void OnIsNightModeChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveTheme();
    }

    partial void OnThemeColorChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value) || Color.TryParse(value, out _))
        {
            SaveTheme();
        }
    }

    partial void OnUseSystemChromeChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveTheme();
    }

    partial void OnWallpaperPathChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveWallpaper();
    }

    partial void OnSelectedWallpaperPlacementChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveWallpaper();
    }

    partial void OnShowClockChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveStatusBar();
    }

    partial void OnSelectedClockFormatChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveStatusBar();
    }

    private void SaveTheme()
    {
        _settingsFacade.Theme.Save(new ThemeAppearanceSettingsState(
            IsNightMode,
            string.IsNullOrWhiteSpace(ThemeColor) ? null : ThemeColor,
            UseSystemChrome));
    }

    private void SaveWallpaper()
    {
        var current = _settingsFacade.Wallpaper.Get();
        _settingsFacade.Wallpaper.Save(new WallpaperSettingsState(
            string.IsNullOrWhiteSpace(WallpaperPath) ? null : WallpaperPath,
            current.Type,
            current.Color,
            SelectedWallpaperPlacement.Value));
    }

    private void SaveStatusBar()
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
            state.SpacingMode,
            state.CustomSpacingPercent));
    }

    private IReadOnlyList<SelectionOption> CreateWallpaperPlacements()
    {
        return
        [
            new SelectionOption("Fill", L("settings.wallpaper.placement.fill", "Fill")),
            new SelectionOption("Fit", L("settings.wallpaper.placement.fit", "Fit")),
            new SelectionOption("Stretch", L("settings.wallpaper.placement.stretch", "Stretch")),
            new SelectionOption("Center", L("settings.wallpaper.placement.center", "Center")),
            new SelectionOption("Tile", L("settings.wallpaper.placement.tile", "Tile"))
        ];
    }

    private IReadOnlyList<SelectionOption> CreateClockFormats()
    {
        return
        [
            new SelectionOption("HourMinute", L("settings.status_bar.clock_format.hm", "Hour:Minute")),
            new SelectionOption("HourMinuteSecond", L("settings.status_bar.clock_format.hms", "Hour:Minute:Second"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.appearance.title", "Appearance");
        PageDescription = L("settings.appearance.description", "Theme and status bar presentation.");
        ThemeHeader = L("settings.appearance.theme_header", "Theme");
        NightModeLabel = L("settings.color.enable_night_mode_toggle", "Enable night mode");
        UseSystemChromeLabel = L("settings.color.use_system_chrome_toggle", "Use system window chrome");
        ThemeColorLabel = L("settings.color.theme_color_label", "Theme Accent Color");
        WallpaperHeader = L("settings.wallpaper.title", "Wallpaper");
        WallpaperPathLabel = L("settings.wallpaper.current_label", "Current Wallpaper");
        WallpaperPlacementLabel = L("settings.wallpaper.placement_label", "Placement");
        ImportWallpaperButtonText = L("settings.wallpaper.pick_button", "Import Wallpaper");
        ClockHeader = L("settings.status_bar.clock_header", "Clock Component");
        ClockDescription = L("settings.status_bar.clock_description", "Display a clock on the top status bar.");
        ClockFormatLabel = L("settings.status_bar.clock_format_label", "Clock Format");
        FilePickerTitle = L("filepicker.title", "Select wallpaper");
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
    private string _gridHeader = string.Empty;

    [ObservableProperty]
    private string _shortSideCellsLabel = string.Empty;

    [ObservableProperty]
    private string _edgeInsetPercentLabel = string.Empty;

    [ObservableProperty]
    private string _spacingPresetLabel = string.Empty;

    public void Load()
    {
        var state = _settingsFacade.Grid.Get();
        ShortSideCells = state.ShortSideCells;
        EdgeInsetPercent = state.EdgeInsetPercent;
        var spacingPreset = _settingsFacade.Grid.NormalizeSpacingPreset(state.SpacingPreset);
        SelectedSpacingPreset = SpacingPresets.FirstOrDefault(option =>
            string.Equals(option.Value, spacingPreset, StringComparison.OrdinalIgnoreCase))
            ?? SpacingPresets[1];
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

    private void SaveGrid()
    {
        _settingsFacade.Grid.Save(new GridSettingsState(
            Math.Clamp(ShortSideCells, 6, 96),
            _settingsFacade.Grid.NormalizeSpacingPreset(SelectedSpacingPreset.Value),
            Math.Clamp(EdgeInsetPercent, 0, 30)));
    }

    private IReadOnlyList<SelectionOption> CreateSpacingPresets()
    {
        return
        [
            new SelectionOption("Compact", L("settings.grid.spacing_compact", "Compact")),
            new SelectionOption("Relaxed", L("settings.grid.spacing_relaxed", "Relaxed"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.components.title", "Components");
        PageDescription = L("settings.components.description", "Desktop grid and widget placement density.");
        GridHeader = L("settings.components.grid_header", "Grid Layout");
        ShortSideCellsLabel = L("settings.grid.short_side_label", "Short Side Cells");
        EdgeInsetPercentLabel = L("settings.grid.edge_inset_label", "Screen Inset");
        SpacingPresetLabel = L("settings.grid.spacing_label", "Grid Spacing");
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
    private bool _autoCheckUpdates;

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
    private string _autoCheckUpdatesLabel = string.Empty;

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

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
    }

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
            AutoCheckUpdates = AutoCheckUpdates,
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
        AutoCheckUpdatesLabel = L("settings.update.auto_check_toggle", "Automatically check for updates on startup");
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
        AutoCheckUpdates = update.AutoCheckUpdates;
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
