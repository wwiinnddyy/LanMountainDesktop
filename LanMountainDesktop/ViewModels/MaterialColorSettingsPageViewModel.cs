using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed class MaterialSurfacePreviewOption
{
    public MaterialSurfacePreviewOption(string label, MaterialSurfaceSnapshot surface, string detailFormat)
    {
        Label = label;
        BackgroundBrush = new SolidColorBrush(surface.BackgroundColor);
        BorderBrush = new SolidColorBrush(surface.BorderColor);
        Detail = string.Format(CultureInfo.CurrentCulture, detailFormat, surface.BackgroundColor.A, surface.BlurRadius);
    }

    public string Label { get; }

    public IBrush BackgroundBrush { get; }

    public IBrush BorderBrush { get; }

    public string Detail { get; }
}

public sealed class MaterialColorRolePreviewOption
{
    public MaterialColorRolePreviewOption(string label, Color color)
    {
        Label = label;
        Value = color.ToString();
        Brush = new SolidColorBrush(color);
    }

    public string Label { get; }

    public string Value { get; }

    public IBrush Brush { get; }
}

public sealed partial class MaterialColorSettingsPageViewModel : ViewModelBase
{
    private static readonly Color DefaultSeedColor = Color.Parse("#FF3B82F6");
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IMaterialColorService _materialColorService;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;
    private bool _isSynchronizingSystemMaterialMode;
    private string? _selectedWallpaperSeed;

    public MaterialColorSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        IMaterialColorService materialColorService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _materialColorService = materialColorService ?? throw new ArgumentNullException(nameof(materialColorService));
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        _isInitializing = true;
        try
        {
            RefreshLocalizedText();
            ColorModes = CreateColorModes();
            WallpaperColorSources = CreateWallpaperColorSources();
            RefreshIntervals = CreateRefreshIntervals();
            RefreshMaterialModes(_materialColorService.GetMaterialColorSnapshot());
            Load();
        }
        finally
        {
            _isInitializing = false;
        }

        _materialColorService.MaterialColorChanged += OnMaterialColorChanged;
    }

    public IReadOnlyList<SelectionOption> ColorModes { get; }

    public IReadOnlyList<SelectionOption> WallpaperColorSources { get; }

    public IReadOnlyList<SelectionOption> RefreshIntervals { get; }

    [ObservableProperty]
    private SelectionOption _selectedColorMode = new(ThemeAppearanceValues.ColorModeDefaultNeutral, "Default neutral");

    [ObservableProperty]
    private SelectionOption _selectedWallpaperColorSource = new(ThemeAppearanceValues.WallpaperColorSourceAuto, "Auto");

    [ObservableProperty]
    private IReadOnlyList<SelectionOption> _systemMaterialModes = [];

    [ObservableProperty]
    private SelectionOption _selectedSystemMaterialMode = new(ThemeAppearanceValues.MaterialAuto, "Auto");

    [ObservableProperty]
    private SelectionOption _selectedRefreshInterval = new("5m", "5 minutes");

    [ObservableProperty]
    private bool _useNativeWallpaperChangeEvents = true;

    [ObservableProperty]
    private Color _customSeedPickerValue = DefaultSeedColor;

    [ObservableProperty]
    private IBrush _seedBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IBrush _accentBrush = new SolidColorBrush(DefaultSeedColor);

    [ObservableProperty]
    private IBrush _surfaceBrush = new SolidColorBrush(Color.Parse("#FFF7F8FA"));

    [ObservableProperty]
    private IReadOnlyList<ThemeSeedCandidateOption> _wallpaperSeedCandidates = [];

    [ObservableProperty]
    private IReadOnlyList<MaterialColorRolePreviewOption> _colorRolePreviews = [];

    [ObservableProperty]
    private IReadOnlyList<MaterialSurfacePreviewOption> _surfacePreviews = [];

    [ObservableProperty]
    private string _resolvedSourceText = string.Empty;

    [ObservableProperty]
    private string _resolvedWallpaperPathText = string.Empty;

    [ObservableProperty]
    private bool _isCustomSeedVisible;

    [ObservableProperty]
    private bool _isWallpaperOptionsVisible;

    [ObservableProperty]
    private bool _isWallpaperSeedSelectable;

    [ObservableProperty]
    private bool _isNativeEventStatusVisible;

    [ObservableProperty]
    private string _nativeEventStatusText = string.Empty;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _colorSourceLabel = string.Empty;

    [ObservableProperty]
    private string _colorSourceDescription = string.Empty;

    [ObservableProperty]
    private string _customSeedLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperColorSourceLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperSeedLabel = string.Empty;

    [ObservableProperty]
    private string _systemMaterialLabel = string.Empty;

    [ObservableProperty]
    private string _systemMaterialDescription = string.Empty;

    [ObservableProperty]
    private string _nativeWallpaperEventsLabel = string.Empty;

    [ObservableProperty]
    private string _nativeWallpaperEventsDescription = string.Empty;

    [ObservableProperty]
    private string _refreshIntervalLabel = string.Empty;

    [ObservableProperty]
    private string _refreshNowText = string.Empty;

    [ObservableProperty]
    private string _previewHeader = string.Empty;

    [ObservableProperty]
    private string _sourceStatusHeader = string.Empty;

    [ObservableProperty]
    private string _semanticColorsHeader = string.Empty;

    [ObservableProperty]
    private string _surfacesHeader = string.Empty;

    [ObservableProperty]
    private string _wallpaperSeedCurrentText = string.Empty;

    [ObservableProperty]
    private string _modeNeutralText = string.Empty;

    [ObservableProperty]
    private string _modeCustomText = string.Empty;

    [ObservableProperty]
    private string _modeWallpaperText = string.Empty;

    [ObservableProperty]
    private string _wallpaperSourceAutoText = string.Empty;

    [ObservableProperty]
    private string _wallpaperSourceAppText = string.Empty;

    [ObservableProperty]
    private string _wallpaperSourceSystemText = string.Empty;

    [ObservableProperty]
    private string _materialNoneText = string.Empty;

    [ObservableProperty]
    private string _materialAutoText = string.Empty;

    [ObservableProperty]
    private string _materialMicaText = string.Empty;

    [ObservableProperty]
    private string _materialAcrylicText = string.Empty;

    [ObservableProperty]
    private string _sourceFallbackText = string.Empty;

    [ObservableProperty]
    private string _colorRoleAccentText = string.Empty;

    [ObservableProperty]
    private string _colorRolePrimaryText = string.Empty;

    [ObservableProperty]
    private string _colorRoleSecondaryText = string.Empty;

    [ObservableProperty]
    private string _colorRoleSurfaceText = string.Empty;

    [ObservableProperty]
    private string _colorRoleTextText = string.Empty;

    [ObservableProperty]
    private string _colorRoleToggleText = string.Empty;

    [ObservableProperty]
    private string _surfaceDetailFormat = string.Empty;

    public void Load()
    {
        var theme = _settingsFacade.Theme.Get();
        var snapshot = _materialColorService.GetMaterialColorSnapshot();
        RefreshMaterialModes(snapshot);

        SelectedColorMode = ColorModes.FirstOrDefault(option =>
            string.Equals(option.Value, theme.ThemeColorMode, StringComparison.OrdinalIgnoreCase))
            ?? ColorModes[0];
        SelectedWallpaperColorSource = WallpaperColorSources.FirstOrDefault(option =>
            string.Equals(option.Value, theme.ThemeWallpaperColorSource, StringComparison.OrdinalIgnoreCase))
            ?? WallpaperColorSources[0];
        SelectedSystemMaterialMode = SystemMaterialModes.FirstOrDefault(option =>
            string.Equals(option.Value, theme.SystemMaterialMode, StringComparison.OrdinalIgnoreCase))
            ?? SystemMaterialModes[0];
        SelectedRefreshInterval = RefreshIntervals.FirstOrDefault(option =>
            GetIntervalSeconds(option.Value) == _settingsFacade.Wallpaper.Get().SystemWallpaperRefreshIntervalSeconds)
            ?? RefreshIntervals[2];
        UseNativeWallpaperChangeEvents = theme.UseNativeWallpaperChangeEvents;
        _selectedWallpaperSeed = theme.SelectedWallpaperSeed;
        CustomSeedPickerValue = !string.IsNullOrWhiteSpace(theme.ThemeColor) && Color.TryParse(theme.ThemeColor, out var parsed)
            ? parsed
            : DefaultSeedColor;

        UpdateVisibility();
        UpdatePreview(snapshot);
    }

    partial void OnSelectedColorModeChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        UpdateVisibility();
        SaveTheme();
    }

    partial void OnSelectedWallpaperColorSourceChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveTheme();
    }

    partial void OnSelectedSystemMaterialModeChanged(SelectionOption value)
    {
        if (_isInitializing || _isSynchronizingSystemMaterialMode || value is null)
        {
            return;
        }

        SaveTheme();
    }

    partial void OnSelectedRefreshIntervalChanged(SelectionOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        SaveWallpaperRefreshInterval();
    }

    partial void OnUseNativeWallpaperChangeEventsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        SaveTheme();
    }

    partial void OnCustomSeedPickerValueChanged(Color value)
    {
        SeedBrush = new SolidColorBrush(value);
        if (_isInitializing || !IsCustomSeedVisible)
        {
            return;
        }

        SaveTheme();
    }

    public void SelectWallpaperSeed(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _selectedWallpaperSeed = value;
        SaveTheme();
    }

    [RelayCommand]
    private void RefreshWallpaperColors()
    {
        _materialColorService.RefreshWallpaperColors();
    }

    private void SaveTheme()
    {
        var current = _settingsFacade.Theme.Get();
        var colorMode = ThemeAppearanceValues.NormalizeThemeColorMode(SelectedColorMode?.Value, current.ThemeColor);
        var themeColor = colorMode == ThemeAppearanceValues.ColorModeSeedMonet
            ? CustomSeedPickerValue.ToString()
            : current.ThemeColor;

        _settingsFacade.Theme.Save(current with
        {
            ThemeColorMode = colorMode,
            ThemeColor = themeColor,
            SystemMaterialMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(SelectedSystemMaterialMode?.Value),
            SelectedWallpaperSeed = _selectedWallpaperSeed,
            ThemeWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(SelectedWallpaperColorSource?.Value),
            UseNativeWallpaperChangeEvents = UseNativeWallpaperChangeEvents
        });
    }

    private void SaveWallpaperRefreshInterval()
    {
        var wallpaper = _settingsFacade.Wallpaper.Get();
        _settingsFacade.Wallpaper.Save(wallpaper with
        {
            SystemWallpaperRefreshIntervalSeconds = GetIntervalSeconds(SelectedRefreshInterval?.Value)
        });
    }

    private void OnMaterialColorChanged(object? sender, MaterialColorSnapshot snapshot)
    {
        _ = sender;
        UpdatePreview(snapshot);
        RefreshMaterialModes(snapshot);
    }

    private void UpdatePreview(MaterialColorSnapshot snapshot)
    {
        AccentBrush = new SolidColorBrush(snapshot.AccentColor);
        SeedBrush = new SolidColorBrush(snapshot.EffectiveSeedColor);
        SurfaceBrush = new SolidColorBrush(snapshot.Palette.SurfaceRaised);
        ResolvedSourceText = ResolveSourceLabel(snapshot);
        ResolvedWallpaperPathText = string.IsNullOrWhiteSpace(snapshot.ResolvedWallpaperPath)
            ? "-"
            : snapshot.ResolvedWallpaperPath;
        NativeEventStatusText = snapshot.NativeWallpaperChangeEventsActive
            ? L("settings.material_color.native_events.active", "Native wallpaper events active")
            : snapshot.WallpaperPollingActive
                ? L("settings.material_color.native_events.polling", "Polling fallback active")
                : L("settings.material_color.native_events.inactive", "Wallpaper monitoring inactive");
        IsNativeEventStatusVisible = IsWallpaperOptionsVisible;

        WallpaperSeedCandidates = snapshot.WallpaperSeedCandidates
            .Select((color, index) => new ThemeSeedCandidateOption(
                color.ToString(),
                string.Format(CultureInfo.CurrentCulture, "{0} {1}", WallpaperSeedLabel, index + 1),
                color,
                string.Equals(color.ToString(), snapshot.EffectiveSeedColor.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        IsWallpaperSeedSelectable = WallpaperSeedCandidates.Count > 1;

        ColorRolePreviews =
        [
            new MaterialColorRolePreviewOption(ColorRoleAccentText, snapshot.Palette.Accent),
            new MaterialColorRolePreviewOption(ColorRolePrimaryText, snapshot.Palette.Primary),
            new MaterialColorRolePreviewOption(ColorRoleSecondaryText, snapshot.Palette.Secondary),
            new MaterialColorRolePreviewOption(ColorRoleSurfaceText, snapshot.Palette.SurfaceRaised),
            new MaterialColorRolePreviewOption(ColorRoleTextText, snapshot.Palette.TextPrimary),
            new MaterialColorRolePreviewOption(ColorRoleToggleText, snapshot.Palette.ToggleOn)
        ];

        SurfacePreviews = snapshot.Surfaces.Values
            .Where(surface => surface.Role is
                MaterialSurfaceRole.SettingsWindowBackground or
                MaterialSurfaceRole.DockBackground or
                MaterialSurfaceRole.DesktopComponentHost or
                MaterialSurfaceRole.OverlayPanel)
            .Select(surface => new MaterialSurfacePreviewOption(surface.Role.ToString(), surface, SurfaceDetailFormat))
            .ToArray();
    }

    private void UpdateVisibility()
    {
        var colorMode = ThemeAppearanceValues.NormalizeThemeColorMode(SelectedColorMode?.Value);
        IsCustomSeedVisible = colorMode == ThemeAppearanceValues.ColorModeSeedMonet;
        IsWallpaperOptionsVisible = colorMode == ThemeAppearanceValues.ColorModeWallpaperMonet;
    }

    private void RefreshMaterialModes(MaterialColorSnapshot snapshot)
    {
        var selectedValue = ThemeAppearanceValues.NormalizeSystemMaterialMode(
            SelectedSystemMaterialMode?.Value ?? snapshot.SystemMaterialMode);
        var snapshotValue = ThemeAppearanceValues.NormalizeSystemMaterialMode(snapshot.SystemMaterialMode);

        SystemMaterialModes = ThemeAppearanceValues.NormalizeAvailableMaterialModes(snapshot.AvailableSystemMaterialModes)
            .Select(value => new SelectionOption(value, ResolveMaterialLabel(value)))
            .ToArray();

        var nextSelection = SystemMaterialModes.FirstOrDefault(option =>
                string.Equals(option.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            ?? SystemMaterialModes.FirstOrDefault(option =>
                string.Equals(option.Value, snapshotValue, StringComparison.OrdinalIgnoreCase))
            ?? SystemMaterialModes.FirstOrDefault()
            ?? new SelectionOption(ThemeAppearanceValues.MaterialNone, MaterialNoneText);

        if (!string.Equals(SelectedSystemMaterialMode?.Value, nextSelection.Value, StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(SelectedSystemMaterialMode, nextSelection))
        {
            _isSynchronizingSystemMaterialMode = true;
            try
            {
                SelectedSystemMaterialMode = nextSelection;
            }
            finally
            {
                _isSynchronizingSystemMaterialMode = false;
            }
        }
    }

    private string ResolveSourceLabel(MaterialColorSnapshot snapshot)
    {
        return snapshot.ColorSourceKind switch
        {
            MaterialColorSourceKind.Neutral => ModeNeutralText,
            MaterialColorSourceKind.CustomSeed => ModeCustomText,
            MaterialColorSourceKind.AppWallpaper => WallpaperSourceAppText,
            MaterialColorSourceKind.SystemWallpaper => WallpaperSourceSystemText,
            MaterialColorSourceKind.WallpaperAuto => WallpaperSourceAutoText,
            _ => SourceFallbackText
        };
    }

    private string ResolveMaterialLabel(string value)
    {
        return ThemeAppearanceValues.NormalizeSystemMaterialMode(value) switch
        {
            ThemeAppearanceValues.MaterialAuto => MaterialAutoText,
            ThemeAppearanceValues.MaterialMica => MaterialMicaText,
            ThemeAppearanceValues.MaterialAcrylic => MaterialAcrylicText,
            _ => MaterialNoneText
        };
    }

    private IReadOnlyList<SelectionOption> CreateColorModes()
    {
        return
        [
            new SelectionOption(ThemeAppearanceValues.ColorModeDefaultNeutral, ModeNeutralText),
            new SelectionOption(ThemeAppearanceValues.ColorModeSeedMonet, ModeCustomText),
            new SelectionOption(ThemeAppearanceValues.ColorModeWallpaperMonet, ModeWallpaperText)
        ];
    }

    private IReadOnlyList<SelectionOption> CreateWallpaperColorSources()
    {
        return
        [
            new SelectionOption(ThemeAppearanceValues.WallpaperColorSourceAuto, WallpaperSourceAutoText),
            new SelectionOption(ThemeAppearanceValues.WallpaperColorSourceApp, WallpaperSourceAppText),
            new SelectionOption(ThemeAppearanceValues.WallpaperColorSourceSystem, WallpaperSourceSystemText)
        ];
    }

    private IReadOnlyList<SelectionOption> CreateRefreshIntervals()
    {
        return
        [
            new SelectionOption("30s", L("settings.wallpaper.refresh.30s", "30 seconds")),
            new SelectionOption("1m", L("settings.wallpaper.refresh.1m", "1 minute")),
            new SelectionOption("5m", L("settings.wallpaper.refresh.5m", "5 minutes")),
            new SelectionOption("10m", L("settings.wallpaper.refresh.10m", "10 minutes")),
            new SelectionOption("15m", L("settings.wallpaper.refresh.15m", "15 minutes")),
            new SelectionOption("30m", L("settings.wallpaper.refresh.30m", "30 minutes")),
            new SelectionOption("1h", L("settings.wallpaper.refresh.1h", "1 hour")),
            new SelectionOption("2h", L("settings.wallpaper.refresh.2h", "2 hours")),
            new SelectionOption("4h", L("settings.wallpaper.refresh.4h", "4 hours")),
            new SelectionOption("8h", L("settings.wallpaper.refresh.8h", "8 hours")),
            new SelectionOption("12h", L("settings.wallpaper.refresh.12h", "12 hours")),
            new SelectionOption("24h", L("settings.wallpaper.refresh.24h", "24 hours"))
        ];
    }

    private static int GetIntervalSeconds(string? value)
    {
        return value switch
        {
            "30s" => 30,
            "1m" => 60,
            "5m" => 300,
            "10m" => 600,
            "15m" => 900,
            "30m" => 1800,
            "1h" => 3600,
            "2h" => 7200,
            "4h" => 14400,
            "8h" => 28800,
            "12h" => 43200,
            "24h" => 86400,
            _ => 300
        };
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.material_color.title", "Material & Color");
        PageDescription = L("settings.material_color.description", "Unify Monet, wallpaper colors, semantic roles, and material surfaces.");
        ColorSourceLabel = L("settings.material_color.source.label", "Color source");
        ColorSourceDescription = L("settings.material_color.source.description", "Choose the single source used by app surfaces, components, and plugins.");
        CustomSeedLabel = L("settings.material_color.custom_seed.label", "Custom Monet seed");
        WallpaperColorSourceLabel = L("settings.material_color.wallpaper_source.label", "Wallpaper color source");
        WallpaperSeedLabel = L("settings.material_color.wallpaper_seed.label", "Seed");
        SystemMaterialLabel = L("settings.material_color.system_material.label", "System material");
        SystemMaterialDescription = L("settings.material_color.system_material.description", "Apply the selected material mode to windows and host surfaces.");
        NativeWallpaperEventsLabel = L("settings.material_color.native_events.label", "Native wallpaper change events");
        NativeWallpaperEventsDescription = L("settings.material_color.native_events.description", "Use OS wallpaper notifications first and keep polling as fallback.");
        RefreshIntervalLabel = L("settings.material_color.refresh_interval.label", "Polling interval");
        RefreshNowText = L("settings.material_color.refresh_now", "Refresh colors");
        PreviewHeader = L("settings.material_color.preview.header", "Unified preview");
        SourceStatusHeader = L("settings.material_color.source_status.header", "Resolved source");
        SemanticColorsHeader = L("settings.material_color.semantic.header", "Semantic colors");
        SurfacesHeader = L("settings.material_color.surfaces.header", "Material surfaces");
        WallpaperSeedCurrentText = L("settings.material_color.preview.wallpaper_current", "Current");
        ModeNeutralText = L("settings.material_color.theme_color_mode.neutral", "Default neutral");
        ModeCustomText = L("settings.material_color.theme_color_mode.user", "User theme color Monet");
        ModeWallpaperText = L("settings.material_color.theme_color_mode.wallpaper", "Wallpaper Monet");
        WallpaperSourceAutoText = L("settings.material_color.wallpaper_source.auto", "Auto");
        WallpaperSourceAppText = L("settings.material_color.wallpaper_source.app", "App wallpaper");
        WallpaperSourceSystemText = L("settings.material_color.wallpaper_source.system", "System wallpaper");
        MaterialNoneText = L("settings.material_color.system_material.none", "None");
        MaterialAutoText = L("settings.material_color.system_material.auto", "Auto (recommended)");
        MaterialMicaText = L("settings.material_color.system_material.mica", "Mica");
        MaterialAcrylicText = L("settings.material_color.system_material.acrylic", "Acrylic");
        SourceFallbackText = L("settings.material_color.source.fallback", "Fallback");
        ColorRoleAccentText = L("settings.material_color.role.accent", "Accent");
        ColorRolePrimaryText = L("settings.material_color.role.primary", "Primary");
        ColorRoleSecondaryText = L("settings.material_color.role.secondary", "Secondary");
        ColorRoleSurfaceText = L("settings.material_color.role.surface", "Surface");
        ColorRoleTextText = L("settings.material_color.role.text", "Text");
        ColorRoleToggleText = L("settings.material_color.role.toggle", "Toggle");
        SurfaceDetailFormat = L("settings.material_color.surface.detail_format", "A={0:X2} Blur={1:0}");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
