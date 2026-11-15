using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.ViewModels;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace LanMountainDesktop.Views;

/// <summary>
/// 用户拍板（B 方案）的「彻底重做版」设置窗口：视觉来自样张（侧栏导航 + 大标题 +
/// 卡片式容器 + 底部操作栏），功能在 §11.3 补齐。
///
/// 设计取舍：样张里的「外观」7 张卡片是概念稿，控件未绑定任何 ViewModel，且玻璃拟态 /
/// 弹簧动效 / 圆角风格等并不与真实设置项一一对应。真正可靠、经过 1349 项测试验证的功能载体
/// 是 SettingsPageRegistry 里那 21 个真实设置页（各自带 VM 绑定、即时保存、重启 / 抽屉行为）。
/// 因此本窗口保留样张外壳并把它数据驱动化，内容区通过 <see cref="ContentFrame"/> 承载真实页面，
/// 而不是把 21 页推翻重画（那会臆造绑定、丢功能，与「补全功能」目标相反）。
///
/// 旧 <see cref="SettingsWindow"/> 完整保留作功能参考与回退路径。
/// </summary>
public partial class ArcSettingsWindow : Window, ISettingsPageHostContext
{
    public static AutoCompleteFilterPredicate<object?> SettingsSearchFilter => SettingsSearchService.Filter;

    private readonly ISettingsPageRegistry _pageRegistry;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IAppLogoService _appLogoService = HostAppLogoProvider.GetOrCreate();
    private readonly SettingsSearchService _searchService = new();
    private readonly Dictionary<string, Control> _cachedPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _navItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _navigationBackStack = new();
    private bool _isRestartPromptVisible;
    private bool _isHandlingSearchSelection;

    /// <summary>
    /// 无参构造：供 XAML 预览 / headless 渲染测试独立构造。用静态宿主提供者自建一份
    /// 「仅内置页」的注册表，使窗口脱离 SettingsWindowService 也能真实导航。
    /// </summary>
    public ArcSettingsWindow()
        : this(
            new SettingsWindowViewModel().Initialize(),
            CreateStandaloneRegistry(),
            new HostApplicationLifecycleService(),
            HostSettingsFacadeProvider.GetOrCreate())
    {
    }

    /// <summary>生产入口：由 SettingsWindowService 注入完整服务图（含动态 AirApp 页）。</summary>
    public ArcSettingsWindow(
        SettingsWindowViewModel viewModel,
        ISettingsPageRegistry pageRegistry,
        IHostApplicationLifecycle hostApplicationLifecycle,
        ISettingsFacadeService settingsFacade)
    {
        ViewModel = viewModel;
        _pageRegistry = pageRegistry;
        _hostApplicationLifecycle = hostApplicationLifecycle;
        _settingsFacade = settingsFacade;
        DataContext = ViewModel;

        InitializeComponent();

        Icon = _appLogoService.CreateWindowIcon();

        Opened += OnOpened;
        Closed += OnClosed;
        PendingRestartStateService.StateChanged += OnPendingRestartStateChanged;

        ReloadPages(null);
    }

    public SettingsWindowViewModel ViewModel { get; }

    private static ISettingsPageRegistry CreateStandaloneRegistry()
    {
        var registry = new SettingsPageRegistry(
            HostSettingsFacadeProvider.GetOrCreate(),
            new HostApplicationLifecycleService(),
            new LocalizationService(),
            () => null);
        registry.Rebuild();
        return registry;
    }

    // ---------------------------------------------------------------------
    // IFunctionalSettingsWindow 行为面（由 SettingsWindowService 调度）
    // ---------------------------------------------------------------------

    public void ReloadPages(string? pageId)
    {
        ViewModel.Pages.Clear();
        foreach (var page in _pageRegistry.GetPages().Where(static page => !page.HideDefault))
        {
            ViewModel.Pages.Add(page);
        }

        DropCachedPages();
        _navigationBackStack.Clear();
        ViewModel.CanGoBack = false;
        CloseDrawer();
        RebuildNavigationItems();
        NavigateTo(pageId ?? ViewModel.Pages.FirstOrDefault()?.PageId, addHistory: false, source: "reload");
        RebuildSearchIndex();
    }

    public void RebuildAndNavigateToDevPage()
    {
        _pageRegistry.Rebuild();
        ReloadPages("dev");
    }

    public void RefreshShellText()
    {
        SyncTitleText();
        SyncPendingRestartState();
    }

    // ---------------------------------------------------------------------
    // ISettingsPageHostContext
    // ---------------------------------------------------------------------

    public void OpenDrawer(Control content, string? title = null)
    {
        var wasOpen = ViewModel.IsDrawerOpen;
        var previousTitle = ViewModel.DrawerTitle;
        DrawerContentHost.Content = content;
        ViewModel.DrawerTitle = title ?? ViewModel.DrawerFallbackTitle;
        ViewModel.IsDrawerOpen = true;
        SyncTitleText();
        if (!wasOpen || !string.Equals(previousTitle, ViewModel.DrawerTitle, StringComparison.Ordinal))
        {
            TelemetryServices.Usage?.TrackSettingsDrawerOpened(ViewModel.CurrentPageId, ViewModel.DrawerTitle);
        }
    }

    public void CloseDrawer()
    {
        var wasOpen = ViewModel.IsDrawerOpen || DrawerContentHost?.Content is not null;
        var currentPageId = ViewModel.CurrentPageId;
        var drawerTitle = ViewModel.DrawerTitle;

        if (DrawerContentHost is not null)
        {
            DrawerContentHost.Content = null;
        }

        ViewModel.IsDrawerOpen = false;
        ViewModel.DrawerTitle = null;
        if (wasOpen)
        {
            TelemetryServices.Usage?.TrackSettingsDrawerClosed(currentPageId, drawerTitle);
        }
    }

    public void RequestRestart(string? reason = null)
    {
        ViewModel.RestartMessage = string.IsNullOrWhiteSpace(reason)
            ? ViewModel.GetDefaultRestartMessage()
            : reason;
        ViewModel.IsRestartRequested = true;
        PendingRestartStateService.SetPending(PendingRestartStateService.SettingsWindowReason, true);
        ShowRestartPrompt();
    }

    // ---------------------------------------------------------------------
    // 导航
    // ---------------------------------------------------------------------

    private void RebuildNavigationItems()
    {
        NavItemsHost.Children.Clear();
        _navItems.Clear();

        // 与样张「系统 / 内容 / 其他」三组对应；注册表按 Category 排序，标签变化即起新组。
        string? previousLabel = null;
        foreach (var page in ViewModel.Pages)
        {
            var label = CategoryLabel(page.Category);
            if (!string.Equals(previousLabel, label, StringComparison.Ordinal))
            {
                var header = new TextBlock { Text = label };
                header.Classes.Add("nav-section");
                NavItemsHost.Children.Add(header);
                previousLabel = label;
            }

            var border = new Border
            {
                Background = Brushes.Transparent,
                Tag = page.PageId,
            };
            border.Classes.Add("nav-item");

            var panel = new StackPanel();
            panel.Children.Add(CreateSettingsIcon(MapIcon(page.IconKey)));
            panel.Children.Add(new TextBlock { Text = page.Title });
            border.Child = panel;
            border.PointerPressed += OnNavItemPointerPressed;

            NavItemsHost.Children.Add(border);
            _navItems[page.PageId] = border;
        }
    }

    /// <summary>把 selected 类贴到当前页对应的导航项上（样张里选中项的高亮反馈）。</summary>
    private void TrySelectNavigationItem(string pageId)
    {
        foreach (var (id, border) in _navItems)
        {
            var isSelected = string.Equals(id, pageId, StringComparison.OrdinalIgnoreCase);
            if (isSelected && !border.Classes.Contains("selected"))
            {
                border.Classes.Add("selected");
            }
            else if (!isSelected && border.Classes.Contains("selected"))
            {
                border.Classes.Remove("selected");
            }
        }
    }

    private static string CategoryLabel(AirAppSettingsPageCategory category) => category switch
    {
        AirAppSettingsPageCategory.General or AirAppSettingsPageCategory.Appearance => "系统",
        AirAppSettingsPageCategory.Components or AirAppSettingsPageCategory.AirApps
            or AirAppSettingsPageCategory.AirAppCatalog => "内容",
        AirAppSettingsPageCategory.About or AirAppSettingsPageCategory.Dev => "其他",
        _ => "其他",
    };

    private void OnNavItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = e;
        if (sender is Border { Tag: string pageId })
        {
            NavigateTo(pageId, addHistory: true, source: "navigation");
        }
    }

    private void NavigateTo(
        string? pageId,
        bool addHistory,
        string source,
        SettingsSearchResult? searchResult = null)
    {
        var previousPageId = ViewModel.CurrentPageId;
        var descriptor = ResolveDescriptor(pageId);
        if (descriptor is null)
        {
            return;
        }

        if (string.Equals(previousPageId, descriptor.PageId, StringComparison.OrdinalIgnoreCase))
        {
            if (searchResult is not null)
            {
                HighlightSearchResult(searchResult);
            }

            return;
        }

        if (addHistory && !string.IsNullOrWhiteSpace(previousPageId))
        {
            _navigationBackStack.Push(previousPageId);
        }

        var page = GetOrCreatePage(descriptor);
        if (page is AirAppSettingsPageBase settingsPage)
        {
            settingsPage.InitializeHostContext(this);
            settingsPage.NavigationUri = new Uri($"lmd://settings/{descriptor.PageId}", UriKind.Absolute);
            settingsPage.OnNavigatedTo(null);
        }

        ContentFrame.Content = page;

        ViewModel.CurrentPageTitle = descriptor.Title;
        ViewModel.CurrentPageDescription = descriptor.Description;
        ViewModel.CurrentPageId = descriptor.PageId;
        ViewModel.IsPageTitleVisible = !descriptor.HidePageTitle;
        ViewModel.CanGoBack = _navigationBackStack.Count > 0;
        CloseDrawer();
        TrySelectNavigationItem(descriptor.PageId);
        SyncTitleText();

        if (searchResult is not null)
        {
            HighlightSearchResult(searchResult);
        }

        if (!string.Equals(previousPageId, descriptor.PageId, StringComparison.OrdinalIgnoreCase))
        {
            TelemetryServices.Usage?.TrackSettingsNavigation(previousPageId, descriptor.PageId, source);
        }
    }

    private SettingsPageDescriptor? ResolveDescriptor(string? pageId)
    {
        if (!string.IsNullOrWhiteSpace(pageId) &&
            _pageRegistry.TryGetPage(pageId, out var descriptor) &&
            descriptor is not null)
        {
            return descriptor;
        }

        return ViewModel.Pages.FirstOrDefault();
    }

    /// <summary>整批丢弃缓存页时必须释放订阅了 Settings.Changed 的页 VM（见旧窗口同款修复）。</summary>
    private void DropCachedPages()
    {
        foreach (var page in _cachedPages.Values)
        {
            if (page.DataContext is IDisposable viewModel)
            {
                viewModel.Dispose();
            }
        }

        _cachedPages.Clear();
    }

    private Control GetOrCreatePage(SettingsPageDescriptor descriptor)
    {
        if (_cachedPages.TryGetValue(descriptor.PageId, out var page))
        {
            return page;
        }

        page = descriptor.CreatePage(this);
        if (page is AirAppSettingsPageBase settingsPage)
        {
            settingsPage.InitializeHostContext(this);
        }

        _cachedPages[descriptor.PageId] = page;
        _searchService.IndexPage(descriptor, page);
        SyncSearchResults();
        return page;
    }

    private void RebuildSearchIndex()
    {
        // 页级条目覆盖全部页面（无需实例化控件）；细粒度行条目在首次访问该页时由
        // GetOrCreatePage → IndexPage 补入，避免冷启动一次性构造全部 21 页。
        _searchService.RebuildPageEntries(ViewModel.Pages);
        SyncSearchResults();
    }

    private void SyncSearchResults()
    {
        ViewModel.SearchResults.Clear();
        foreach (var result in _searchService.Entries)
        {
            ViewModel.SearchResults.Add(result);
        }
    }

    private void HighlightSearchResult(SettingsSearchResult result)
    {
        if (result.TargetControl is not Control target)
        {
            return;
        }

        try
        {
            // 把命中的设置行滚到可视区；控件若尚未挂到可视化树会自行抛错，定位是增强行为，
            // 捕获后不影响导航本身。
            target.BringIntoView();
        }
        catch (Exception)
        {
            // 忽略：跨页结果定位失败不应阻断页面切换。
        }
    }

    private void SyncTitleText()
    {
        Title = string.IsNullOrWhiteSpace(ViewModel.Title) ? "设置" : ViewModel.Title;
    }

    // ---------------------------------------------------------------------
    // 搜索
    // ---------------------------------------------------------------------

    private void OnSearchBoxKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var selected = ViewModel.SelectedSearchResult;
        if (selected is null && SettingsSearchBox is not null)
        {
            selected = _searchService.Search(SettingsSearchBox.Text, maxResults: 1).FirstOrDefault();
        }

        NavigateToSearchResult(selected);
    }

    private void OnSearchBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        if (_isHandlingSearchSelection || e.AddedItems.Count == 0)
        {
            return;
        }

        NavigateToSearchResult(e.AddedItems[0] as SettingsSearchResult);
    }

    private void NavigateToSearchResult(SettingsSearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        _isHandlingSearchSelection = true;
        try
        {
            NavigateTo(result.PageId, addHistory: true, source: "search", searchResult: result);
            ViewModel.SelectedSearchResult = null;
        }
        finally
        {
            _isHandlingSearchSelection = false;
        }
    }

    // ---------------------------------------------------------------------
    // 返回 / 重启 / 抽屉 / 底部操作栏
    // ---------------------------------------------------------------------

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        while (_navigationBackStack.Count > 0)
        {
            var pageId = _navigationBackStack.Pop();
            if (ResolveDescriptor(pageId) is not null)
            {
                NavigateTo(pageId, addHistory: false, source: "back");
                return;
            }
        }

        ViewModel.CanGoBack = false;
    }

    private void OnRestartNowClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowRestartPrompt();
    }

    private void OnCloseDrawerClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseDrawer();
    }

    private void OnDrawerOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseDrawer();
    }

    /// <summary>设置项在变更时即由各页 VM 调用 SaveSnapshot 持久化，无待提交事务；这里确认并关闭。</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    /// <summary>「重置为默认」：确认后把应用级设置恢复为出厂快照，再重建页面刷新所有 VM。</summary>
    private async void OnResetDefaultsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        var confirmed = await Dialog.ShowAsync(
            this,
            "重置为默认",
            "这会将应用级设置（主题、壁纸、语言、开发者模式等）恢复为默认值。此操作不可撤销。",
            description: null,
            new DialogAction("取消", false),
            new DialogAction("重置", true));

        if (confirmed is not true)
        {
            return;
        }

        _settingsFacade.Settings.SaveSnapshot(AirAppSettingsScope.App, new AppSettingsSnapshot());
        _pageRegistry.Rebuild();
        ReloadPages(null);
    }

    private void OnPendingRestartStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SyncPendingRestartState();
            return;
        }

        Dispatcher.UIThread.Post(SyncPendingRestartState, DispatcherPriority.Background);
    }

    private void SyncPendingRestartState()
    {
        if (!PendingRestartStateService.HasPendingRestart && !ViewModel.IsRestartRequested)
        {
            return;
        }

        if (PendingRestartStateService.HasPendingRestart && string.IsNullOrWhiteSpace(ViewModel.RestartMessage))
        {
            ViewModel.RestartMessage = ViewModel.GetDefaultRestartMessage();
        }

        ViewModel.IsRestartRequested = ViewModel.IsRestartRequested || PendingRestartStateService.HasPendingRestart;
    }

    private void ShowRestartPrompt()
    {
        void ShowPrompt()
        {
            UiExceptionGuard.FireAndForgetGuarded(
                ShowRestartPromptCoreAsync,
                "ArcSettingsWindow.ShowRestartPrompt");
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowPrompt();
            return;
        }

        Dispatcher.UIThread.Post(ShowPrompt, DispatcherPriority.Send);
    }

    private async Task ShowRestartPromptCoreAsync()
    {
        if (_isRestartPromptVisible)
        {
            return;
        }

        _isRestartPromptVisible = true;
        try
        {
            var result = await Dialog.ShowAsync(this,
                ViewModel.RestartDialogTitle,
                ViewModel.RestartMessage,
                description: null,
                new DialogAction(ViewModel.RestartDialogCloseText, false),
                new DialogAction(ViewModel.RestartDialogPrimaryText, true, ButtonVariant.Primary));

            if (result is true)
            {
                _hostApplicationLifecycle.TryRestart(new HostApplicationLifecycleRequest(
                    Source: "ArcSettingsWindow",
                    Reason: "User accepted restart from settings window."));
            }
        }
        finally
        {
            _isRestartPromptVisible = false;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SyncPendingRestartState();
        SyncTitleText();
        TelemetryServices.Usage?.TrackSettingsWindowOpened("ArcSettingsWindow.OnOpened", ViewModel.CurrentPageId);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        PendingRestartStateService.StateChanged -= OnPendingRestartStateChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;
        if (DrawerContentHost is not null)
        {
            DrawerContentHost.Content = null;
        }

        DropCachedPages();
    }

    // ---------------------------------------------------------------------
    // 图标（沿用旧 SettingsWindow 的 IconKey → Lucide 映射，避免臆造图标名）
    // ---------------------------------------------------------------------

    private static LucideIconKind MapIcon(string iconKey)
    {
        return iconKey?.Trim() switch
        {
            "DesignIdeas" => LucideIconKind.Palette,
            "Image" => LucideIconKind.Image,
            "WeatherMoon" => LucideIconKind.Moon,
            "Apps" => LucideIconKind.LayoutGrid,
            "AppFolder" => LucideIconKind.Folder,
            "AppsListDetail" => LucideIconKind.List,
            "MatchAppLayout" => LucideIconKind.LayoutTemplate,
            "Widget" => LucideIconKind.LayoutGrid,
            "SwitchApps" => LucideIconKind.RefreshCw,
            "GridDots" => LucideIconKind.LayoutGrid,
            "PuzzlePiece" => LucideIconKind.Puzzle,
            "ShoppingBag" => LucideIconKind.ShoppingBag,
            "Shield" => LucideIconKind.Shield,
            "Info" => LucideIconKind.Info,
            "ArrowSync" => LucideIconKind.RefreshCw,
            "Hourglass" => LucideIconKind.Hourglass,
            "Alert" => LucideIconKind.TriangleAlert,
            "Bell" => LucideIconKind.Bell,
            "DeveloperBoard" => LucideIconKind.Cpu,
            "FolderLink" => LucideIconKind.FolderOpen,
            "WindowConsole" => LucideIconKind.SquareCode,
            "HardDrive" => LucideIconKind.HardDrive,
            _ => LucideIconKind.Settings
        };
    }

    private static LucideIcon CreateSettingsIcon(LucideIconKind kind)
    {
        var icon = new LucideIcon
        {
            Kind = kind,
            Width = 16,
            Height = 16
        };
        icon.Classes.Add("icon-sm");
        return icon;
    }
}
