using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        RefreshLocalizedText();

        _isInitializing = true;
        Load();
        _isInitializing = false;
    }

    public IReadOnlyList<SelectionOption> WallpaperPlacements { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _wallpaperPath = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private SelectionOption _selectedWallpaperPlacement = new("Fill", "Fill");

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _wallpaperHeader = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _wallpaperPathLabel = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _wallpaperPlacementLabel = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _wallpaperPlacementDescription = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _importWallpaperButtonText = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _filePickerTitle = string.Empty;

    public void Load()
    {
        var wallpaper = _settingsFacade.Wallpaper.Get();
        WallpaperPath = wallpaper.WallpaperPath ?? string.Empty;
        var wallpaperPlacement = string.IsNullOrWhiteSpace(wallpaper.Placement)
            ? "Fill"
            : wallpaper.Placement;
        SelectedWallpaperPlacement = WallpaperPlacements.FirstOrDefault(option =>
            string.Equals(option.Value, wallpaperPlacement, StringComparison.OrdinalIgnoreCase))
            ?? WallpaperPlacements[0];
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

    private void SaveWallpaper()
    {
        _settingsFacade.Wallpaper.Save(new WallpaperSettingsState(
            string.IsNullOrWhiteSpace(WallpaperPath) ? null : WallpaperPath,
            SelectedWallpaperPlacement.Value));
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

    private void RefreshLocalizedText()
    {
        WallpaperHeader = L("settings.wallpaper.title", "Wallpaper");
        WallpaperPathLabel = L("settings.wallpaper.current_label", "Current Wallpaper");
        WallpaperPlacementLabel = L("settings.wallpaper.placement_label", "Placement");
        WallpaperPlacementDescription = L("settings.wallpaper.placement_desc", "Adjust how the image fills the desktop.");
        ImportWallpaperButtonText = L("settings.wallpaper.pick_button", "Import Wallpaper");
        FilePickerTitle = L("filepicker.title", "Select wallpaper");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
