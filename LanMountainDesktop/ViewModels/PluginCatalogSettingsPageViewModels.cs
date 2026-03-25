using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.PluginMarket;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.ViewModels;

public enum PluginCatalogPrimaryActionState
{
    Install,
    Update,
    RestartRequired,
    Installed,
    Incompatible
}

public sealed partial class PluginCatalogItemViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private readonly string _languageCode;
    private bool _isLoadingIcon;

    public PluginCatalogItemViewModel(
        PluginCatalogItemInfo plugin,
        LocalizationService localizationService,
        string languageCode)
    {
        Info = plugin;
        _localizationService = localizationService;
        _languageCode = languageCode;
        DeveloperInfo = ResolveDeveloperInfo();
        IconFallbackText = string.IsNullOrWhiteSpace(plugin.Name)
            ? "?"
            : plugin.Name.Trim()[0].ToString().ToUpperInvariant();
        ActionSymbol = Symbol.ArrowDownload;
        ActionTooltip = L("market.button.install", "Install");
    }

    public PluginCatalogItemInfo Info { get; }

    public string PluginId => Info.Id;

    public string Name => Info.Name;

    public string Description => Info.Description;

    public string Author => Info.Author;

    public string Version => Info.Version;

    public string ApiVersion => Info.ApiVersion;

    public string MinHostVersion => Info.MinHostVersion;

    public string ReadmeUrl => Info.ReadmeUrl;

    public IReadOnlyList<PluginCatalogSharedContractInfo> Dependencies => Info.SharedContracts;

    public IReadOnlyList<PluginPackageSourceInfo> PackageSources => Info.PackageSources;

    public IReadOnlyList<PluginCapabilityInfo> Capabilities => Info.Capabilities;

    public string IconFallbackText { get; }

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    private string _developerInfo = string.Empty;

    [ObservableProperty]
    private Symbol _actionSymbol;

    [ObservableProperty]
    private string _actionTooltip = string.Empty;

    [ObservableProperty]
    private bool _isActionEnabled = true;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _requiresRestart;

    [ObservableProperty]
    private bool _isCompatibleWithHost = true;

    public bool HasIcon => IconBitmap is not null;

    public PluginCatalogPrimaryActionState ActionState { get; private set; }

    partial void OnIconBitmapChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasIcon));
    }

    public async Task EnsureIconLoadedAsync(AirAppMarketIconService iconService)
    {
        if (_isLoadingIcon || IconBitmap is not null)
        {
            return;
        }

        _isLoadingIcon = true;
        try
        {
            IconBitmap = await iconService.LoadAsync(Info);
        }
        catch
        {
            IconBitmap = null;
        }
        finally
        {
            _isLoadingIcon = false;
        }
    }

    public void SetInstalling(bool value)
    {
        IsInstalling = value;
        RefreshActionPresentation();
    }

    public void ApplyInstallState(InstalledPluginInfo? installedPlugin, Version? hostVersion)
    {
        var isCompatible = hostVersion is null
            || !System.Version.TryParse(MinHostVersion, out var minHostVersion)
            || hostVersion >= minHostVersion;

        var isInstalled = installedPlugin is not null;
        var isUpdateAvailable = installedPlugin is not null && CompareVersions(Version, installedPlugin.Manifest.Version) > 0;
        var requiresRestart = installedPlugin is not null &&
                              installedPlugin.IsEnabled &&
                              !installedPlugin.IsLoaded &&
                              string.IsNullOrWhiteSpace(installedPlugin.ErrorMessage);

        IsCompatibleWithHost = isCompatible;
        IsInstalled = isInstalled;
        IsUpdateAvailable = isUpdateAvailable;
        RequiresRestart = requiresRestart;
        DeveloperInfo = ResolveDeveloperInfo();
        RefreshActionPresentation();
    }

    private void RefreshActionPresentation()
    {
        if (IsInstalling)
        {
            ActionState = IsUpdateAvailable ? PluginCatalogPrimaryActionState.Update : PluginCatalogPrimaryActionState.Install;
            ActionSymbol = Symbol.ArrowClockwise;
            ActionTooltip = L("market.button.installing", "Installing...");
            IsActionEnabled = false;
            return;
        }

        if (!IsCompatibleWithHost)
        {
            ActionState = PluginCatalogPrimaryActionState.Incompatible;
            ActionSymbol = Symbol.Warning;
            ActionTooltip = string.Format(
                CultureInfo.CurrentCulture,
                L("market.status.host_incompatible_format", "This host is too old. Version {0} or newer is required."),
                MinHostVersion);
            IsActionEnabled = false;
            return;
        }

        if (RequiresRestart)
        {
            ActionState = PluginCatalogPrimaryActionState.RestartRequired;
            ActionSymbol = Symbol.ArrowClockwise;
            ActionTooltip = L("market.button.restart", "Restart to apply");
            IsActionEnabled = true;
            return;
        }

        if (IsUpdateAvailable)
        {
            ActionState = PluginCatalogPrimaryActionState.Update;
            ActionSymbol = Symbol.ArrowSync;
            ActionTooltip = L("market.button.update", "Update");
            IsActionEnabled = true;
            return;
        }

        if (IsInstalled)
        {
            ActionState = PluginCatalogPrimaryActionState.Installed;
            ActionSymbol = Symbol.CheckmarkCircle;
            ActionTooltip = L("market.button.installed", "Installed");
            IsActionEnabled = false;
            return;
        }

        ActionState = PluginCatalogPrimaryActionState.Install;
        ActionSymbol = Symbol.ArrowDownload;
        ActionTooltip = L("market.button.install", "Install");
        IsActionEnabled = true;
    }

    private string ResolveDeveloperInfo()
    {
        return string.IsNullOrWhiteSpace(Author)
            ? L("settings.plugins.publisher_unknown", "Unknown publisher")
            : Author;
    }

    private int CompareVersions(string? left, string? right)
    {
        if (!System.Version.TryParse(left, out var leftVersion))
        {
            leftVersion = new Version(0, 0, 0);
        }

        if (!System.Version.TryParse(right, out var rightVersion))
        {
            rightVersion = new Version(0, 0, 0);
        }

        return leftVersion.CompareTo(rightVersion);
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class PluginCatalogDetailViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private readonly string _languageCode;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly Func<PluginCatalogItemViewModel, Task> _primaryActionAsync;
    private bool _isInitialized;

    public PluginCatalogDetailViewModel(
        PluginCatalogItemViewModel item,
        LocalizationService localizationService,
        string languageCode,
        AirAppMarketReadmeService readmeService,
        Func<PluginCatalogItemViewModel, Task> primaryActionAsync)
    {
        Item = item;
        _localizationService = localizationService;
        _languageCode = languageCode;
        _readmeService = readmeService;
        _primaryActionAsync = primaryActionAsync;

        Dependencies = new ObservableCollection<PluginCatalogSharedContractInfo>(item.Dependencies);
        VersionLabel = L("market.detail.version", "Version");
        PublisherLabel = L("market.detail.author", "Author");
        ApiVersionLabel = L("market.detail.api_version", "API Version");
        MinHostVersionLabel = L("market.detail.min_host_version", "Minimum Host Version");
        ReadmeHeader = L("market.detail.readme", "README");
        DependenciesHeader = L("market.detail.dependencies", "Dependencies");
        EmptyDependenciesText = L("market.detail.dependencies_empty", "No dependencies were declared by this plugin.");
    }

    public PluginCatalogItemViewModel Item { get; }

    public ObservableCollection<PluginCatalogSharedContractInfo> Dependencies { get; }

    public string DrawerTitle => Item.Name;

    public string VersionLabel { get; }

    public string PublisherLabel { get; }

    public string ApiVersionLabel { get; }

    public string MinHostVersionLabel { get; }

    public string ReadmeHeader { get; }

    public string DependenciesHeader { get; }

    public string EmptyDependenciesText { get; }

    public string ReadmeLoadingText => L("market.detail.readme_loading", "Loading README...");

    [ObservableProperty]
    private string _readmeMarkdown = string.Empty;

    [ObservableProperty]
    private bool _isReadmeLoading;

    [ObservableProperty]
    private string? _readmeError;

    public bool HasDependencies => Dependencies.Count > 0;

    public bool HasReadmeError => !string.IsNullOrWhiteSpace(ReadmeError);

    public bool HasReadmeContent => !IsReadmeLoading && !HasReadmeError && !string.IsNullOrWhiteSpace(ReadmeMarkdown);

    public IReadOnlyList<PluginPackageSourceInfo> PackageSources => Item.PackageSources;

    public IReadOnlyList<PluginCapabilityInfo> Capabilities => Item.Capabilities;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        IsReadmeLoading = true;
        OnReadmeStateChanged();

        try
        {
            var content = await _readmeService.LoadAsync(Item.Info);
            ReadmeMarkdown = string.IsNullOrWhiteSpace(content)
                ? L("market.detail.readme_empty", "README is empty.")
                : content;
            ReadmeError = null;
        }
        catch (Exception ex)
        {
            ReadmeMarkdown = string.Empty;
            ReadmeError = string.Format(
                CultureInfo.CurrentCulture,
                L("market.detail.readme_error_format", "README could not be loaded: {0}"),
                ex.Message);
        }
        finally
        {
            IsReadmeLoading = false;
            OnReadmeStateChanged();
        }
    }

    [RelayCommand]
    private Task PerformPrimaryActionAsync()
    {
        return _primaryActionAsync(Item);
    }

    partial void OnReadmeMarkdownChanged(string value)
    {
        OnReadmeStateChanged();
    }

    partial void OnReadmeErrorChanged(string? value)
    {
        OnReadmeStateChanged();
    }

    private void OnReadmeStateChanged()
    {
        OnPropertyChanged(nameof(HasReadmeContent));
        OnPropertyChanged(nameof(HasReadmeError));
        OnPropertyChanged(nameof(HasDependencies));
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}

public sealed partial class PluginCatalogSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IPluginCatalogSettingsService _pluginCatalog;
    private readonly LocalizationService _localizationService;
    private readonly AirAppMarketIconService _iconService;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly string _languageCode;
    private readonly Dictionary<string, InstalledPluginInfo> _installedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Version? _hostVersion;
    private bool _isInitialized;
    private bool _hasLoadedCatalog;

    public PluginCatalogSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        LocalizationService localizationService,
        AirAppMarketIconService iconService,
        AirAppMarketReadmeService readmeService)
    {
        _settingsFacade = settingsFacade;
        _pluginCatalog = _settingsFacade.PluginCatalog;
        _localizationService = localizationService;
        _iconService = iconService;
        _readmeService = readmeService;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        Version.TryParse(_settingsFacade.ApplicationInfo.GetAppVersionText(), out _hostVersion);
        RefreshLocalizedText();
        StatusMessage = L("market.status.loading", "Loading the official plugin catalog...");
    }

    public event Action<string?>? RestartRequested;

    public event Action<PluginCatalogItemViewModel>? DetailsRequested;

    public ObservableCollection<PluginCatalogItemViewModel> CatalogPlugins { get; } = [];

    public ObservableCollection<PluginCatalogItemViewModel> FilteredPlugins { get; } = [];

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _emptyStateText = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState;

    [ObservableProperty]
    private string _restartRequiredMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        await RefreshAsync();
    }

    public PluginCatalogDetailViewModel CreateDetailViewModel(PluginCatalogItemViewModel item)
    {
        return new PluginCatalogDetailViewModel(
            item,
            _localizationService,
            _languageCode,
            _readmeService,
            ExecutePrimaryActionAsync);
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
            StatusMessage = L("market.status.loading", "Loading the official plugin catalog...");
            RefreshInstalledSnapshot();

            var result = await _pluginCatalog.LoadCatalogAsync();
            if (!result.Success)
            {
                _hasLoadedCatalog = false;
                CatalogPlugins.Clear();
                FilteredPlugins.Clear();
                ShowEmptyState = true;
                EmptyStateText = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("market.list.empty", "The plugin catalog has not been loaded yet.")
                    : result.ErrorMessage;
                StatusMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("market.status.load_failed_format", "Failed to load the plugin catalog: Unknown")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("market.status.load_failed_format", "Failed to load the plugin catalog: {0}"),
                        result.ErrorMessage);
                return;
            }

            _hasLoadedCatalog = true;
            CatalogPlugins.Clear();
            foreach (var plugin in result.Plugins)
            {
                var item = new PluginCatalogItemViewModel(plugin, _localizationService, _languageCode);
                item.ApplyInstallState(ResolveInstalledPlugin(plugin.Id), _hostVersion);
                CatalogPlugins.Add(item);
                _ = item.EnsureIconLoadedAsync(_iconService);
            }

            ApplyFilter();

            StatusMessage = string.Equals(result.Source, "Cache", StringComparison.OrdinalIgnoreCase)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.loaded_cache_format", "Official source unavailable. Loaded {0} plugin(s) from cache. Reason: {1}"),
                    CatalogPlugins.Count,
                    result.WarningMessage ?? L("market.detail.unknown", "Unknown"))
                : string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.loaded_network_format", "Loaded {0} plugin(s) from the official source."),
                    CatalogPlugins.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDetails(PluginCatalogItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        DetailsRequested?.Invoke(item);
    }

    [RelayCommand]
    private Task ExecutePrimaryActionAsync(PluginCatalogItemViewModel? item)
    {
        return item is null ? Task.CompletedTask : ExecutePrimaryActionCoreAsync(item);
    }

    private async Task ExecutePrimaryActionCoreAsync(PluginCatalogItemViewModel item)
    {
        if (item.IsInstalling)
        {
            return;
        }

        if (item.ActionState == PluginCatalogPrimaryActionState.RestartRequired)
        {
            RestartRequested?.Invoke(RestartRequiredMessage);
            return;
        }

        if (!item.IsActionEnabled)
        {
            return;
        }

        try
        {
            item.SetInstalling(true);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                L("market.status.installing_format", "Downloading and staging plugin '{0}'..."),
                item.Name);

            var result = await _pluginCatalog.InstallAsync(item.PluginId);
            if (result.Success)
            {
                RefreshInstalledSnapshot();
                RefreshItemStates();
                
                // 设置更明显的状态消息
                var pluginName = result.PluginName ?? item.Name;
                StatusMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.install_success_restart_format", "✓ Plugin '{0}' installed successfully! Please restart the application to activate it."),
                    pluginName);
                
                // 触发重启提醒
                RestartRequested?.Invoke(string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.dialog.restart_message_format", "Plugin '{0}' has been installed successfully.\n\nTo use this plugin, you need to restart the application now.\n\nWould you like to restart?"),
                    pluginName));
                return;
            }

            StatusMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.install_failed_format", "Failed to install plugin: {0}"),
                    item.Name)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.install_failed_format", "Failed to install plugin: {0}"),
                    result.ErrorMessage);
        }
        finally
        {
            item.SetInstalling(false);
            RefreshItemStates();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void RefreshItemStates()
    {
        foreach (var item in CatalogPlugins)
        {
            item.ApplyInstallState(ResolveInstalledPlugin(item.PluginId), _hostVersion);
        }

        ApplyFilter();
    }

    private void RefreshInstalledSnapshot()
    {
        _installedPlugins.Clear();
        foreach (var plugin in _settingsFacade.PluginManagement.GetInstalledPlugins())
        {
            _installedPlugins[plugin.Manifest.Id] = plugin;
        }
    }

    private InstalledPluginInfo? ResolveInstalledPlugin(string pluginId)
    {
        return _installedPlugins.TryGetValue(pluginId, out var installedPlugin)
            ? installedPlugin
            : null;
    }

    private void ApplyFilter()
    {
        FilteredPlugins.Clear();

        IEnumerable<PluginCatalogItemViewModel> filtered = CatalogPlugins;
        var query = SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.PluginId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Info.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in filtered)
        {
            FilteredPlugins.Add(item);
        }

        ShowEmptyState = FilteredPlugins.Count == 0;
        EmptyStateText = !_hasLoadedCatalog
            ? L("market.list.empty", "The plugin catalog has not been loaded yet.")
            : string.IsNullOrWhiteSpace(query)
                ? L("settings.plugins.marketplace_empty", "No marketplace plugins are available right now.")
                : L("market.list.no_results", "No plugins match the current search.");
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.plugin_catalog.title", "Plugin Catalog");
        PageDescription = L("settings.plugin_catalog.subtitle", "Browse plugins from the official LanAirApp source and stage installs.");
        SearchPlaceholder = L("market.toolbar.search_placeholder", "Search plugins");
        RefreshButtonText = L("market.toolbar.refresh", "Refresh");
        RestartRequiredMessage = L("settings.plugins.restart_required", "Plugin changes take effect after restart.");
        EmptyStateText = L("market.list.empty", "The plugin catalog has not been loaded yet.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
