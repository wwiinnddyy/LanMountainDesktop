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
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using Symbol = FluentIcons.Common.Symbol;

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

        if (RootNavigationView is not null)
        {
            RootNavigationView.PropertyChanged += OnRootNavigationViewPropertyChanged;
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
        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;

        // SecRandom MainWindow：标题栏按钮悬停/按下/非活动色，与系统 caption 更一致
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        SyncPendingRestartState();
        SyncTitleText();
        UpdateChromeMetrics();
        UpdatePaneToggleVisibility();
        UpdatePaneToggleIcon();
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

        _cachedPages.Clear();
        _navigationBackStack.Clear();
        ViewModel.CanGoBack = false;
        CloseDrawer();
        RebuildNavigationItems();
        NavigateTo(pageId ?? ViewModel.Pages.FirstOrDefault()?.PageId, addHistory: false, source: "reload");
        RebuildSearchIndex(scanBuiltInPages: true);
        UpdatePaneToggleVisibility();
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

        ExtendClientAreaToDecorationsHint = true;
        WindowDecorations = WindowDecorations.Full;

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
        if (RootNavigationView is null)
        {
            return;
        }

        RootNavigationView.MenuItems.Clear();
        RootNavigationView.FooterMenuItems.Clear();

        SettingsPageCategory? previousCategory = null;

        foreach (var page in ViewModel.Pages)
        {
            var item = new FANavigationViewItem
            {
                Content = page.Title,
                Tag = page.PageId,
                IconSource = CreateSettingsIconSource(MapIcon(page.IconKey))
            };

            if (page.Category == SettingsPageCategory.About ||
                page.Category == SettingsPageCategory.Dev)
            {
                RootNavigationView.FooterMenuItems.Add(item);
                continue;
            }

            if (previousCategory is not null && previousCategory != page.Category)
            {
                RootNavigationView.MenuItems.Add(new FANavigationViewItemSeparator());
            }

            RootNavigationView.MenuItems.Add(item);

            previousCategory = page.Category;
        }
    }

    private void OnNavigationSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        _ = sender;
        var selectedItem = e.SelectedItemContainer ?? e.SelectedItem as FANavigationViewItem;
        NavigateTo(selectedItem?.Tag as string, addHistory: true, source: "navigation");
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
        if (page is SettingsPageBase settingsPage)
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
        UpdatePaneToggleVisibility();
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

    private Control GetOrCreatePage(SettingsPageDescriptor descriptor)
    {
        if (_cachedPages.TryGetValue(descriptor.PageId, out var page))
        {
            return page;
        }

        page = descriptor.CreatePage(this);
        if (page is SettingsPageBase settingsPage)
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
        if (RootNavigationView is null)
        {
            return;
        }

        var allItems = RootNavigationView.MenuItems.OfType<FANavigationViewItem>()
            .Concat(RootNavigationView.FooterMenuItems.OfType<FANavigationViewItem>());

        foreach (var item in allItems)
        {
            if (string.Equals(item.Tag as string, pageId, StringComparison.OrdinalIgnoreCase))
            {
                RootNavigationView.SelectedItem = item;
                return;
            }
        }
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
            var dialog = new FAContentDialog
            {
                Title = ViewModel.RestartDialogTitle,
                Content = ViewModel.RestartMessage,
                PrimaryButtonText = ViewModel.RestartDialogPrimaryText,
                CloseButtonText = ViewModel.RestartDialogCloseText,
                DefaultButton = FAContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync(this);
            if (result == FAContentDialogResult.Primary)
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
        var compactPaneWidth = Math.Clamp(48d * renderScale, 40d, 60d);
        var narrowThreshold = Math.Clamp(BaseNarrowThreshold * renderScale, 760d, 980d);
        var isNarrow = width < narrowThreshold;
        var paneOpenWidth = ComputePaneOpenLength();
        var paneReservedWidth = GetReservedPaneWidth(compactPaneWidth, isNarrow, paneOpenWidth);
        var containerMaxWidth = ComputeSettingsContainerMaxWidth();

        if (RootNavigationView is not null &&
            Math.Abs(RootNavigationView.OpenPaneLength - paneOpenWidth) > 0.5d)
        {
            RootNavigationView.OpenPaneLength = paneOpenWidth;
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

        var rootContentWidth = RootNavigationView?.Bounds.Width > 1
            ? RootNavigationView.Bounds.Width - paneReservedWidth
            : Math.Max(SettingsContentGrid.Bounds.Width, width - horizontalMargin * 2d - paneReservedWidth);
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
        _cachedPages.Clear();
        PendingRestartStateService.StateChanged -= OnPendingRestartStateChanged;
        if (RootNavigationView is not null)
        {
            RootNavigationView.PropertyChanged -= OnRootNavigationViewPropertyChanged;
        }
        Opened -= OnOpened;
        SizeChanged -= OnWindowSizeChanged;
        TelemetryServices.Usage?.TrackSettingsWindowClosed("SettingsWindow.OnClosed", ViewModel.CurrentPageId);
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

    private void OnTitleBarPaneToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (RootNavigationView is null)
        {
            return;
        }

        RootNavigationView.IsPaneOpen = !RootNavigationView.IsPaneOpen;
        UpdatePaneToggleIcon();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
    }

    private void OnRootNavigationViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;

        if (e.Property == FANavigationView.IsPaneOpenProperty ||
            e.Property == FANavigationView.OpenPaneLengthProperty ||
            e.Property == FANavigationView.PaneDisplayModeProperty ||
            e.Property == FANavigationView.IsPaneToggleButtonVisibleProperty)
        {
            if (e.Property == FANavigationView.IsPaneToggleButtonVisibleProperty)
            {
                UpdatePaneToggleVisibility();
            }

            UpdatePaneToggleIcon();
            RequestResponsiveLayoutRefresh();
        }
    }

    /// <summary>
    /// 仅在 <c>:minimal</c>（<see cref="FANavigationView.IsPaneToggleButtonVisible"/> 为 false）时显示侧栏底部备胎按钮。
    /// 根 DataContext 为 ViewModel 时，对 <c>#RootNavigationView</c> 的绑定易失效，故用代码同步可见性。
    /// </summary>
    private void UpdatePaneToggleVisibility()
    {
        if (TitleBarPaneToggleButton is null || RootNavigationView is null)
        {
            return;
        }

        TitleBarPaneToggleButton.IsVisible = !RootNavigationView.IsPaneToggleButtonVisible;
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

    private double GetReservedPaneWidth(double compactPaneWidth, bool isNarrow, double openPaneWidth)
    {
        if (RootNavigationView is null || isNarrow)
        {
            return 0d;
        }

        return RootNavigationView.IsPaneOpen
            ? openPaneWidth
            : compactPaneWidth;
    }

    private void UpdatePaneToggleIcon()
    {
        if (TitleBarPaneToggleButtonIcon is null || RootNavigationView is null)
        {
            return;
        }

        TitleBarPaneToggleButtonIcon.Icon = RootNavigationView.IsPaneOpen
            ? FluentIcons.Common.Icon.LineHorizontal3
            : FluentIcons.Common.Icon.Navigation;
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
            RootNavigationView is null)
        {
            return;
        }

        var width = Bounds.Width > 1 ? Bounds.Width : Math.Max(Width, MinWidth);
        var height = Bounds.Height > 1 ? Bounds.Height : Math.Max(Height, MinHeight);
        var layoutScale = Math.Clamp(Math.Min(width / 1120d, height / 760d), 0.90, 1.18);

        const double titleBarHeight = 48d;
        var titleFontSize = Math.Clamp(12d * layoutScale, 11d, 14d);
        var titleBarIconSize = Math.Clamp(16d * layoutScale, 15d, 20d);
        var drawerTitleFontSize = Math.Clamp(16d * layoutScale, 14d, 20d);
        var chromePadding = Math.Clamp(12d * layoutScale, 10d, 18d);
        var restartSpacing = Math.Clamp(6d * layoutScale, 6d, 10d);

        ExtendClientAreaTitleBarHeightHint = titleBarHeight;

        WindowTitleBarHost.Height = titleBarHeight;

        WindowBrandIcon.FontSize = titleBarIconSize + 2;

        WindowTitleTextBlock.FontSize = titleFontSize;

        RestartNowButton.Padding = new Thickness(chromePadding * 0.9, Math.Max(6, chromePadding * 0.5));
        if (RestartNowButton.Content is StackPanel restartStack)
        {
            restartStack.Spacing = restartSpacing;
        }

        RestartButtonIcon.FontSize = titleBarIconSize;
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

    private static Symbol MapIcon(string iconKey)
    {
        return iconKey?.Trim() switch
        {
            "DesignIdeas" => Symbol.Color,
            "Image" => Symbol.Image,
            "WeatherMoon" => Symbol.WeatherMoon,
            "Apps" => Symbol.Apps,
            "AppFolder" => Symbol.AppFolder,
            "AppsListDetail" => Symbol.AppsListDetail,
            "MatchAppLayout" => Symbol.MatchAppLayout,
            "Widget" => Symbol.GridDots,
            "SwitchApps" => Symbol.ArrowSync,
            "GridDots" => Symbol.GridDots,
            "PuzzlePiece" => Symbol.PuzzlePiece,
            "ShoppingBag" => Symbol.ShoppingBag,
            "Shield" => Symbol.ShieldLock,
            "Info" => Symbol.Info,
            "ArrowSync" => Symbol.ArrowSync,
            "Hourglass" => Symbol.Hourglass,
            "Alert" => Symbol.Alert,
            "Bell" => Symbol.AlertOn,
            "DeveloperBoard" => Symbol.DeveloperBoard,
            "FolderLink" => Symbol.FolderLink,
            "WindowConsole" => Symbol.WindowConsole,
            _ => Symbol.Settings
        };
    }

    private static FAFontIconSource CreateSettingsIconSource(Symbol symbol)
    {
        var symbolIcon = new FluentIcons.Avalonia.SymbolIcon
        {
            Symbol = symbol,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };

        // FluentAvalonia still expects IconSource here, so bridge the Avalonia 12 FluentIcons glyph/font into FAFontIconSource.
        var iconTextProp = typeof(FluentIcons.Avalonia.SymbolIcon).GetProperty("IconText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var iconFontProp = typeof(FluentIcons.Avalonia.SymbolIcon).GetProperty("IconFont", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var iconText = iconTextProp?.GetValue(symbolIcon) as string ?? "?";
        var iconFont = iconFontProp?.GetValue(symbolIcon);
        var fontFamily = iconFont?.GetType().GetProperty("FontFamily")?.GetValue(iconFont) as Avalonia.Media.FontFamily
            ?? new Avalonia.Media.FontFamily("avares://fluenticons.resources.avalonia/Assets#Seagull Fluent Icons");

        return new FAFontIconSource
        {
            Glyph = iconText,
            FontFamily = fontFamily,
            FontSize = 16
        };
    }
}
