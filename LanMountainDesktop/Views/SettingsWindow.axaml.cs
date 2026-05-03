using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly Dictionary<string, Control> _cachedPages = new(StringComparer.OrdinalIgnoreCase);
    private bool _useSystemChrome;
    private bool _isResponsiveRefreshPending;
    private bool _isRestartPromptVisible;

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
        CloseDrawer();
        RebuildNavigationItems();
        NavigateTo(pageId ?? ViewModel.Pages.FirstOrDefault()?.PageId);
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
        var selectedItem = e.SelectedItemContainer ?? e.SelectedItem as FANavigationViewItem;
        NavigateTo(selectedItem?.Tag as string);
    }

    private void NavigateTo(string? pageId)
    {
        var previousPageId = ViewModel.CurrentPageId;
        var descriptor = ResolveDescriptor(pageId);
        if (descriptor is null)
        {
            return;
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
        TrySelectNavigationItem(descriptor.PageId);
        SyncTitleText();
        UpdateResponsiveLayout();
        RequestResponsiveLayoutRefresh();
        if (!string.Equals(previousPageId, descriptor.PageId, StringComparison.OrdinalIgnoreCase))
        {
            TelemetryServices.Usage?.TrackSettingsNavigation(previousPageId, descriptor.PageId, "navigation");
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
        return page;
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

    private void OnClosed(object? sender, EventArgs e)
    {
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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTogglePaneButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
            e.Property == FANavigationView.PaneDisplayModeProperty)
        {
            UpdatePaneToggleIcon();
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
        if (TogglePaneButtonIcon is null || RootNavigationView is null)
        {
            return;
        }

        TogglePaneButtonIcon.Icon = RootNavigationView.IsPaneOpen
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
            TogglePaneButton is null ||
            TogglePaneButtonIcon is null ||
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
        var titleBarButtonWidth = Math.Clamp(40d * layoutScale, 36d, 48d);
        var titleBarButtonHeight = Math.Clamp(32d * layoutScale, 30d, 38d);
        var titleFontSize = Math.Clamp(12d * layoutScale, 11d, 14d);
        var titleBarIconSize = Math.Clamp(16d * layoutScale, 15d, 20d);
        var drawerTitleFontSize = Math.Clamp(16d * layoutScale, 14d, 20d);
        var chromePadding = Math.Clamp(12d * layoutScale, 10d, 18d);
        var restartSpacing = Math.Clamp(6d * layoutScale, 6d, 10d);

        ExtendClientAreaTitleBarHeightHint = titleBarHeight;

        WindowTitleBarHost.Height = titleBarHeight;

        TogglePaneButton.Width = titleBarButtonWidth;
        TogglePaneButton.Height = titleBarButtonHeight;
        TogglePaneButtonIcon.FontSize = titleBarIconSize;
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
