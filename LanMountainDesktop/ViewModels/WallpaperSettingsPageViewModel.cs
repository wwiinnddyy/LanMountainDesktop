using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public sealed partial class WallpaperSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISystemWallpaperProvider _systemWallpaperProvider;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public WallpaperSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _systemWallpaperProvider = HostSystemWallpaperProvider.GetOrCreate();
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        WallpaperPlacements = CreateWallpaperPlacements();
        WallpaperTypes = CreateWallpaperTypes();
        RefreshIntervals = CreateRefreshIntervals();
        PresetColors = CreatePresetColors();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> WallpaperPlacements { get; }
    public IReadOnlyList<SelectionOption> WallpaperTypes { get; }
    public IReadOnlyList<SelectionOption> RefreshIntervals { get; }
    public IReadOnlyList<string> PresetColors { get; }

    public bool IsSystemWallpaperSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [ObservableProperty]
    private string _wallpaperPath = string.Empty;

    [ObservableProperty]
    private SelectionOption _selectedWallpaperType = null!;

    [ObservableProperty]
    private string? _selectedColor;

    [ObservableProperty]
    private SelectionOption _selectedWallpaperPlacement = null!;

    [ObservableProperty]
    private SelectionOption _selectedRefreshInterval = null!;

    [ObservableProperty]
    private string _wallpaperHeader = string.Empty;

    [ObservableProperty]
    private string _wallpaperTypeLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperPathLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperColorLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperPlacementLabel = string.Empty;

    [ObservableProperty]
    private string _wallpaperPlacementDescription = string.Empty;

    [ObservableProperty]
    private string _importWallpaperButtonText = string.Empty;

    [ObservableProperty]
    private string _filePickerTitle = string.Empty;

    [ObservableProperty]
    private string _systemWallpaperLabel = string.Empty;

    [ObservableProperty]
    private string _refreshIntervalLabel = string.Empty;

    [ObservableProperty]
    private string _refreshButtonTooltip = string.Empty;

    [ObservableProperty]
    private string _systemWallpaperStatus = string.Empty;

    [ObservableProperty]
    private bool _isImageOrVideo;

    [ObservableProperty]
    private bool _isSolidColor;

    [ObservableProperty]
    private bool _isImage;

    [ObservableProperty]
    private bool _isSystemWallpaper;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private IBrush? _previewBrush;

    [ObservableProperty]
    private Color _customColor = Colors.White;

    [ObservableProperty]
    private IBrush _customColorBrush = new SolidColorBrush(Colors.White);

    public void Load()
    {
        var wallpaper = _settingsFacade.Wallpaper.Get();
        WallpaperPath = wallpaper.WallpaperPath ?? string.Empty;

        SelectedWallpaperType = WallpaperTypes.FirstOrDefault(t => t.Value == wallpaper.Type) ?? WallpaperTypes[0];
        SelectedColor = wallpaper.Color ?? PresetColors[0];

        var wallpaperPlacement = string.IsNullOrWhiteSpace(wallpaper.Placement)
            ? "Fill"
            : wallpaper.Placement;
        SelectedWallpaperPlacement = WallpaperPlacements.FirstOrDefault(option =>
            string.Equals(option.Value, wallpaperPlacement, StringComparison.OrdinalIgnoreCase))
            ?? WallpaperPlacements[0];

        var refreshIntervalSeconds = wallpaper.SystemWallpaperRefreshIntervalSeconds;
        SelectedRefreshInterval = RefreshIntervals.FirstOrDefault(option =>
            GetIntervalSeconds(option.Value) == refreshIntervalSeconds)
            ?? RefreshIntervals[2];

        if (!string.IsNullOrWhiteSpace(wallpaper.CustomColor) && Color.TryParse(wallpaper.CustomColor, out var customColor))
        {
            CustomColor = customColor;
            CustomColorBrush = new SolidColorBrush(customColor);
        }

        UpdateVisibility();
        UpdatePreviewFromCurrentSelection();
        UpdateSystemWallpaperStatus();
    }

    partial void OnSelectedWallpaperTypeChanged(SelectionOption value)
    {
        UpdateVisibility();
        UpdatePreviewFromCurrentSelection();
        if (_isInitializing) return;
        SaveWallpaper();
    }

    private void UpdateVisibility()
    {
        IsImage = SelectedWallpaperType?.Value == "Image";
        IsImageOrVideo = IsImage || SelectedWallpaperType?.Value == "SystemWallpaper";
        IsSolidColor = SelectedWallpaperType?.Value == "SolidColor";
        IsSystemWallpaper = SelectedWallpaperType?.Value == "SystemWallpaper";
    }

    partial void OnSelectedColorChanged(string? value)
    {
        if (_isInitializing) return;
        SaveWallpaper();
    }

    partial void OnCustomColorChanged(Color value)
    {
        CustomColorBrush = new SolidColorBrush(value);
        var colorHex = $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
        SelectedColor = colorHex;
        if (_isInitializing) return;
        SaveWallpaper();
    }

    partial void OnSelectedRefreshIntervalChanged(SelectionOption value)
    {
        if (_isInitializing) return;
        SaveWallpaper();
    }

    public async Task ImportWallpaperAsync(string sourcePath)
    {
        var importedPath = await _settingsFacade.WallpaperMedia.ImportAssetAsync(sourcePath);
        if (!string.IsNullOrWhiteSpace(importedPath))
        {
            WallpaperPath = importedPath;
        }
    }

    partial void OnWallpaperPathChanged(string value)
    {
        UpdatePreviewFromCurrentSelection();
        if (_isInitializing) return;
        SaveWallpaper();
    }

    private void UpdatePreviewFromCurrentSelection()
    {
        if (IsSystemWallpaper)
        {
            UpdateSystemWallpaperPreview();
            return;
        }

        if (!IsImage)
        {
            ClearPreviewImage();
            PreviewBrush = null;
            return;
        }

        UpdatePreviewImage(WallpaperPath);
    }

    private void UpdateSystemWallpaperPreview()
    {
        var systemPath = _systemWallpaperProvider.GetWallpaperPath();
        if (string.IsNullOrWhiteSpace(systemPath))
        {
            ClearPreviewImage();
            SystemWallpaperStatus = L("settings.wallpaper.system.unavailable", "Unable to read system wallpaper");
            return;
        }

        SystemWallpaperStatus = systemPath;
        UpdatePreviewImage(systemPath);
    }

    private void UpdatePreviewImage(string? path)
    {
        var previousPreview = PreviewImage;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            previousPreview?.Dispose();
            PreviewImage = null;
            PreviewBrush = null;
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            PreviewImage = bitmap;
            PreviewBrush = WallpaperImageBrushFactory.Create(bitmap, SelectedWallpaperPlacement?.Value);
            previousPreview?.Dispose();
        }
        catch
        {
            previousPreview?.Dispose();
            PreviewImage = null;
            PreviewBrush = null;
        }
    }

    private void ClearPreviewImage()
    {
        var previousPreview = PreviewImage;
        PreviewImage = null;
        PreviewBrush = null;
        previousPreview?.Dispose();
    }

    private void UpdateSystemWallpaperStatus()
    {
        if (!IsSystemWallpaper) return;
        UpdateSystemWallpaperPreview();
    }

    [RelayCommand]
    private void RefreshSystemWallpaper()
    {
        UpdateSystemWallpaperPreview();
    }

    partial void OnSelectedWallpaperPlacementChanged(SelectionOption value)
    {
        if ((IsImage || IsSystemWallpaper) && PreviewImage is not null)
        {
            PreviewBrush = WallpaperImageBrushFactory.Create(PreviewImage, value?.Value);
        }

        if (_isInitializing || value is null) return;
        SaveWallpaper();
    }

    [RelayCommand]
    private void SelectColor(string color)
    {
        SelectedColor = color;
    }

    private void SaveWallpaper()
    {
        var selectedType = SelectedWallpaperType?.Value ?? "Image";
        var selectedPlacement = SelectedWallpaperPlacement?.Value ?? WallpaperImageBrushFactory.Fill;
        var refreshIntervalSeconds = GetIntervalSeconds(SelectedRefreshInterval?.Value);
        
        string? normalizedPath;
        if (selectedType == "SolidColor" || selectedType == "SystemWallpaper")
        {
            normalizedPath = null;
        }
        else
        {
            normalizedPath = string.IsNullOrWhiteSpace(WallpaperPath) ? null : WallpaperPath;
        }

        var customColorHex = $"#{CustomColor.A:X2}{CustomColor.R:X2}{CustomColor.G:X2}{CustomColor.B:X2}";
        _settingsFacade.Wallpaper.Save(new WallpaperSettingsState(
            normalizedPath,
            selectedType,
            SelectedColor,
            selectedPlacement,
            customColorHex,
            refreshIntervalSeconds));
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

    private IReadOnlyList<SelectionOption> CreateWallpaperTypes()
    {
        var types = new List<SelectionOption>
        {
            new SelectionOption("Image", L("settings.wallpaper.type.image", "Image")),
            new SelectionOption("SolidColor", L("settings.wallpaper.type.solid_color", "Solid Color"))
        };

        if (IsSystemWallpaperSupported)
        {
            types.Add(new SelectionOption("SystemWallpaper", L("settings.wallpaper.type.system", "System Wallpaper")));
        }

        return types;
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

    private IReadOnlyList<string> CreatePresetColors()
    {
        return
        [
            "#D8A7B1", "#B6C9BB", "#A2B5BB", "#E6E2D3",
            "#B5A397", "#C5C1C0", "#D4BE8D", "#C08261",
            "#8E9775", "#9FBAD3", "#E5BAA2"
        ];
    }

    private void RefreshLocalizedText()
    {
        WallpaperHeader = L("settings.wallpaper.title", "Wallpaper");
        WallpaperTypeLabel = L("settings.wallpaper.type_label", "Wallpaper Type");
        WallpaperPathLabel = L("settings.wallpaper.current_label", "Current Wallpaper");
        WallpaperColorLabel = L("settings.wallpaper.color_label", "Wallpaper Color");
        WallpaperPlacementLabel = L("settings.wallpaper.placement_label", "Placement");
        WallpaperPlacementDescription = L("settings.wallpaper.placement_desc", "Adjust how the image fills the desktop.");
        ImportWallpaperButtonText = L("settings.wallpaper.pick_button", "Import Wallpaper");
        FilePickerTitle = L("filepicker.title", "Select wallpaper");
        SystemWallpaperLabel = L("settings.wallpaper.system.label", "System Wallpaper");
        RefreshIntervalLabel = L("settings.wallpaper.refresh_interval", "Refresh Interval");
        RefreshButtonTooltip = L("settings.wallpaper.refresh_now", "Refresh Now");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
