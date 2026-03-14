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

public sealed class PluginMarketItemViewModel
{
    public PluginMarketItemViewModel(PluginMarketPluginInfo plugin)
    {
        PluginId = plugin.Id;
        Name = plugin.Name;
        Description = plugin.Description;
        Version = plugin.Version;
        Author = plugin.Author;
        ApiVersion = plugin.ApiVersion;
    }

    public string PluginId { get; }

    public string Name { get; }

    public string Description { get; }

    public string Version { get; }

    public string Author { get; }

    public string ApiVersion { get; }
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

    public ObservableCollection<PluginMarketItemViewModel> MarketPlugins { get; } = [];

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
    private string _marketplaceHeader = string.Empty;

    [ObservableProperty]
    private string _deleteButtonText = string.Empty;

    [ObservableProperty]
    private string _installButtonText = string.Empty;

    [ObservableProperty]
    private string _emptyInstalledText = string.Empty;

    [ObservableProperty]
    private string _emptyMarketplaceText = string.Empty;

    [ObservableProperty]
    private string _restartRequiredMessage = string.Empty;

    public async Task InitializeAsync()
    {
        if (InstalledPlugins.Count > 0 || MarketPlugins.Count > 0)
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

            MarketPlugins.Clear();
            var marketResult = await _settingsFacade.PluginMarket.LoadIndexAsync();
            if (marketResult.Success)
            {
                foreach (var plugin in marketResult.Plugins)
                {
                    MarketPlugins.Add(new PluginMarketItemViewModel(plugin));
                }

                StatusMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    L(
                        "settings.plugins.refresh_success_format",
                        "Loaded {0} installed plugins and {1} marketplace entries."),
                    InstalledPlugins.Count,
                    MarketPlugins.Count);
            }
            else
            {
                StatusMessage = string.IsNullOrWhiteSpace(marketResult.ErrorMessage)
                    ? L("settings.plugins.refresh_failed", "Failed to load plugin market index.")
                    : marketResult.ErrorMessage;
            }
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

    [RelayCommand]
    private async Task InstallPluginAsync(PluginMarketItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _settingsFacade.PluginMarket.InstallAsync(item.PluginId);
            if (result.Success)
            {
                StatusMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    L(
                        "settings.plugins.install_success_format",
                        "Installed plugin '{0}'. Restart the app to apply newly added settings pages and widgets."),
                    item.Name);
                RestartRequested?.Invoke();
                await RefreshAsync();
                return;
            }

            StatusMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L("settings.plugins.install_failed_name_format", "Failed to install '{0}'."),
                    item.Name)
                : result.ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.plugins.title", "Plugins");
        PageDescription = L("settings.plugins.description", "Manage installed plugins and discover marketplace packages.");
        RefreshButtonText = L("settings.plugins.refresh_button", "Refresh Plugins");
        InstalledHeader = L("settings.plugins.installed_header", "Installed Plugins");
        MarketplaceHeader = L("settings.plugins.marketplace_header", "Marketplace");
        DeleteButtonText = L("settings.plugins.delete_button_short", "Delete");
        InstallButtonText = L("settings.plugins.install_button_short", "Install");
        EmptyInstalledText = L("settings.plugins.empty", "No plugins found.");
        EmptyMarketplaceText = L("settings.plugins.marketplace_empty", "No marketplace plugins available.");
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
    private bool _isInitializing;

    public AboutSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        UpdateChannels = CreateUpdateChannels();
        RefreshLocalizedText();

        var update = _settingsFacade.Update.Get();
        _isInitializing = true;
        AutoCheckUpdates = update.AutoCheckUpdates;
        IncludePrereleaseUpdates = update.IncludePrereleaseUpdates;
        SelectedUpdateChannel = UpdateChannels.FirstOrDefault(option =>
            string.Equals(
                option.Value,
                string.IsNullOrWhiteSpace(update.UpdateChannel) ? "stable" : update.UpdateChannel,
                StringComparison.OrdinalIgnoreCase))
            ?? UpdateChannels[0];

        var versionText = _settingsFacade.ApplicationInfo.GetAppVersionText();
        var backendInfo = _settingsFacade.ApplicationInfo.GetRenderBackendInfo();
        var renderBackendText = string.IsNullOrWhiteSpace(backendInfo.ImplementationTypeName)
            ? backendInfo.ActualBackend
            : $"{backendInfo.ActualBackend} ({backendInfo.ImplementationTypeName})";
        VersionText = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.about.version_format", "Version: {0}"),
            versionText);
        RenderBackendText = string.Format(
            CultureInfo.CurrentCulture,
            L("settings.about.render_backend_format", "Render Backend: {0}"),
            renderBackendText);
        UpdateStatus = L("settings.update.status_idle", "No update check has been performed yet.");
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> UpdateChannels { get; }

    [ObservableProperty]
    private string _versionText = "-";

    [ObservableProperty]
    private string _renderBackendText = "-";

    [ObservableProperty]
    private bool _autoCheckUpdates;

    [ObservableProperty]
    private bool _includePrereleaseUpdates;

    [ObservableProperty]
    private SelectionOption _selectedUpdateChannel = new("stable", "Stable");

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _autoCheckUpdatesLabel = string.Empty;

    [ObservableProperty]
    private string _includePrereleaseUpdatesLabel = string.Empty;

    [ObservableProperty]
    private string _updateChannelLabel = string.Empty;

    [ObservableProperty]
    private string _checkForUpdatesButtonText = string.Empty;

    [ObservableProperty]
    private string _appInfoHeader = string.Empty;

    [ObservableProperty]
    private string _updateHeader = string.Empty;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _renderBackendLabel = string.Empty;

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
    }

    partial void OnIncludePrereleaseUpdatesChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveUpdateSettings();
    }

    partial void OnSelectedUpdateChannelChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveUpdateSettings();
    }

    private void SaveUpdateSettings()
    {
        _settingsFacade.Update.Save(new UpdateSettingsState(
            AutoCheckUpdates,
            IncludePrereleaseUpdates,
            SelectedUpdateChannel.Value));
        UpdateStatus = L("settings.update.status_preferences_saved", "Update preferences saved.");
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            var version = Version.TryParse(VersionText, out var currentVersion)
                ? currentVersion
                : new Version(0, 0, 0);

            var result = await _settingsFacade.Update.CheckForUpdatesAsync(version, IncludePrereleaseUpdates);
            if (!result.Success)
            {
                UpdateStatus = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("settings.update.status_check_failed", "Failed to check for updates.")
                    : result.ErrorMessage;
                return;
            }

            UpdateStatus = result.IsUpdateAvailable
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L(
                        "settings.update.status_available_summary_format",
                        "Update available: {0} (current: {1})"),
                    result.LatestVersionText,
                    result.CurrentVersionText)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    L("settings.update.status_up_to_date_format", "You are up to date ({0})."),
                    result.CurrentVersionText);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private IReadOnlyList<SelectionOption> CreateUpdateChannels()
    {
        return
        [
            new SelectionOption("stable", L("settings.update.channel_stable", "Stable")),
            new SelectionOption("preview", L("settings.update.channel_preview", "Preview"))
        ];
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.about.title", "About");
        PageDescription = L("settings.about.description", "Application details and update preferences.");
        AppInfoHeader = L("settings.about.app_info_header", "Application Information");
        UpdateHeader = L("settings.about.update_header", "Updates");
        VersionLabel = L("settings.about.version_label", "Version");
        RenderBackendLabel = L("settings.about.render_backend_label", "Render Backend");
        AutoCheckUpdatesLabel = L("settings.update.auto_check_toggle", "Automatically check for updates on startup");
        IncludePrereleaseUpdatesLabel = L("settings.update.include_prerelease_toggle", "Include prerelease versions");
        UpdateChannelLabel = L("settings.update.channel_label", "Update Channel");
        CheckForUpdatesButtonText = L("settings.update.check_button", "Check for Updates");
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
