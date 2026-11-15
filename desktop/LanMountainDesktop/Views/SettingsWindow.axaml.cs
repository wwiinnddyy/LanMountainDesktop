using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts;
using LanMountainDesktop.ViewModels;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
// TKCode.Arc 与 Avalonia.Controls 存在同名控件（Button 等）；本文件的控件引用全部是 Avalonia 语义。
using Button = Avalonia.Controls.Button;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow : FAAppWindow, ISettingsPageHostContext
{
    public static AutoCompleteFilterPredicate<object?> SettingsSearchFilter => SettingsSearchService.Filter;

    private const double BaseSettingsContainerWidth = 960d;
    private const double MinSettingsContentWidth = 320d;
    private const double MinSettingsContainerWidth = 840d;
    private const double MaxSettingsContainerWidth = 1160d;
    private const double BaseDrawerWidth = 296d;
    private const double BasePaneOpenLength = 283d;
    private const double MinPaneOpenLength = 260d;
    private const double MaxPaneOpenLength = 288d;
    private const double BaseNarrowThreshold = 800d;
    private const double CustomTitleBarHeight = 48d;

    private readonly ISettingsPageRegistry _pageRegistry;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly IAppLogoService _appLogoService = HostAppLogoProvider.GetOrCreate();
    private readonly SettingsSearchService _searchService = new();
    private readonly Dictionary<string, Control> _cachedPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _navigationBackStack = new();
    private bool _useSystemChrome;
    private bool _isResponsiveRefreshPending;
    private bool _isRestartPromptVisible;
    private bool _isHandlingSearchSelection;
    private Border? _currentSearchHighlight;
    private Action? _searchHighlightCleanup;

    public SettingsWindow()
        : this(
            new SettingsWindowViewModel(),
            EmptySettingsPageRegistry.Instance,
            new HostApplicationLifecycleService())
    {
    }

    public SettingsWindow(
        SettingsWindowViewModel viewModel,
        ISettingsPageRegistry pageRegistry,
        IHostApplicationLifecycle hostApplicationLifecycle,
        bool useSystemChrome = false)
    {
        _useSystemChrome = useSystemChrome;
        ViewModel = viewModel;
        _pageRegistry = pageRegistry;
        _hostApplicationLifecycle = hostApplicationLifecycle;
        DataContext = ViewModel;
        InitializeComponent();
        SetValue(Window.IconProperty, _appLogoService.CreateWindowIcon());
        ApplyChromeMode(useSystemChrome);
        ApplyFluentCornerRadius();

        if (NavSidebar is not null)
        {
            NavSidebar.PropertyChanged += OnSidebarPropertyChanged;
        }

        Opened += OnOpened;
        SizeChanged += OnWindowSizeChanged;
        Closed += OnClosed;
        Loaded += OnLoaded;
        PendingRestartStateService.StateChanged += OnPendingRestartStateChanged;
    }

    public SettingsWindowViewModel ViewModel { get; }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TitleBar.Height = CustomTitleBarHeight;

        // Match the native caption button feedback used by SecRandom MainWindow.
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        ApplyChromeMode(_useSystemChrome);
        SyncPendingRestartState();
        SyncTitleText();
        UpdateChromeMetrics();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
    }

    public void ReloadPages(string? pageId)
    {
        ViewModel.Pages.Clear();
        foreach (var page in _pageRegistry.GetPages().Where(page => !page.HideDefault))
        {
            ViewModel.Pages.Add(page);
        }

        DropCachedPages();
        _navigationBackStack.Clear();
        ViewModel.CanGoBack = false;
        CloseDrawer();
        RebuildNavigationItems();
        NavigateTo(pageId ?? ViewModel.Pages.FirstOrDefault()?.PageId, addHistory: false, source: "reload");
        RebuildSearchIndex(scanBuiltInPages: true);
    }

    public void RebuildAndNavigateToDevPage()
    {
        _pageRegistry.Rebuild();
        ReloadPages("dev");
    }

    public void OpenDrawer(Control content, string? title = null)
    {
        if (DrawerContentHost is null)
        {
            return;
        }

        var wasOpen = ViewModel.IsDrawerOpen;
        var previousTitle = ViewModel.DrawerTitle;
        DrawerContentHost.Content = content;
        ViewModel.DrawerTitle = title ?? ViewModel.DrawerFallbackTitle;
        ViewModel.IsDrawerOpen = true;
        SyncTitleText();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
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
        SyncTitleText();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
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

    public void ApplyChromeMode(bool useSystemChrome)
    {
        _useSystemChrome = useSystemChrome || OperatingSystem.IsMacOS();

        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = !_useSystemChrome;
        ExtendClientAreaTitleBarHeightHint = _useSystemChrome ? 0d : CustomTitleBarHeight;
        TitleBar.ExtendsContentIntoTitleBar = !_useSystemChrome;
        TitleBar.Height = CustomTitleBarHeight;

        if (_useSystemChrome)
        {
            if (WindowTitleBarHost is { })
            {
                WindowTitleBarHost.IsVisible = false;
            }
            return;
        }

        if (WindowTitleBarHost is { })
        {
            WindowTitleBarHost.IsVisible = true;
        }
    }

    public void RefreshShellText()
    {
        SyncPendingRestartState();
        SyncTitleText();
    }

    private void RebuildNavigationItems()
    {
        if (NavSidebar is null)
        {
            return;
        }

        NavSidebar.Sections.Clear();

        // 与原 FANavigationView 的分隔语义一致：Category 变化即新组（连续同类共用一组，
        // About/Dev 自然落到尾部各自的组）。
        AirAppSettingsPageCategory? previousCategory = null;
        SidebarSection? currentSection = null;

        foreach (var page in ViewModel.Pages)
        {
            if (currentSection is null || previousCategory != page.Category)
            {
                currentSection = new SidebarSection();
                NavSidebar.Sections.Add(currentSection);
            }

            currentSection.Items.Add(new SidebarItem
            {
                Key = page.PageId,
                Label = page.Title,
                Icon = CreateSettingsIcon(MapIcon(page.IconKey)),
            });

            previousCategory = page.Category;
        }
    }

    private void OnSidebarSelectionChanged(object? sender, SidebarSelectionChangedEventArgs e)
    {
        NavigateTo(NavSidebar?.SelectedItem?.Key, addHistory: true, source: "navigation");
    }

    private void OnSidebarItemInvoked(object? sender, SidebarItemInvokedEventArgs e)
    {
        // 已选中页再点也走 NavigateTo（幂等早退），保持与原 ItemInvoked 一致的行为面。
        NavigateTo(NavSidebar?.SelectedItem?.Key, addHistory: true, source: "navigation");
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

        if (ContentFrame is not null)
        {
            ContentFrame.Content = page;
        }

        ViewModel.CurrentPageTitle = descriptor.Title;
        ViewModel.CurrentPageDescription = descriptor.Description;
        ViewModel.CurrentPageId = descriptor.PageId;
        ViewModel.IsPageTitleVisible = !descriptor.HidePageTitle;
        ViewModel.CanGoBack = _navigationBackStack.Count > 0;
        CloseDrawer();
        TrySelectNavigationItem(descriptor.PageId);
        SyncTitleText();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
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

    /// <summary>
    /// 缓存整批丢弃时必须把页的 ViewModel 释放掉：页是 <c>ActivatorUtilities.CreateInstance</c> 现造的，
    /// 容器不追踪也不负责释放，而 <c>Rebuild()</c> 之后旧页只是从字典里消失——
    /// 那些在构造里订了 <c>Settings.Changed</c> 的页 VM 就永远留在那条事件上（每开一次设置窗口留一份，
    /// 旧页还会继续响应设置变更）。这里只认实现了 <c>IDisposable</c> 的 DataContext，别的页不受影响。
    /// </summary>
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
        return page;
    }

    private void RebuildSearchIndex(bool scanBuiltInPages)
    {
        _searchService.RebuildPageEntries(ViewModel.Pages);

        if (scanBuiltInPages)
        {
            foreach (var descriptor in ViewModel.Pages.Where(static page => page.IsBuiltIn))
            {
                _ = GetOrCreatePage(descriptor);
            }
        }

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

    private void TrySelectNavigationItem(string pageId)
    {
        if (NavSidebar is null)
        {
            return;
        }

        // SelectedKey 双向绑定选中项；设置后触发的 SelectionChanged 会在 NavigateTo 里幂等早退。
        NavSidebar.SelectedKey = pageId;
    }

    private void OnRestartNowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowRestartPrompt();
    }

    private void OnCloseDrawerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseDrawer();
    }

    private void OnBackButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private void OnRestartMenuItemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ShowRestartPrompt();
    }

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

    private void OnPendingRestartStateChanged()
    {
        SyncPendingRestartState();
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
                "SettingsWindow.ShowRestartPrompt");
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
                    Source: "SettingsWindow",
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
        UpdateChromeMetrics();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
        TelemetryServices.Usage?.TrackSettingsWindowOpened("SettingsWindow.OnOpened", ViewModel.CurrentPageId);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateChromeMetrics();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
    }

    private bool TryApplyResponsiveLayout()
    {
        if (SettingsContentGrid is null || DrawerBorder is null)
        {
            return false;
        }

        var width = Bounds.Width > 1 ? Bounds.Width : Math.Max(Width, MinWidth);
        var renderScale = RenderScaling > 0 ? RenderScaling : 1d;
        var contentScale = GetContentScale();

        var horizontalMargin = Math.Clamp(16d * renderScale, 12d, 24d);
        var topMargin = Math.Clamp(2d * renderScale, 0d, 8d);
        var bottomMargin = Math.Clamp(16d * renderScale, 12d, 28d);
        var columnSpacing = Math.Clamp(20d * renderScale, 16d, 28d);
        var edgePadding = Math.Clamp(20d * renderScale, 12d, 28d);
        var drawerWidth = Math.Clamp(BaseDrawerWidth * contentScale, 276d, 380d);
        var narrowThreshold = Math.Clamp(BaseNarrowThreshold * renderScale, 760d, 980d);
        var isNarrow = width < narrowThreshold;
        var paneOpenWidth = ComputePaneOpenLength();
        var containerMaxWidth = ComputeSettingsContainerMaxWidth();

        if (NavSidebar is not null &&
            Math.Abs(NavSidebar.ExpandedWidth - paneOpenWidth) > 0.5d)
        {
            NavSidebar.ExpandedWidth = paneOpenWidth;
        }

        SettingsContentGrid.Margin = new Thickness(horizontalMargin, topMargin, horizontalMargin, bottomMargin);
        DrawerBorder.Width = drawerWidth;

        if (isNarrow)
        {
            SettingsContentGrid.ColumnDefinitions = new ColumnDefinitions("*");
            SettingsContentGrid.ColumnSpacing = 0;
            if (DrawerBorder.IsVisible)
            {
                ViewModel.IsDrawerOpen = false;
            }
        }
        else
        {
            SettingsContentGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            SettingsContentGrid.ColumnSpacing = columnSpacing;
        }

        // Push 模式下内容列与侧栏真实并排，SettingsContentGrid 的 Bounds 即扣除窗格后的可用宽。
        var rootContentWidth = Math.Max(SettingsContentGrid.Bounds.Width, width - horizontalMargin * 2d);
        var contentHostWidth = rootContentWidth - (isNarrow ? 0d : drawerWidth + SettingsContentGrid.ColumnSpacing);
        var availableContentWidth = Math.Max(MinSettingsContentWidth, contentHostWidth - edgePadding * 2d);
        var resolvedContentWidth = Math.Min(containerMaxWidth, availableContentWidth);

        Resources["SettingsContainerMaxWidth"] = containerMaxWidth;

        if (PageTitleTextBlock is not null)
        {
            var narrowTitleThreshold = Math.Clamp(760d * renderScale, 700d, 860d);
            PageTitleTextBlock.Classes.Set("narrow", resolvedContentWidth < narrowTitleThreshold);
        }

        return true;
    }

    private void UpdateResponsiveLayout()
    {
        _ = TryApplyResponsiveLayout();

        // Hide the drawer pane on narrow windows.
    }

    private void HighlightSearchResult(SettingsSearchResult result)
    {
        var target = result.TargetControl;
        if (target is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                ExpandSearchTarget(target);
                target.BringIntoView();
                target.Focus();
                ShowSearchHighlight(target);
            },
            DispatcherPriority.Render);
    }

    private static void ExpandSearchTarget(Control target)
    {
        if (target is FASettingsExpander expander)
        {
            expander.IsExpanded = true;
        }

        foreach (var ancestor in target.GetVisualAncestors().OfType<FASettingsExpander>())
        {
            ancestor.IsExpanded = true;
        }
    }

    private void ShowSearchHighlight(Control target)
    {
        RemoveSearchHighlight();

        if (SearchHighlightOverlay is null || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            return;
        }

        var transform = target.TransformToVisual(SearchHighlightOverlay);
        if (transform is null)
        {
            return;
        }

        var position = transform.Value.Transform(new Point(0, 0));
        var accent = HostAppearanceThemeProvider.GetOrCreate().GetCurrent().AccentColor;
        var highlight = new Border
        {
            Width = target.Bounds.Width,
            Height = target.Bounds.Height,
            Background = new SolidColorBrush(Color.FromArgb(34, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(highlight, position.X);
        Canvas.SetTop(highlight, position.Y);
        SearchHighlightOverlay.Children.Add(highlight);
        _currentSearchHighlight = highlight;

        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            if (_currentSearchHighlight != highlight || SearchHighlightOverlay is null)
            {
                return;
            }

            var nextTransform = target.TransformToVisual(SearchHighlightOverlay);
            if (nextTransform is null)
            {
                return;
            }

            var nextPosition = nextTransform.Value.Transform(new Point(0, 0));
            Canvas.SetLeft(highlight, nextPosition.X);
            Canvas.SetTop(highlight, nextPosition.Y);
            highlight.Width = target.Bounds.Width;
            highlight.Height = target.Bounds.Height;
        }

        target.LayoutUpdated += OnLayoutUpdated;
        _searchHighlightCleanup = () =>
        {
            target.LayoutUpdated -= OnLayoutUpdated;
            SearchHighlightOverlay?.Children.Remove(highlight);
        };

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.4)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_currentSearchHighlight == highlight)
            {
                RemoveSearchHighlight();
            }
        };
        timer.Start();
    }

    private void RemoveSearchHighlight()
    {
        _searchHighlightCleanup?.Invoke();
        _searchHighlightCleanup = null;
        _currentSearchHighlight = null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        RemoveSearchHighlight();
        DropCachedPages();
        PendingRestartStateService.StateChanged -= OnPendingRestartStateChanged;
        if (NavSidebar is not null)
        {
            NavSidebar.PropertyChanged -= OnSidebarPropertyChanged;
        }
        Opened -= OnOpened;
        SizeChanged -= OnWindowSizeChanged;
        TelemetryServices.Usage?.TrackSettingsWindowClosed("SettingsWindow.OnClosed", ViewModel.CurrentPageId);
    }

    /// <summary>
    /// Override inherited corner radius tokens on the settings window root grid
    /// so all child controls use Microsoft Fluent Design System values,
    /// independent of the user's component corner radius preference.
    /// </summary>
    public void ApplyFluentCornerRadius()
    {
        var tokens = AppearanceCornerRadiusTokenFactory.Create(
            GlobalAppearanceSettings.CornerRadiusStyleFluent);
        ApplyFluentCornerRadius(Resources, tokens);
        if (RootGrid is not null)
        {
            ApplyFluentCornerRadius(RootGrid.Resources, tokens);
        }
    }

    private static void ApplyFluentCornerRadius(IResourceDictionary resources)
    {
        var tokens = AppearanceCornerRadiusTokenFactory.Create(
            GlobalAppearanceSettings.CornerRadiusStyleFluent);
        ApplyFluentCornerRadius(resources, tokens);
    }

    private static void ApplyFluentCornerRadius(
        IResourceDictionary resources,
        AppearanceCornerRadiusTokens tokens)
    {
        resources["DesignCornerRadiusMicro"] = tokens.Micro;
        resources["DesignCornerRadiusXs"] = tokens.Xs;
        resources["DesignCornerRadiusSm"] = tokens.Sm;
        resources["DesignCornerRadiusMd"] = tokens.Md;
        resources["DesignCornerRadiusLg"] = tokens.Lg;
        resources["DesignCornerRadiusXl"] = tokens.Xl;
        resources["DesignCornerRadiusIsland"] = tokens.Island;
        resources["DesignCornerRadiusComponent"] = tokens.Component;
    }

    private void OnTitleBarDragZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsInteractiveTitleBarSource(e.Source as Control))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private bool IsInteractiveTitleBarSource(Control? source)
    {
        if (source is null)
        {
            return false;
        }

        IEnumerable<Control> controls = source.GetVisualAncestors().OfType<Control>().Prepend(source);
        foreach (var control in controls)
        {
            if (ReferenceEquals(control, WindowTitleBarHost))
            {
                return false;
            }

            if (control is Button or AutoCompleteBox or TextBox or MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    private void OnSidebarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;

        // 折叠/展开改变窗格宽度（弹簧过渡），内容可用宽随之变化。
        if (e.Property == Sidebar.IsCollapsedProperty)
        {
            RequestResponsiveLayoutRefresh();
        }
    }

    private void RequestResponsiveLayoutRefresh()
    {
        if (_isResponsiveRefreshPending)
        {
            return;
        }

        _isResponsiveRefreshPending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _isResponsiveRefreshPending = false;
                UpdateResponsiveLayout();
            },
            DispatcherPriority.Loaded);
    }

    private void UpdateChromeMetrics()
    {
        if (_useSystemChrome)
        {
            if (WindowTitleBarHost is { })
            {
                WindowTitleBarHost.IsVisible = false;
            }
            return;
        }

        if (WindowTitleBarHost is null ||
            WindowBrandIcon is null ||
            WindowTitleTextBlock is null ||
            RestartNowButton is null ||
            RestartButtonIcon is null ||
            RestartButtonTextBlock is null ||
            DrawerTitleTextBlock is null ||
            NavSidebar is null)
        {
            return;
        }

        var width = Bounds.Width > 1 ? Bounds.Width : Math.Max(Width, MinWidth);
        var height = Bounds.Height > 1 ? Bounds.Height : Math.Max(Height, MinHeight);
        var layoutScale = Math.Clamp(Math.Min(width / 1120d, height / 760d), 0.90, 1.18);

        const double titleBarHeight = CustomTitleBarHeight;
        var titleFontSize = Math.Clamp(12d * layoutScale, 11d, 14d);
        var titleBarIconSize = Math.Clamp(16d * layoutScale, 15d, 20d);
        var drawerTitleFontSize = Math.Clamp(16d * layoutScale, 14d, 20d);
        var chromePadding = Math.Clamp(12d * layoutScale, 10d, 18d);
        var restartSpacing = Math.Clamp(6d * layoutScale, 6d, 10d);

        ExtendClientAreaTitleBarHeightHint = titleBarHeight;

        WindowTitleBarHost.Height = titleBarHeight;

        WindowBrandIcon.Width = titleBarIconSize + 2;
        WindowBrandIcon.Height = titleBarIconSize + 2;

        WindowTitleTextBlock.FontSize = titleFontSize;

        RestartNowButton.Padding = new Thickness(chromePadding * 0.9, Math.Max(6, chromePadding * 0.5));
        if (RestartNowButton.Content is StackPanel restartStack)
        {
            restartStack.Spacing = restartSpacing;
        }

        RestartButtonIcon.Width = titleBarIconSize;
        RestartButtonIcon.Height = titleBarIconSize;
        RestartButtonTextBlock.FontSize = titleFontSize;

        DrawerTitleTextBlock.FontSize = drawerTitleFontSize;
    }

    private double GetContentScale()
    {
        var renderScale = RenderScaling > 0 ? RenderScaling : 1d;
        var titleScale = WindowTitleTextBlock?.FontSize > 0
            ? WindowTitleTextBlock.FontSize / 12d
            : 1d;
        var pageTitleScale = PageTitleTextBlock?.FontSize > 0
            ? PageTitleTextBlock.FontSize / 28d
            : 1d;
        var typographyScale = Math.Max(titleScale, pageTitleScale);

        return Math.Clamp(
            1d + ((renderScale - 1d) * 0.65d) + ((typographyScale - 1d) * 0.35d),
            1d,
            1.18d);
    }

    private double ComputePaneOpenLength()
    {
        return Math.Clamp(BasePaneOpenLength * GetContentScale(), MinPaneOpenLength, MaxPaneOpenLength);
    }

    private double ComputeSettingsContainerMaxWidth()
    {
        return Math.Clamp(
            BaseSettingsContainerWidth * GetContentScale(),
            MinSettingsContainerWidth,
            MaxSettingsContainerWidth);
    }

    private void SyncTitleText()
    {
        Title = ViewModel.Title;
        
        if (_useSystemChrome)
        {
            return;
        }

        if (WindowTitleTextBlock is null ||
            DrawerTitleTextBlock is null)
        {
            return;
        }

        WindowTitleTextBlock.Text = ViewModel.Title;
        DrawerTitleTextBlock.IsVisible = !string.IsNullOrWhiteSpace(ViewModel.DrawerTitle);
    }

    private sealed class EmptySettingsPageRegistry : ISettingsPageRegistry
    {
        public static EmptySettingsPageRegistry Instance { get; } = new();

        public void Rebuild()
        {
        }

        public IReadOnlyList<SettingsPageDescriptor> GetPages()
        {
            return Array.Empty<SettingsPageDescriptor>();
        }

        public bool TryGetPage(string pageId, out SettingsPageDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }
    }

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
        return new LucideIcon
        {
            Kind = kind,
            Width = 16,
            Height = 16
        };
    }
}
