using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _isInitializing;

    public WallpaperSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        WallpaperPlacements = CreateWallpaperPlacements();
        WallpaperTypes = CreateWallpaperTypes();
        PresetColors = CreatePresetColors();
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> WallpaperPlacements { get; }
    public IReadOnlyList<SelectionOption> WallpaperTypes { get; }
    public IReadOnlyList<string> PresetColors { get; }

    [ObservableProperty]
    private string _wallpaperPath = string.Empty;

    [ObservableProperty]
    private SelectionOption _selectedWallpaperType = null!;

    [ObservableProperty]
    private string? _selectedColor;

    [ObservableProperty]
    private SelectionOption _selectedWallpaperPlacement = null!;

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
    private bool _isImageOrVideo;

    [ObservableProperty]
    private bool _isSolidColor;

    [ObservableProperty]
    private bool _isImage;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private IBrush? _previewBrush;

    // 自定义颜色持久化
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

        // 加载自定义颜色
        if (!string.IsNullOrWhiteSpace(wallpaper.CustomColor) && Color.TryParse(wallpaper.CustomColor, out var customColor))
        {
            CustomColor = customColor;
            CustomColorBrush = new SolidColorBrush(customColor);
        }

        UpdateVisibility();
        UpdatePreviewFromCurrentSelection();
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
        IsImageOrVideo = IsImage;
        IsSolidColor = SelectedWallpaperType?.Value == "SolidColor";
    }

    partial void OnSelectedColorChanged(string? value)
    {
        if (_isInitializing) return;
        SaveWallpaper();
    }

    partial void OnCustomColorChanged(Color value)
    {
        CustomColorBrush = new SolidColorBrush(value);
        // 将自定义颜色应用到壁纸
        var colorHex = $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
        SelectedColor = colorHex;
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
        if (!IsImage)
        {
            ClearPreviewImage();
            PreviewBrush = null;
            return;
        }

        UpdatePreviewImage(WallpaperPath);
    }

    private void UpdatePreviewImage(string path)
    {
        var previousPreview = PreviewImage;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            previousPreview?.Dispose();
            PreviewImage = null;
            PreviewBrush = null;
            return;
        }

        try
        {
            using var stream = System.IO.File.OpenRead(path);
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

    partial void OnSelectedWallpaperPlacementChanged(SelectionOption value)
    {
        if (IsImage && PreviewImage is not null)
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
        var normalizedPath = SelectedWallpaperType?.Value == "SolidColor" || string.IsNullOrWhiteSpace(WallpaperPath)
            ? null
            : WallpaperPath;
        var customColorHex = $"#{CustomColor.A:X2}{CustomColor.R:X2}{CustomColor.G:X2}{CustomColor.B:X2}";
        _settingsFacade.Wallpaper.Save(new WallpaperSettingsState(
            normalizedPath,
            selectedType,
            SelectedColor,
            selectedPlacement,
            customColorHex));
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
        return
        [
            new SelectionOption("Image", L("settings.wallpaper.type.image", "Image")),
            new SelectionOption("SolidColor", L("settings.wallpaper.type.solid_color", "Solid Color"))
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
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
