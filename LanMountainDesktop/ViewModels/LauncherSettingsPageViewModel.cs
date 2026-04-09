using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public enum LauncherHiddenItemKind
{
    Folder,
    Shortcut
}

public sealed partial class LauncherHiddenItemViewModel : ObservableObject
{
    private readonly Action<LauncherHiddenItemViewModel> _restoreAction;

    public LauncherHiddenItemViewModel(
        LauncherHiddenItemKind kind,
        string key,
        string displayName,
        string typeLabel,
        Symbol iconSymbol,
        string restoreButtonText,
        Action<LauncherHiddenItemViewModel> restoreAction)
    {
        Kind = kind;
        Key = key;
        DisplayName = displayName;
        TypeLabel = typeLabel;
        IconSymbol = iconSymbol;
        RestoreButtonText = restoreButtonText;
        _restoreAction = restoreAction ?? throw new ArgumentNullException(nameof(restoreAction));
    }

    public LauncherHiddenItemKind Kind { get; }

    public string Key { get; }

    public string DisplayName { get; }

    public string TypeLabel { get; }

    public Symbol IconSymbol { get; }

    public string RestoreButtonText { get; }

    [RelayCommand]
    private void Restore()
    {
        _restoreAction(this);
    }
}

public sealed partial class LauncherSettingsPageViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService = new();
    private readonly string _languageCode;
    private bool _disposed;

    public LauncherSettingsPageViewModel(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);

        RefreshLocalizedText();
        ReloadData();
        _settingsFacade.Settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<LauncherHiddenItemViewModel> HiddenItems { get; } = [];

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _launcherHeader = string.Empty;

    [ObservableProperty]
    private string _launcherSubtitle = string.Empty;

    [ObservableProperty]
    private string _hiddenHeader = string.Empty;

    [ObservableProperty]
    private string _hiddenDescription = string.Empty;

    [ObservableProperty]
    private string _hiddenHint = string.Empty;

    [ObservableProperty]
    private string _hiddenEmptyText = string.Empty;

    [ObservableProperty]
    private string _hiddenSummary = string.Empty;

    [ObservableProperty]
    private string _hiddenCountText = "0";

    [ObservableProperty]
    private bool _hasHiddenItems;

    [ObservableProperty]
    private bool _isHiddenItemsEmpty = true;

    [ObservableProperty]
    private string _appearanceHeader = string.Empty;

    [ObservableProperty]
    private string _appearanceDescription = string.Empty;

    [ObservableProperty]
    private string _showTileBackgroundHeader = string.Empty;

    [ObservableProperty]
    private string _showTileBackgroundDescription = string.Empty;

    [ObservableProperty]
    private bool _showTileBackground;

    partial void OnShowTileBackgroundChanged(bool value)
    {
        SaveShowTileBackgroundSetting(value);
    }

    private void SaveShowTileBackgroundSetting(bool value)
    {
        var snapshot = _settingsFacade.LauncherPolicy.Get()?.Clone() ?? new LauncherSettingsSnapshot();
        snapshot.ShowTileBackground = value;
        _settingsFacade.Settings.SaveSnapshot(
            SettingsScope.Launcher,
            snapshot,
            changedKeys: [nameof(LauncherSettingsSnapshot.ShowTileBackground)]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _settingsFacade.Settings.Changed -= OnSettingsChanged;
        _disposed = true;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        if (e.Scope != SettingsScope.Launcher)
        {
            return;
        }

        Dispatcher.UIThread.Post(ReloadData, DispatcherPriority.Background);
    }

    private void ReloadData()
    {
        var root = LoadCatalogSafe();
        var snapshot = _settingsFacade.LauncherPolicy.Get()?.Clone() ?? new LauncherSettingsSnapshot();
        var hiddenItems = BuildHiddenItems(root, snapshot);

        HiddenItems.Clear();
        foreach (var hiddenItem in hiddenItems)
        {
            HiddenItems.Add(hiddenItem);
        }

        HasHiddenItems = HiddenItems.Count > 0;
        IsHiddenItemsEmpty = !HasHiddenItems;
        HiddenCountText = HiddenItems.Count.ToString(CultureInfo.CurrentCulture);
        HiddenSummary = string.Format(
            ResolveCulture(),
            L("settings.launcher.hidden_summary_format", "{0} hidden items"),
            HiddenItems.Count);

        ShowTileBackground = snapshot.ShowTileBackground;
    }

    private StartMenuFolderNode LoadCatalogSafe()
    {
        try
        {
            return _settingsFacade.LauncherCatalog.LoadCatalog() ?? new StartMenuFolderNode(L("launcher.title", "App Launcher"), string.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Launcher.Settings", "Failed to load launcher catalog for settings page.", ex);
            return new StartMenuFolderNode(L("launcher.title", "App Launcher"), string.Empty);
        }
    }

    private IReadOnlyList<LauncherHiddenItemViewModel> BuildHiddenItems(StartMenuFolderNode root, LauncherSettingsSnapshot snapshot)
    {
        var items = new List<LauncherHiddenItemViewModel>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectHiddenItems(root, snapshot, items, seenFolders, seenApps);

        foreach (var key in snapshot.HiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedKey = NormalizeLauncherHiddenKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey) || !seenFolders.Add(normalizedKey))
            {
                continue;
            }

            items.Add(CreateHiddenItem(
                LauncherHiddenItemKind.Folder,
                normalizedKey,
                BuildLauncherHiddenFallbackDisplayName(normalizedKey)));
        }

        foreach (var key in snapshot.HiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var normalizedKey = NormalizeLauncherHiddenKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey) || !seenApps.Add(normalizedKey))
            {
                continue;
            }

            items.Add(CreateHiddenItem(
                LauncherHiddenItemKind.Shortcut,
                normalizedKey,
                BuildLauncherHiddenFallbackDisplayName(normalizedKey)));
        }

        return items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectHiddenItems(
        StartMenuFolderNode folder,
        LauncherSettingsSnapshot snapshot,
        List<LauncherHiddenItemViewModel> items,
        HashSet<string> seenFolders,
        HashSet<string> seenApps)
    {
        foreach (var subFolder in folder.Folders)
        {
            var folderKey = NormalizeLauncherHiddenKey(subFolder.RelativePath);
            if (!string.IsNullOrWhiteSpace(folderKey) &&
                snapshot.HiddenLauncherFolderPaths.Contains(folderKey, StringComparer.OrdinalIgnoreCase) &&
                seenFolders.Add(folderKey))
            {
                items.Add(CreateHiddenItem(
                    LauncherHiddenItemKind.Folder,
                    folderKey,
                    subFolder.Name));
            }

            CollectHiddenItems(subFolder, snapshot, items, seenFolders, seenApps);
        }

        foreach (var app in folder.Apps)
        {
            var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
            if (string.IsNullOrWhiteSpace(appKey) ||
                !snapshot.HiddenLauncherAppPaths.Contains(appKey, StringComparer.OrdinalIgnoreCase) ||
                !seenApps.Add(appKey))
            {
                continue;
            }

            items.Add(CreateHiddenItem(
                LauncherHiddenItemKind.Shortcut,
                appKey,
                app.DisplayName));
        }
    }

    private LauncherHiddenItemViewModel CreateHiddenItem(
        LauncherHiddenItemKind kind,
        string key,
        string displayName)
    {
        var typeLabel = kind == LauncherHiddenItemKind.Folder
            ? L("settings.launcher.hidden_type_folder", "Folder")
            : L("settings.launcher.hidden_type_shortcut", "Shortcut");
        var iconSymbol = kind == LauncherHiddenItemKind.Folder
            ? Symbol.Folder
            : Symbol.Apps;

        return new LauncherHiddenItemViewModel(
            kind,
            key,
            displayName,
            typeLabel,
            iconSymbol,
            L("settings.launcher.restore_button", "Unhide"),
            RestoreHiddenItem);
    }

    private void RestoreHiddenItem(LauncherHiddenItemViewModel item)
    {
        var snapshot = _settingsFacade.LauncherPolicy.Get()?.Clone() ?? new LauncherSettingsSnapshot();
        var normalizedKey = NormalizeLauncherHiddenKey(item.Key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        IReadOnlyCollection<string>? changedKeys = item.Kind switch
        {
            LauncherHiddenItemKind.Folder => RemoveKey(snapshot.HiddenLauncherFolderPaths, normalizedKey)
                ? [nameof(LauncherSettingsSnapshot.HiddenLauncherFolderPaths)]
                : null,
            LauncherHiddenItemKind.Shortcut => RemoveKey(snapshot.HiddenLauncherAppPaths, normalizedKey)
                ? [nameof(LauncherSettingsSnapshot.HiddenLauncherAppPaths)]
                : null,
            _ => null
        };

        if (changedKeys is null)
        {
            return;
        }

        _settingsFacade.Settings.SaveSnapshot(SettingsScope.Launcher, snapshot, changedKeys: changedKeys);
        ReloadData();
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.launcher.title", "App Launcher");
        PageDescription = L("settings.launcher.description", "Manage hidden apps and folders in the App Launcher.");
        LauncherHeader = L("launcher.title", "App Launcher");
        LauncherSubtitle = OperatingSystem.IsLinux()
            ? L("launcher.subtitle_linux", "Displays installed apps discovered from Linux desktop entries.")
            : L("launcher.subtitle", "Displays all apps and folders based on the Windows Start menu structure.");
        HiddenHeader = L("settings.launcher.hidden_header", "Hidden Items");
        HiddenDescription = L("settings.launcher.hidden_desc", "Review hidden launcher entries and show them again.");
        HiddenHint = L("settings.launcher.hidden_hint", "In desktop edit mode, select a launcher icon and click Hide. Hidden entries appear here.");
        HiddenEmptyText = L("settings.launcher.hidden_empty", "No hidden items.");
        AppearanceHeader = L("settings.launcher.appearance_header", "Appearance");
        AppearanceDescription = L("settings.launcher.appearance_desc", "Customize the appearance of the App Launcher.");
        ShowTileBackgroundHeader = L("settings.launcher.show_tile_background_header", "Show tile background");
        ShowTileBackgroundDescription = L("settings.launcher.show_tile_background_desc", "Display a background card behind each app icon in the launcher.");
    }

    private CultureInfo ResolveCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_languageCode);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);

    private static string NormalizeLauncherHiddenKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();

    private static string BuildLauncherHiddenFallbackDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? key
            : fileName;
    }

    private static bool RemoveKey(ICollection<string> values, string key)
    {
        var existing = values.FirstOrDefault(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return false;
        }

        values.Remove(existing);
        return true;
    }
}
