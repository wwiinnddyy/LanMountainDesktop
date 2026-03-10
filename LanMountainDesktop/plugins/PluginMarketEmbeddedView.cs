using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

internal sealed class PluginMarketEmbeddedView : UserControl, IDisposable
{
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.Parse("#14000000"));
    private static readonly IBrush SelectedSurfaceBrush = new SolidColorBrush(Color.Parse("#1F0EA5E9"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#FF0F766E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FF9A6700"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FFC42B1C"));

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly PluginRuntimeService _runtime;
    private readonly AirAppMarketIndexService _indexService;
    private readonly AirAppMarketInstallService _installService;
    private readonly AirAppMarketReadmeService _readmeService;
    private readonly Version? _hostVersion;

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
    private string _marketSourceDisplay = AirAppMarketDefaults.DefaultIndexUrl;
    private string? _loadingReadmePluginId;
    private bool _isRefreshing;
    private bool _isInstalling;
    private bool _hasLoadedOnce;

    public PluginMarketEmbeddedView(PluginRuntimeService runtime)
    {
        _runtime = runtime;
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "AirAppMarket");
        _indexService = new AirAppMarketIndexService(new AirAppMarketCacheService(dataDirectory));
        _installService = new AirAppMarketInstallService(runtime, dataDirectory);
        _readmeService = new AirAppMarketReadmeService();
        _hostVersion = typeof(App).Assembly.GetName().Version;

        _searchTextBox = new TextBox
        {
            MinWidth = 240,
            Watermark = T("market.toolbar.search_placeholder", "搜索插件")
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
            Content = T("market.toolbar.refresh", "刷新"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _refreshButton.Click += OnRefreshClick;

        _statusTextBlock = new TextBlock
        {
            Text = T("market.status.loading", "正在加载官方插件市场…"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = WarningBrush
        };

        _pluginListHost = new StackPanel
        {
            Spacing = 10
        };

        _detailBorder = CreatePanelShell();

        Content = BuildLayout();
        AttachedToVisualTree += async (_, _) =>
        {
            if (_hasLoadedOnce)
            {
                return;
            }

            _hasLoadedOnce = true;
            await RefreshAsync();
        };
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
        _readmeService.Dispose();
        _installService.Dispose();
        _indexService.Dispose();
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };

        toolbar.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        _searchTextBox,
                        _refreshButton
                    }
                },
                _statusTextBlock
            }
        });

        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("360,*"),
            ColumnSpacing = 16
        };

        var listShell = CreatePanelShell();
        listShell.Child = new ScrollViewer
        {
            Content = _pluginListHost
        };

        contentGrid.Children.Add(listShell);
        contentGrid.Children.Add(_detailBorder);
        Grid.SetColumn(_detailBorder, 1);

        root.Children.Add(toolbar);
        root.Children.Add(contentGrid);
        Grid.SetRow(contentGrid, 1);

        return root;
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        _refreshButton.IsEnabled = false;
        SetStatus(T("market.status.loading", "正在加载官方插件市场…"), WarningBrush);

        try
        {
            RefreshInstalledSnapshot();

            var result = await _indexService.LoadAsync();
            if (!result.Success || result.Document is null)
            {
                _document = null;
                _selectedPlugin = null;
                SetStatus(
                    F("market.status.load_failed_format", "加载插件市场失败：{0}", result.ErrorMessage ?? T("market.detail.unknown", "未知错误")),
                    ErrorBrush);
                RebuildSurface();
                return;
            }

            _document = result.Document;
            _marketSourceDisplay = result.SourceLocation ?? AirAppMarketDefaults.DefaultIndexUrl;
            _selectedPlugin = ResolveSelectedPlugin(_selectedPlugin?.Id, result.Document.Plugins);

            var statusMessage = result.Source == AirAppMarketLoadSource.Cache
                ? F(
                    "market.status.loaded_cache_format",
                    "官方源不可用，已从缓存加载 {0} 个插件。原因：{1}",
                    result.Document.Plugins.Count,
                    result.WarningMessage ?? T("market.detail.unknown", "未知错误"))
                : F(
                    "market.status.loaded_network_format",
                    "已从官方源加载 {0} 个插件。",
                    result.Document.Plugins.Count);

            SetStatus(statusMessage, result.Source == AirAppMarketLoadSource.Cache ? WarningBrush : SuccessBrush);
            RebuildSurface();
            await EnsureReadmeLoadedAsync(_selectedPlugin);
        }
        finally
        {
            _isRefreshing = false;
            _refreshButton.IsEnabled = true;
        }
    }

    private void RebuildSurface()
    {
        var filteredPlugins = GetFilteredPlugins();
        if (filteredPlugins.Count > 0)
        {
            _selectedPlugin = ResolveSelectedPlugin(_selectedPlugin?.Id, filteredPlugins);
        }
        else
        {
            _selectedPlugin = null;
        }

        BuildPluginList(filteredPlugins);
        BuildDetailPanel();
        _ = EnsureReadmeLoadedAsync(_selectedPlugin);
    }

    private List<AirAppMarketPluginEntry> GetFilteredPlugins()
    {
        if (_document is null)
        {
            return [];
        }

        var query = (_searchTextBox.Text ?? string.Empty).Trim();
        var source = _document.Plugins;
        if (string.IsNullOrWhiteSpace(query))
        {
            return source.ToList();
        }

        return source
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
            _pluginListHost.Children.Add(CreateEmptyState(T("market.list.empty", "插件市场尚未加载。")));
            return;
        }

        if (plugins.Count == 0)
        {
            _pluginListHost.Children.Add(CreateEmptyState(T("market.list.no_results", "没有匹配的插件。")));
            return;
        }

        foreach (var plugin in plugins)
        {
            _pluginListHost.Children.Add(CreatePluginCard(plugin));
        }
    }

    private Control CreatePluginCard(AirAppMarketPluginEntry plugin)
    {
        var installState = ResolveInstallState(plugin, out var installedPlugin);
        var isSelected = string.Equals(_selectedPlugin?.Id, plugin.Id, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new Border
            {
                Background = isSelected ? SelectedSurfaceBrush : SurfaceBrush,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                            ColumnSpacing = 12,
                            Children =
                            {
                                CreateMonogramIcon(plugin.Name, 42),
                                new StackPanel
                                {
                                    Spacing = 4,
                                    Children =
                                    {
                                        new TextBlock
                                        {
                                            Text = plugin.Name,
                                            FontSize = 16,
                                            FontWeight = FontWeight.SemiBold,
                                            TextWrapping = TextWrapping.Wrap
                                        },
                                        new TextBlock
                                        {
                                            Text = F("market.card.subtitle_format", "{0} · v{1}", plugin.Author, plugin.Version),
                                            Foreground = Brushes.Gray,
                                            TextWrapping = TextWrapping.Wrap
                                        }
                                    }
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = plugin.Description,
                            TextWrapping = TextWrapping.Wrap,
                            MaxHeight = 56
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children =
                            {
                                CreateStateChip(T(StateKey(installState), StateFallback(installState))),
                                CreateStateChip(installedPlugin?.IsLoaded == true
                                    ? T("market.card.loaded", "已加载")
                                    : T("market.card.pending_restart", "需重启")),
                                new TextBlock
                                {
                                    Text = string.Join("  ", plugin.Tags.Take(3)),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Foreground = Brushes.Gray
                                }
                            }
                        }
                    }
                }
            }
        };

        button.Click += async (_, _) =>
        {
            _selectedPlugin = plugin;
            RebuildSurface();
            await EnsureReadmeLoadedAsync(plugin);
        };

        return button;
    }

    private void BuildDetailPanel()
    {
        if (_selectedPlugin is null)
        {
            _detailBorder.Child = CreateEmptyState(T("market.detail.placeholder", "从左侧选择一个插件以查看详情。"));
            return;
        }

        var plugin = _selectedPlugin;
        var installState = ResolveInstallState(plugin, out var installedPlugin);
        var isCompatible = IsCompatibleWithHost(plugin);
        var installButton = new Button
        {
            Content = _isInstalling
                ? T("market.button.installing", "安装中…")
                : T(ButtonKey(installState), ButtonFallback(installState)),
            IsEnabled = !_isInstalling && isCompatible && installState != AirAppMarketInstallState.Installed,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 120
        };
        installButton.Click += async (_, _) => await InstallSelectedPluginAsync(plugin);

        var detailPanel = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 14,
                    Children =
                    {
                        CreateMonogramIcon(plugin.Name, 64),
                        new StackPanel
                        {
                            Spacing = 4,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = plugin.Name,
                                    FontSize = 24,
                                    FontWeight = FontWeight.SemiBold,
                                    TextWrapping = TextWrapping.Wrap
                                },
                                new TextBlock
                                {
                                    Text = plugin.Description,
                                    TextWrapping = TextWrapping.Wrap
                                }
                            }
                        }
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        CreateStateChip(T(StateKey(installState), StateFallback(installState))),
                        CreateStateChip(plugin.GetVersionSummary()),
                        CreateStateChip(string.Join(", ", plugin.Tags))
                    }
                },
                installButton,
                CreateInfoRow(T("market.detail.author", "作者"), plugin.Author),
                CreateInfoRow(T("market.detail.version", "版本"), plugin.Version),
                CreateInfoRow(T("market.detail.api_version", "API 版本"), plugin.ApiVersion),
                CreateInfoRow(T("market.detail.min_host_version", "最低宿主版本"), plugin.MinHostVersion),
                CreateInfoRow(T("market.detail.installed_version", "当前已安装版本"), installedPlugin?.Manifest.Version ?? T("market.detail.not_installed", "未安装")),
                CreateInfoRow(T("market.detail.market_source", "市场源"), _marketSourceDisplay),
                CreateInfoRow(T("market.detail.project", "Project"), plugin.ProjectUrl),
                CreateInfoRow(T("market.detail.homepage", "主页"), plugin.HomepageUrl),
                CreateInfoRow(T("market.detail.repository", "仓库"), plugin.RepositoryUrl),
                new TextBlock
                {
                    Text = T("market.detail.readme", "README"),
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold
                },
                new Border
                {
                    Background = SurfaceBrush,
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14),
                    Child = new TextBlock
                    {
                        Text = GetReadmeContent(plugin),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };

        if (!isCompatible)
        {
            detailPanel.Children.Insert(
                3,
                new TextBlock
                {
                    Text = F(
                        "market.status.host_incompatible_format",
                        "当前宿主版本过低，至少需要 {0}。",
                        plugin.MinHostVersion),
                    Foreground = ErrorBrush,
                    TextWrapping = TextWrapping.Wrap
                });
        }

        _detailBorder.Child = new ScrollViewer
        {
            Content = detailPanel
        };
    }

    private async Task InstallSelectedPluginAsync(AirAppMarketPluginEntry plugin)
    {
        if (_isInstalling)
        {
            return;
        }

        _isInstalling = true;
        BuildDetailPanel();
        SetStatus(
            F("market.status.installing_format", "正在下载并暂存插件“{0}”…", plugin.Name),
            WarningBrush);

        try
        {
            var result = await _installService.InstallAsync(plugin);
            if (!result.Success || result.Manifest is null)
            {
                SetStatus(
                    F(
                        "market.status.install_failed_format",
                        "安装插件失败：{0}",
                        result.ErrorMessage ?? T("market.detail.unknown", "未知错误")),
                    ErrorBrush);
                return;
            }

            RefreshInstalledSnapshot();
            SetStatus(
                F(
                    "market.status.install_success_format",
                    "插件“{0}”已暂存完成，重启应用后生效。",
                    result.Manifest.Name),
                SuccessBrush);
            RebuildSurface();
        }
        finally
        {
            _isInstalling = false;
            BuildDetailPanel();
        }
    }

    private async Task EnsureReadmeLoadedAsync(AirAppMarketPluginEntry? plugin)
    {
        if (plugin is null ||
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
            var readme = await _readmeService.LoadAsync(plugin);
            _readmeContents[plugin.Id] = string.IsNullOrWhiteSpace(readme)
                ? T("market.detail.readme_empty", "README is empty.")
                : readme.Trim();
        }
        catch (Exception ex)
        {
            _readmeErrors[plugin.Id] = ex.Message;
        }
        finally
        {
            _loadingReadmePluginId = null;
            if (string.Equals(_selectedPlugin?.Id, plugin.Id, StringComparison.OrdinalIgnoreCase))
            {
                BuildDetailPanel();
            }
        }
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

    private Border CreatePanelShell()
    {
        return new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16)
        };
    }

    private Control CreateEmptyState(string text)
    {
        return new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private Border CreateMonogramIcon(string text, double size)
    {
        var glyph = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[0].ToString().ToUpperInvariant();
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(Color.Parse("#FF0EA5E9")),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = Math.Max(16, size * 0.36),
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private Border CreateStateChip(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#22000000")),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12
            }
        };
    }

    private Control CreateInfoRow(string label, string value)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = Brushes.Gray
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? T("market.detail.unknown", "未知") : value,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
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
            AirAppMarketInstallState.UpdateAvailable => "可更新",
            AirAppMarketInstallState.Installed => "已安装",
            _ => "未安装"
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
            AirAppMarketInstallState.UpdateAvailable => "更新",
            AirAppMarketInstallState.Installed => "已安装",
            _ => "安装"
        };
    }
}
