using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

internal sealed class PluginMarketEmbeddedView : UserControl, IDisposable
{
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.Parse("#14000000"));
    private static readonly IBrush SelectedSurfaceBrush = new SolidColorBrush(Color.Parse("#1A0EA5E9"));
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(Color.Parse("#24FFFFFF"));
    private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(Color.Parse("#7C0EA5E9"));
    private static readonly IBrush IconSurfaceBrush = new SolidColorBrush(Color.Parse("#221E3A8A"));
    private static readonly IBrush ChipBrush = new SolidColorBrush(Color.Parse("#22000000"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#CC94A3B8"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#FF0F766E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FF9A6700"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FFC42B1C"));

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly PluginRuntimeService _runtime;
    private readonly AirAppMarketIndexService _indexService;
    private readonly AirAppMarketInstallService _installService;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly AirAppMarketIconService _iconService;
    private readonly Version? _hostVersion;
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly TextBox _searchTextBox;
    private readonly Button _refreshButton;
    private readonly TextBlock _statusTextBlock;
    private readonly StackPanel _pluginListHost;
    private readonly Border _detailBorder;

    private AirAppMarketIndexDocument? _document;
    private AirAppMarketPluginEntry? _selectedPlugin;
    private Dictionary<string, PluginCatalogEntry> _installedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _readmeContents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _readmeErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap?> _iconBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadingIconPluginIds = new(StringComparer.OrdinalIgnoreCase);
    private string _marketSourceDisplay = AirAppMarketDefaults.DefaultIndexUrl;
    private string? _loadingReadmePluginId;
    private string? _installingPluginId;
    private bool _isRefreshing;
    private bool _isInstalling;
    private bool _hasLoadedOnce;
    private bool _isDisposed;
    private bool _isAttachedToVisualTree;

    public PluginMarketEmbeddedView(PluginRuntimeService runtime)
    {
        _runtime = runtime;
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "AirAppMarket");
        _indexService = new AirAppMarketIndexService(new AirAppMarketCacheService(dataDirectory));
        _installService = new AirAppMarketInstallService(runtime, dataDirectory);
        _readmeService = new AirAppMarketReadmeService();
        _iconService = new AirAppMarketIconService();
        _hostVersion = typeof(App).Assembly.GetName().Version;

        _searchTextBox = new TextBox
        {
            MinWidth = 260,
            Watermark = T("market.toolbar.search_placeholder", "Search plugins")
        };
        _searchTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                RebuildSurface();
            }
        };

        _refreshButton = new Button
        {
            Content = T("market.toolbar.refresh", "Refresh"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _refreshButton.Click += OnRefreshClick;

        _statusTextBlock = new TextBlock
        {
            Text = T("market.status.loading", "Loading the official plugin market..."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = WarningBrush
        };

        _pluginListHost = new StackPanel
        {
            Spacing = 10
        };

        _detailBorder = CreatePanelShell(18);

        Content = BuildLayout();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public void RefreshInstalledSnapshot()
    {
        _installedPlugins = _runtime.Catalog
            .ToDictionary(entry => entry.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        RebuildSurface();
    }

    public void RefreshLocalization()
    {
        _searchTextBox.Watermark = T("market.toolbar.search_placeholder", "Search plugins");
        _refreshButton.Content = T("market.toolbar.refresh", "Refresh");
        RebuildSurface();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _lifetimeCts.Cancel();

        foreach (var bitmap in _iconBitmaps.Values)
        {
            bitmap?.Dispose();
        }

        _iconBitmaps.Clear();
        _lifetimeCts.Dispose();
        _iconService.Dispose();
        _readmeService.Dispose();
        _installService.Dispose();
        _indexService.Dispose();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        if (_hasLoadedOnce)
        {
            return;
        }

        _hasLoadedOnce = true;
        UiExceptionGuard.FireAndForgetGuarded(
            RefreshAsync,
            "PluginMarket.InitialLoad",
            BuildMarketContext());
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 16
        };

        var toolbar = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8
        };

        var actionRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        actionRow.Children.Add(_searchTextBox);
        actionRow.Children.Add(_refreshButton);
        Grid.SetColumn(_refreshButton, 1);

        toolbar.Children.Add(actionRow);
        toolbar.Children.Add(_statusTextBlock);
        Grid.SetRow(_statusTextBlock, 1);

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("430,*"),
            ColumnSpacing = 16
        };

        var listShell = CreatePanelShell(14);
        listShell.Child = new ScrollViewer
        {
            Content = _pluginListHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        contentGrid.Children.Add(listShell);
        contentGrid.Children.Add(_detailBorder);
        Grid.SetColumn(_detailBorder, 1);

        root.Children.Add(toolbar);
        root.Children.Add(contentGrid);
        Grid.SetRow(contentGrid, 1);

        return root;
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        UiExceptionGuard.FireAndForgetGuarded(
            RefreshAsync,
            "PluginMarket.Refresh",
            BuildMarketContext(),
            ex => HandleTopLevelUiActionExceptionAsync(
                ex,
                F(
                    "market.status.load_failed_format",
                    "Failed to load the plugin market: {0}",
                    DescribeException(ex))));
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || _isDisposed || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _isRefreshing = true;
        _refreshButton.IsEnabled = false;
        SetStatus(T("market.status.loading", "Loading the official plugin market..."), WarningBrush);

        try
        {
            RefreshInstalledSnapshot();

            var result = await _indexService.LoadAsync(_lifetimeCts.Token);
            if (!CanUpdateUi())
            {
                return;
            }

            if (!result.Success || result.Document is null)
            {
                _document = null;
                _selectedPlugin = null;
                AppLogger.Warn(
                    "PluginMarket",
                    $"Refresh failed. Source=None; Warning={result.WarningMessage ?? string.Empty}; Error={result.ErrorMessage ?? string.Empty}; Context={BuildMarketContext()}");
                SetStatus(
                    F(
                        "market.status.load_failed_format",
                        "Failed to load the plugin market: {0}",
                        result.ErrorMessage ?? T("market.detail.unknown", "Unknown")),
                    ErrorBrush);
                RebuildSurface();
                return;
            }

            _document = result.Document;
            _marketSourceDisplay = result.SourceLocation ?? AirAppMarketDefaults.DefaultIndexUrl;
            _selectedPlugin = ResolveSelectedPlugin(_selectedPlugin?.Id, result.Document.Plugins);
            AppLogger.Info(
                "PluginMarket",
                $"Refresh completed. Source={result.Source}; PluginCount={result.Document.Plugins.Count}; SourceLocation={result.SourceLocation ?? string.Empty}; Warning={result.WarningMessage ?? string.Empty}; Context={BuildMarketContext()}");

            var statusMessage = result.Source == AirAppMarketLoadSource.Cache
                ? F(
                    "market.status.loaded_cache_format",
                    "Official source unavailable. Loaded {0} plugin(s) from cache. Reason: {1}",
                    result.Document.Plugins.Count,
                    result.WarningMessage ?? T("market.detail.unknown", "Unknown"))
                : F(
                    "market.status.loaded_network_format",
                    "Loaded {0} plugin(s) from the official source.",
                    result.Document.Plugins.Count);

            SetStatus(statusMessage, result.Source == AirAppMarketLoadSource.Cache ? WarningBrush : SuccessBrush);
            RebuildSurface();
            await EnsureReadmeLoadedAsync(_selectedPlugin);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            AppLogger.Info("PluginMarket", $"Refresh canceled because the view is being disposed. Context={BuildMarketContext()}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "PluginMarket",
                $"Refresh threw unexpectedly. ExceptionType={ex.GetType().FullName}; Classification={ClassifyException(ex)}; Context={BuildMarketContext()}",
                ex);
            if (CanUpdateUi())
            {
                SetStatus(
                    F(
                        "market.status.load_failed_format",
                        "Failed to load the plugin market: {0}",
                        DescribeException(ex)),
                    ErrorBrush);
                _document = null;
                _selectedPlugin = null;
                RebuildSurface();
            }
        }
        finally
        {
            _isRefreshing = false;
            _refreshButton.IsEnabled = !_isDisposed;
        }
    }

    private void RebuildSurface()
    {
        if (_isDisposed)
        {
            return;
        }

        var filteredPlugins = GetFilteredPlugins();
        _selectedPlugin = filteredPlugins.Count > 0
            ? ResolveSelectedPlugin(_selectedPlugin?.Id, filteredPlugins)
            : null;

        BuildPluginList(filteredPlugins);
        BuildDetailPanel();

        _ = EnsureReadmeLoadedAsync(_selectedPlugin);
        foreach (var plugin in filteredPlugins)
        {
            _ = EnsureIconLoadedAsync(plugin);
        }
    }

    private List<AirAppMarketPluginEntry> GetFilteredPlugins()
    {
        if (_document is null)
        {
            return [];
        }

        var query = (_searchTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return _document.Plugins.ToList();
        }

        return _document.Plugins
            .Where(plugin =>
                plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                plugin.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void BuildPluginList(IReadOnlyList<AirAppMarketPluginEntry> plugins)
    {
        _pluginListHost.Children.Clear();

        if (_document is null)
        {
            _pluginListHost.Children.Add(CreateEmptyState(T("market.list.empty", "The plugin market has not been loaded yet.")));
            return;
        }

        if (plugins.Count == 0)
        {
            _pluginListHost.Children.Add(CreateEmptyState(T("market.list.no_results", "No plugins match the current search.")));
            return;
        }

        foreach (var plugin in plugins)
        {
            _pluginListHost.Children.Add(CreatePluginListItem(plugin));
        }
    }

    private Control CreatePluginListItem(AirAppMarketPluginEntry plugin)
    {
        var installState = ResolveInstallState(plugin, out var installedPlugin);
        var isCompatible = IsCompatibleWithHost(plugin);
        var isSelected = string.Equals(_selectedPlugin?.Id, plugin.Id, StringComparison.OrdinalIgnoreCase);

        var titleBlock = new TextBlock
        {
            Text = plugin.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var subtitleBlock = new TextBlock
        {
            Text = F("market.card.subtitle_format", "{0} | v{1}", plugin.Author, plugin.Version),
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };

        var descriptionBlock = new TextBlock
        {
            Text = plugin.Description,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40
        };

        var chips = CreateChipWrapPanel(
            CreateStateChip(T(StateKey(installState), StateFallback(installState))));

        if (installedPlugin is not null)
        {
            chips.Children.Add(CreateStateChip(installedPlugin.IsLoaded
                ? T("market.card.loaded", "Loaded")
                : T("market.card.pending_restart", "Restart required")));
        }

        foreach (var tag in plugin.Tags.Take(3))
        {
            chips.Children.Add(CreateStateChip(tag));
        }

        var summaryStack = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                titleBlock,
                subtitleBlock,
                descriptionBlock,
                chips
            }
        };

        var selectGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            Children =
            {
                CreatePluginIcon(plugin, 56),
                summaryStack
            }
        };
        Grid.SetColumn(summaryStack, 1);

        var selectButton = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = selectGrid
        };
        selectButton.Click += (_, _) => UiExceptionGuard.FireAndForgetGuarded(
            () => SelectPluginAsync(plugin),
            "PluginMarket.SelectPlugin",
            BuildMarketContext(plugin));

        var rightPanel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                CreateInstallButton(plugin, installState, isCompatible, 96),
                new TextBlock
                {
                    Text = installedPlugin is null
                        ? string.Empty
                        : installedPlugin.IsLoaded
                            ? T("market.card.loaded", "Loaded")
                            : T("market.card.pending_restart", "Restart required"),
                    FontSize = 12,
                    Foreground = MutedBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    IsVisible = installedPlugin is not null
                }
            }
        };

        var layoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14,
            Children =
            {
                selectButton,
                rightPanel
            }
        };
        Grid.SetColumn(rightPanel, 1);

        return new Border
        {
            Background = isSelected ? SelectedSurfaceBrush : SurfaceBrush,
            BorderBrush = isSelected ? SelectedBorderBrush : CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            Child = layoutGrid
        };
    }

    private void BuildDetailPanel()
    {
        if (_selectedPlugin is null)
        {
            _detailBorder.Child = CreateEmptyState(T("market.detail.placeholder", "Select a plugin on the left to inspect details."));
            return;
        }

        var plugin = _selectedPlugin;
        var installState = ResolveInstallState(plugin, out var installedPlugin);
        var isCompatible = IsCompatibleWithHost(plugin);

        var headerSummary = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = plugin.Name,
                    FontSize = 26,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = F("market.detail.author_subtitle_format", "By {0}", plugin.Author),
                    Foreground = MutedBrush,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = plugin.Description,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var headerChips = CreateChipWrapPanel(
            CreateStateChip(T(StateKey(installState), StateFallback(installState))),
            CreateStateChip(plugin.GetVersionSummary()));
        foreach (var tag in plugin.Tags)
        {
            headerChips.Children.Add(CreateStateChip(tag));
        }

        headerSummary.Children.Add(headerChips);

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 16,
            Children =
            {
                CreatePluginIcon(plugin, 76),
                headerSummary,
                CreateInstallButton(plugin, installState, isCompatible, 120)
            }
        };
        Grid.SetColumn(headerSummary, 1);
        Grid.SetColumn(headerGrid.Children[2], 2);

        var detailPanel = new StackPanel
        {
            Spacing = 18,
            Children =
            {
                headerGrid
            }
        };

        if (!isCompatible)
        {
            detailPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#24FFC42B1C")),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Child = new TextBlock
                {
                    Text = F(
                        "market.status.host_incompatible_format",
                        "This host is too old. Version {0} or newer is required.",
                        plugin.MinHostVersion),
                    Foreground = ErrorBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        detailPanel.Children.Add(CreateSectionTitle(T("market.detail.readme", "README")));
        detailPanel.Children.Add(new Border
        {
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = GetReadmeContent(plugin),
                TextWrapping = TextWrapping.Wrap
            }
        });

        detailPanel.Children.Add(CreateSectionTitle(T("market.detail.plugin_information", "Plugin Information")));
        detailPanel.Children.Add(CreatePluginInfoSection(plugin, installedPlugin, installState));

        _detailBorder.Child = new ScrollViewer
        {
            Content = detailPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Control CreatePluginInfoSection(
        AirAppMarketPluginEntry plugin,
        PluginCatalogEntry? installedPlugin,
        AirAppMarketInstallState installState)
    {
        var infoPanel = new StackPanel
        {
            Spacing = 14
        };

        var cardWrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        foreach (var card in new[]
        {
            CreateInfoCard(T("market.detail.version", "Version"), $"v{plugin.Version}"),
            CreateInfoCard(T("market.detail.installed_version", "Installed Version"), installedPlugin?.Manifest.Version ?? T("market.detail.not_installed", "Not installed")),
            CreateInfoCard(T("market.detail.api_version", "API Version"), plugin.ApiVersion),
            CreateInfoCard(T("market.detail.min_host_version", "Minimum Host Version"), plugin.MinHostVersion),
            CreateInfoCard(T("market.detail.package_size", "Package Size"), FormatPackageSize(plugin.PackageSizeBytes)),
            CreateInfoCard(T("market.detail.published_at", "Published At"), FormatTimestamp(plugin.PublishedAt)),
            CreateInfoCard(T("market.detail.updated_at", "Updated At"), FormatTimestamp(plugin.UpdatedAt)),
            CreateInfoCard(T("market.detail.state", "Install State"), T(StateKey(installState), StateFallback(installState)))
        })
        {
            cardWrap.Children.Add(card);
        }

        infoPanel.Children.Add(cardWrap);
        infoPanel.Children.Add(CreateInfoRow(T("market.detail.tags", "Tags"), plugin.Tags.Count == 0 ? T("market.detail.unknown", "Unknown") : string.Join(", ", plugin.Tags)));
        infoPanel.Children.Add(CreateInfoRow(T("market.detail.project", "Project"), plugin.ProjectUrl));
        infoPanel.Children.Add(CreateInfoRow(T("market.detail.homepage", "Homepage"), plugin.HomepageUrl));
        infoPanel.Children.Add(CreateInfoRow(T("market.detail.repository", "Repository"), plugin.RepositoryUrl));
        infoPanel.Children.Add(CreateInfoRow(T("market.detail.market_source", "Market Source"), _marketSourceDisplay));

        if (!string.IsNullOrWhiteSpace(plugin.ReleaseNotes))
        {
            infoPanel.Children.Add(CreateInfoRow(T("market.detail.release_notes", "Release Notes"), plugin.ReleaseNotes));
        }

        return infoPanel;
    }

    private Button CreateInstallButton(
        AirAppMarketPluginEntry plugin,
        AirAppMarketInstallState installState,
        bool isCompatible,
        double minWidth)
    {
        var isThisInstalling = _isInstalling &&
            string.Equals(_installingPluginId, plugin.Id, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Content = isThisInstalling
                ? T("market.button.installing", "Installing...")
                : T(ButtonKey(installState), ButtonFallback(installState)),
            IsEnabled = !_isInstalling && isCompatible && installState != AirAppMarketInstallState.Installed,
            MinWidth = minWidth,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        button.Click += (_, _) => UiExceptionGuard.FireAndForgetGuarded(
            async () =>
            {
                _selectedPlugin = plugin;
                RebuildSurface();
                await EnsureReadmeLoadedAsync(plugin);
                await InstallSelectedPluginAsync(plugin);
            },
            "PluginMarket.InstallPlugin",
            BuildMarketContext(plugin),
            ex => HandleTopLevelUiActionExceptionAsync(
                ex,
                F(
                    "market.status.install_failed_format",
                    "Failed to install plugin: {0}",
                    DescribeException(ex))));

        return button;
    }

    private async Task SelectPluginAsync(AirAppMarketPluginEntry plugin)
    {
        _selectedPlugin = plugin;
        RebuildSurface();
        await EnsureReadmeLoadedAsync(plugin);
    }

    private async Task InstallSelectedPluginAsync(AirAppMarketPluginEntry plugin)
    {
        if (_isInstalling || _isDisposed || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _isInstalling = true;
        _installingPluginId = plugin.Id;
        BuildDetailPanel();
        BuildPluginList(GetFilteredPlugins());
        SetStatus(
            F("market.status.installing_format", "Downloading and staging plugin '{0}'...", plugin.Name),
            WarningBrush);

        try
        {
            var result = await _installService.InstallAsync(plugin, _lifetimeCts.Token);
            if (!CanUpdateUi())
            {
                return;
            }

            if (!result.Success || result.Manifest is null)
            {
                SetStatus(
                    F(
                        "market.status.install_failed_format",
                        "Failed to install plugin: {0}",
                        result.ErrorMessage ?? T("market.detail.unknown", "Unknown")),
                    ErrorBrush);
                return;
            }

            RefreshInstalledSnapshot();
            SetStatus(
                F(
                    "market.status.install_success_format",
                    "Plugin '{0}' has been staged. Restart the app to apply it.",
                    result.Manifest.Name),
                SuccessBrush);
            RebuildSurface();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            AppLogger.Info(
                "PluginMarket",
                $"Install canceled because the view is being disposed. PluginId={plugin.Id}; Context={BuildMarketContext(plugin)}");
        }
        finally
        {
            _isInstalling = false;
            _installingPluginId = null;
            RebuildSurface();
        }
    }

    private async Task EnsureReadmeLoadedAsync(AirAppMarketPluginEntry? plugin)
    {
        if (plugin is null ||
            _isDisposed ||
            _readmeContents.ContainsKey(plugin.Id) ||
            string.Equals(_loadingReadmePluginId, plugin.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loadingReadmePluginId = plugin.Id;
        _readmeErrors.Remove(plugin.Id);
        BuildDetailPanel();

        try
        {
            var readme = await _readmeService.LoadAsync(plugin, _lifetimeCts.Token);
            _readmeContents[plugin.Id] = string.IsNullOrWhiteSpace(readme)
                ? T("market.detail.readme_empty", "README is empty.")
                : readme.Trim();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            AppLogger.Info(
                "PluginMarket",
                $"README load canceled because the view is being disposed. PluginId={plugin.Id}; Context={BuildMarketContext(plugin)}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "PluginMarket",
                $"README load failed. PluginId={plugin.Id}; ExceptionType={ex.GetType().FullName}; Classification={ClassifyException(ex)}; Context={BuildMarketContext(plugin)}",
                ex);
            _readmeErrors[plugin.Id] = ex.Message;
        }
        finally
        {
            _loadingReadmePluginId = null;
            if (CanUpdateUi() &&
                string.Equals(_selectedPlugin?.Id, plugin.Id, StringComparison.OrdinalIgnoreCase))
            {
                BuildDetailPanel();
            }
        }
    }

    private async Task EnsureIconLoadedAsync(AirAppMarketPluginEntry? plugin)
    {
        if (plugin is null ||
            _isDisposed ||
            _iconBitmaps.ContainsKey(plugin.Id) ||
            !_loadingIconPluginIds.Add(plugin.Id))
        {
            return;
        }

        try
        {
            _iconBitmaps[plugin.Id] = await _iconService.LoadAsync(plugin, _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            AppLogger.Info(
                "PluginMarket",
                $"Icon load canceled because the view is being disposed. PluginId={plugin.Id}; Context={BuildMarketContext(plugin)}");
        }
        catch
        {
            _iconBitmaps[plugin.Id] = null;
        }
        finally
        {
            _loadingIconPluginIds.Remove(plugin.Id);
            if (CanUpdateUi())
            {
                RebuildSurface();
            }
        }
    }

    private Task HandleTopLevelUiActionExceptionAsync(Exception ex, string fallbackStatus)
    {
        if (CanUpdateUi())
        {
            SetStatus(fallbackStatus, ErrorBrush);
            RebuildSurface();
        }

        return Task.CompletedTask;
    }

    private bool CanUpdateUi()
    {
        return !_isDisposed && _isAttachedToVisualTree && !_lifetimeCts.IsCancellationRequested;
    }

    private string BuildMarketContext(AirAppMarketPluginEntry? plugin = null)
    {
        return UiExceptionGuard.BuildContext(
            ("SelectedPluginId", _selectedPlugin?.Id),
            ("PluginId", plugin?.Id),
            ("Source", _marketSourceDisplay),
            ("IsRefreshing", _isRefreshing),
            ("IsInstalling", _isInstalling),
            ("IsDisposed", _isDisposed));
    }

    private static string ClassifyException(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => "Canceled",
            TimeoutException => "Timeout",
            HttpRequestException => "Network",
            IOException => "IO",
            _ => "Unexpected"
        };
    }

    private static string DescribeException(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => "The request timed out or was canceled.",
            TimeoutException => "The request timed out.",
            HttpRequestException => ex.Message,
            _ => ex.Message
        };
    }

    private string GetReadmeContent(AirAppMarketPluginEntry plugin)
    {
        if (_readmeContents.TryGetValue(plugin.Id, out var readme))
        {
            return readme;
        }

        if (_readmeErrors.TryGetValue(plugin.Id, out var error))
        {
            return F(
                "market.detail.readme_error_format",
                "README could not be loaded: {0}",
                error);
        }

        if (string.Equals(_loadingReadmePluginId, plugin.Id, StringComparison.OrdinalIgnoreCase))
        {
            return T("market.detail.readme_loading", "Loading README...");
        }

        return plugin.ReleaseNotes;
    }

    private AirAppMarketPluginEntry? ResolveSelectedPlugin(
        string? selectedPluginId,
        IReadOnlyList<AirAppMarketPluginEntry> plugins)
    {
        if (plugins.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedPluginId))
        {
            var existing = plugins.FirstOrDefault(plugin =>
                string.Equals(plugin.Id, selectedPluginId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        return plugins[0];
    }

    private AirAppMarketInstallState ResolveInstallState(
        AirAppMarketPluginEntry plugin,
        out PluginCatalogEntry? installedPlugin)
    {
        if (!_installedPlugins.TryGetValue(plugin.Id, out installedPlugin))
        {
            return AirAppMarketInstallState.NotInstalled;
        }

        return CompareVersions(plugin.Version, installedPlugin.Manifest.Version) > 0
            ? AirAppMarketInstallState.UpdateAvailable
            : AirAppMarketInstallState.Installed;
    }

    private bool IsCompatibleWithHost(AirAppMarketPluginEntry plugin)
    {
        if (_hostVersion is null ||
            !AirAppMarketIndexDocument.TryParseVersion(plugin.MinHostVersion, out var minHostVersion) ||
            minHostVersion is null)
        {
            return true;
        }

        return _hostVersion >= minHostVersion;
    }

    private void SetStatus(string message, IBrush foreground)
    {
        _statusTextBlock.Text = message;
        _statusTextBlock.Foreground = foreground;
    }

    private static int CompareVersions(string? left, string? right)
    {
        if (!AirAppMarketIndexDocument.TryParseVersion(left, out var leftVersion))
        {
            leftVersion = new Version(0, 0, 0);
        }

        if (!AirAppMarketIndexDocument.TryParseVersion(right, out var rightVersion))
        {
            rightVersion = new Version(0, 0, 0);
        }

        return (leftVersion ?? new Version(0, 0, 0)).CompareTo(rightVersion ?? new Version(0, 0, 0));
    }

    private Border CreatePanelShell(double padding)
    {
        return new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(padding)
        };
    }

    private Control CreateEmptyState(string text)
    {
        return new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private Control CreatePluginIcon(AirAppMarketPluginEntry plugin, double size)
    {
        if (!_iconBitmaps.ContainsKey(plugin.Id))
        {
            _ = EnsureIconLoadedAsync(plugin);
        }

        Control iconChild;
        if (_iconBitmaps.TryGetValue(plugin.Id, out var bitmap) && bitmap is not null)
        {
            iconChild = new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            var glyph = string.IsNullOrWhiteSpace(plugin.Name) ? "?" : plugin.Name.Trim()[0].ToString().ToUpperInvariant();
            iconChild = new TextBlock
            {
                Text = glyph,
                FontSize = Math.Max(18, size * 0.32),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(12, size * 0.24)),
            ClipToBounds = true,
            Background = IconSurfaceBrush,
            Child = iconChild
        };
    }

    private WrapPanel CreateChipWrapPanel(params Control[] chips)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        foreach (var chip in chips)
        {
            chip.Margin = new Thickness(0, 0, 8, 8);
            panel.Children.Add(chip);
        }

        return panel;
    }

    private Border CreateStateChip(string text)
    {
        return new Border
        {
            Background = ChipBrush,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12
            }
        };
    }

    private TextBlock CreateSectionTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        };
    }

    private Border CreateInfoCard(string label, string value)
    {
        return new Border
        {
            Width = 190,
            Margin = new Thickness(0, 0, 12, 12),
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        Foreground = MutedBrush
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(value) ? T("market.detail.unknown", "Unknown") : value,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private Control CreateInfoRow(string label, string value)
    {
        return new Border
        {
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        Foreground = MutedBrush
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(value) ? T("market.detail.unknown", "Unknown") : value,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    private static string FormatPackageSize(long packageSizeBytes)
    {
        var size = packageSizeBytes;
        string[] units = ["B", "KB", "MB", "GB"];
        var unitIndex = 0;
        decimal display = size;

        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            display >= 10 || unitIndex == 0 ? "{0:0} {1}" : "{0:0.0} {1}",
            display,
            units[unitIndex]);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp == default)
        {
            return string.Empty;
        }

        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    }

    private string T(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        return _localizationService.GetString(snapshot.LanguageCode, key, fallback);
    }

    private string F(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, T(key, fallback), args);
    }

    private static string StateKey(AirAppMarketInstallState state)
    {
        return state switch
        {
            AirAppMarketInstallState.UpdateAvailable => "market.detail.state.update_available",
            AirAppMarketInstallState.Installed => "market.detail.state.installed",
            _ => "market.detail.state.not_installed"
        };
    }

    private static string StateFallback(AirAppMarketInstallState state)
    {
        return state switch
        {
            AirAppMarketInstallState.UpdateAvailable => "Update available",
            AirAppMarketInstallState.Installed => "Installed",
            _ => "Not installed"
        };
    }

    private static string ButtonKey(AirAppMarketInstallState state)
    {
        return state switch
        {
            AirAppMarketInstallState.UpdateAvailable => "market.button.update",
            AirAppMarketInstallState.Installed => "market.button.installed",
            _ => "market.button.install"
        };
    }

    private static string ButtonFallback(AirAppMarketInstallState state)
    {
        return state switch
        {
            AirAppMarketInstallState.UpdateAvailable => "Update",
            AirAppMarketInstallState.Installed => "Installed",
            _ => "Install"
        };
    }
}
