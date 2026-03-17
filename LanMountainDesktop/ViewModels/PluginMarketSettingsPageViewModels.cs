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

public enum PluginMarketPrimaryActionState
{
    Install,
    Update,
    RestartRequired,
    Installed,
    Incompatible
}

public sealed partial class PluginMarketItemViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private readonly string _languageCode;
    private bool _isLoadingIcon;

    public PluginMarketItemViewModel(
        PluginMarketPluginInfo plugin,
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

    public PluginMarketPluginInfo Info { get; }

    public string PluginId => Info.Id;

    public string Name => Info.Name;

    public string Description => Info.Description;

    public string Author => Info.Author;

    public string Version => Info.Version;

    public string ApiVersion => Info.ApiVersion;

    public string MinHostVersion => Info.MinHostVersion;

    public string ReadmeUrl => Info.ReadmeUrl;

    public IReadOnlyList<PluginMarketDependencyInfo> Dependencies => Info.Dependencies;

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

    public PluginMarketPrimaryActionState ActionState { get; private set; }

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
            ActionState = IsUpdateAvailable ? PluginMarketPrimaryActionState.Update : PluginMarketPrimaryActionState.Install;
            ActionSymbol = Symbol.ArrowClockwise;
            ActionTooltip = L("market.button.installing", "Installing...");
            IsActionEnabled = false;
            return;
        }

        if (!IsCompatibleWithHost)
        {
            ActionState = PluginMarketPrimaryActionState.Incompatible;
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
            ActionState = PluginMarketPrimaryActionState.RestartRequired;
            ActionSymbol = Symbol.ArrowClockwise;
            ActionTooltip = L("market.button.restart", "Restart to apply");
            IsActionEnabled = true;
            return;
        }

        if (IsUpdateAvailable)
        {
            ActionState = PluginMarketPrimaryActionState.Update;
            ActionSymbol = Symbol.ArrowSync;
            ActionTooltip = L("market.button.update", "Update");
            IsActionEnabled = true;
            return;
        }

        if (IsInstalled)
        {
            ActionState = PluginMarketPrimaryActionState.Installed;
            ActionSymbol = Symbol.CheckmarkCircle;
            ActionTooltip = L("market.button.installed", "Installed");
            IsActionEnabled = false;
            return;
        }

        ActionState = PluginMarketPrimaryActionState.Install;
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

public sealed partial class PluginMarketDetailViewModel : ViewModelBase
{
    private readonly LocalizationService _localizationService;
    private readonly string _languageCode;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly Func<PluginMarketItemViewModel, Task> _primaryActionAsync;
    private bool _isInitialized;

    public PluginMarketDetailViewModel(
        PluginMarketItemViewModel item,
        LocalizationService localizationService,
        string languageCode,
        AirAppMarketReadmeService readmeService,
        Func<PluginMarketItemViewModel, Task> primaryActionAsync)
    {
        Item = item;
        _localizationService = localizationService;
        _languageCode = languageCode;
        _readmeService = readmeService;
        _primaryActionAsync = primaryActionAsync;

        Dependencies = new ObservableCollection<PluginMarketDependencyInfo>(item.Dependencies);
        VersionLabel = L("market.detail.version", "Version");
        PublisherLabel = L("market.detail.author", "Author");
        ApiVersionLabel = L("market.detail.api_version", "API Version");
        MinHostVersionLabel = L("market.detail.min_host_version", "Minimum Host Version");
        ReadmeHeader = L("market.detail.readme", "README");
        DependenciesHeader = L("market.detail.dependencies", "Dependencies");
        EmptyDependenciesText = L("market.detail.dependencies_empty", "No dependencies were declared by this plugin.");
    }

    public PluginMarketItemViewModel Item { get; }

    public ObservableCollection<PluginMarketDependencyInfo> Dependencies { get; }

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

public sealed partial class PluginMarketSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService;
    private readonly AirAppMarketIconService _iconService;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly string _languageCode;
    private readonly Dictionary<string, InstalledPluginInfo> _installedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Version? _hostVersion;
    private bool _isInitialized;
    private bool _hasLoadedMarket;

    public PluginMarketSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        LocalizationService localizationService,
        AirAppMarketIconService iconService,
        AirAppMarketReadmeService readmeService)
    {
        _settingsFacade = settingsFacade;
        _localizationService = localizationService;
        _iconService = iconService;
        _readmeService = readmeService;
        _languageCode = _localizationService.NormalizeLanguageCode(_settingsFacade.Region.Get().LanguageCode);
        Version.TryParse(_settingsFacade.ApplicationInfo.GetAppVersionText(), out _hostVersion);
        RefreshLocalizedText();
        StatusMessage = L("market.status.loading", "Loading the official plugin market...");
    }

    public event Action<string?>? RestartRequested;

    public event Action<PluginMarketItemViewModel>? DetailsRequested;

    public ObservableCollection<PluginMarketItemViewModel> MarketPlugins { get; } = [];

    public ObservableCollection<PluginMarketItemViewModel> FilteredPlugins { get; } = [];

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

    public PluginMarketDetailViewModel CreateDetailViewModel(PluginMarketItemViewModel item)
    {
        return new PluginMarketDetailViewModel(
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
            StatusMessage = L("market.status.loading", "Loading the official plugin market...");
            RefreshInstalledSnapshot();

            var result = await _settingsFacade.PluginMarket.LoadIndexAsync();
            if (!result.Success)
            {
                _hasLoadedMarket = false;
                MarketPlugins.Clear();
                FilteredPlugins.Clear();
                ShowEmptyState = true;
                EmptyStateText = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("market.list.empty", "The plugin market has not been loaded yet.")
                    : result.ErrorMessage;
                StatusMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? L("market.status.load_failed_format", "Failed to load the plugin market: Unknown")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("market.status.load_failed_format", "Failed to load the plugin market: {0}"),
                        result.ErrorMessage);
                return;
            }

            _hasLoadedMarket = true;
            MarketPlugins.Clear();
            foreach (var plugin in result.Plugins)
            {
                var item = new PluginMarketItemViewModel(plugin, _localizationService, _languageCode);
                item.ApplyInstallState(ResolveInstalledPlugin(plugin.Id), _hostVersion);
                MarketPlugins.Add(item);
                _ = item.EnsureIconLoadedAsync(_iconService);
            }

            ApplyFilter();

            StatusMessage = string.Equals(result.Source, "Cache", StringComparison.OrdinalIgnoreCase)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.loaded_cache_format", "Official source unavailable. Loaded {0} plugin(s) from cache. Reason: {1}"),
                    MarketPlugins.Count,
                    result.WarningMessage ?? L("market.detail.unknown", "Unknown"))
                : string.Format(
                    CultureInfo.CurrentCulture,
                    L("market.status.loaded_network_format", "Loaded {0} plugin(s) from the official source."),
                    MarketPlugins.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDetails(PluginMarketItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        DetailsRequested?.Invoke(item);
    }

    [RelayCommand]
    private Task ExecutePrimaryActionAsync(PluginMarketItemViewModel? item)
    {
        return item is null ? Task.CompletedTask : ExecutePrimaryActionCoreAsync(item);
    }

    private async Task ExecutePrimaryActionCoreAsync(PluginMarketItemViewModel item)
    {
        if (item.IsInstalling)
        {
            return;
        }

        if (item.ActionState == PluginMarketPrimaryActionState.RestartRequired)
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

            var result = await _settingsFacade.PluginMarket.InstallAsync(item.PluginId);
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
        foreach (var item in MarketPlugins)
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

        IEnumerable<PluginMarketItemViewModel> filtered = MarketPlugins;
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
        EmptyStateText = !_hasLoadedMarket
            ? L("market.list.empty", "The plugin market has not been loaded yet.")
            : string.IsNullOrWhiteSpace(query)
                ? L("settings.plugins.marketplace_empty", "No marketplace plugins are available right now.")
                : L("market.list.no_results", "No plugins match the current search.");
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.plugin_market.title", "Plugin Market");
        PageDescription = L("settings.plugin_market.subtitle", "Browse plugins from the official LanAirApp source and stage installs.");
        SearchPlaceholder = L("market.toolbar.search_placeholder", "Search plugins");
        RefreshButtonText = L("market.toolbar.refresh", "Refresh");
        RestartRequiredMessage = L("settings.plugins.restart_required", "Plugin changes take effect after restart.");
        EmptyStateText = L("market.list.empty", "The plugin market has not been loaded yet.");
    }

    private string L(string key, string fallback)
        => _localizationService.GetString(_languageCode, key, fallback);
}
