using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using Symbol = FluentIcons.Common.Symbol;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow : Window, ISettingsPageHostContext
{
    private readonly ISettingsPageRegistry _pageRegistry;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly Dictionary<string, Control> _cachedPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _useSystemChrome;

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
        ApplyChromeMode(useSystemChrome);

        Opened += OnOpened;
        SizeChanged += OnWindowSizeChanged;
        Closed += OnClosed;
        Loaded += OnLoaded;
        PendingRestartStateService.StateChanged += OnPendingRestartStateChanged;
    }

    public SettingsWindowViewModel ViewModel { get; }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SyncPendingRestartState();
        SyncTitleText();
        UpdateChromeMetrics();
        UpdatePaneToggleIcon();
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

    public void OpenDrawer(Control content, string? title = null)
    {
        if (DrawerContentHost is null)
        {
            return;
        }

        DrawerContentHost.Content = content;
        ViewModel.DrawerTitle = title ?? ViewModel.DrawerFallbackTitle;
        ViewModel.IsDrawerOpen = true;
        SyncTitleText();
    }

    public void CloseDrawer()
    {
        if (DrawerContentHost is not null)
        {
            DrawerContentHost.Content = null;
        }

        ViewModel.IsDrawerOpen = false;
        ViewModel.DrawerTitle = null;
        SyncTitleText();
    }

    public void RequestRestart(string? reason = null)
    {
        ViewModel.RestartMessage = string.IsNullOrWhiteSpace(reason)
            ? ViewModel.GetDefaultRestartMessage()
            : reason;
        ViewModel.IsRestartRequested = true;
    }

    public void ApplyChromeMode(bool useSystemChrome)
    {
        if (useSystemChrome || OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            SystemDecorations = SystemDecorations.Full;
            return;
        }

        SystemDecorations = SystemDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 48;
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
        SettingsPageCategory? previousCategory = null;

        foreach (var page in ViewModel.Pages)
        {
            if (previousCategory is not null && previousCategory != page.Category)
            {
                RootNavigationView.MenuItems.Add(new NavigationViewItemSeparator());
            }

            RootNavigationView.MenuItems.Add(new NavigationViewItem
            {
                Content = page.Title,
                Tag = page.PageId,
                IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
                {
                    Symbol = MapIcon(page.IconKey),
                    IconVariant = FluentIcons.Common.IconVariant.Regular
                }
            });

            previousCategory = page.Category;
        }
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        var selectedItem = e.SelectedItemContainer ?? e.SelectedItem as NavigationViewItem;
        NavigateTo(selectedItem?.Tag as string);
    }

    private void NavigateTo(string? pageId)
    {
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
        TrySelectNavigationItem(descriptor.PageId);
        SyncTitleText();
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

        foreach (var item in RootNavigationView.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, pageId, StringComparison.OrdinalIgnoreCase))
            {
                RootNavigationView.SelectedItem = item;
                return;
            }
        }
    }

    private async void OnRestartNowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _hostApplicationLifecycle.TryRestart(new HostApplicationLifecycleRequest(
            Source: "SettingsWindow",
            Reason: "User accepted restart from settings window."));
        await Task.CompletedTask;
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

    private void OnOpened(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateChromeMetrics();
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateChromeMetrics();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _cachedPages.Clear();
        PendingRestartStateService.StateChanged -= OnPendingRestartStateChanged;
        Opened -= OnOpened;
        SizeChanged -= OnWindowSizeChanged;
    }

    private void OnWindowTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
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
    }

    private void OnCloseWindowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void UpdatePaneToggleIcon()
    {
        if (TogglePaneButtonIcon is null || RootNavigationView is null)
        {
            return;
        }

        TogglePaneButtonIcon.Icon = RootNavigationView.IsPaneOpen
            ? FluentIcons.Common.Icon.PanelLeftContract
            : FluentIcons.Common.Icon.PanelLeftExpand;
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
            CloseWindowButton is null ||
            CloseWindowButtonIcon is null ||
            DrawerTitleTextBlock is null ||
            RootNavigationView is null)
        {
            return;
        }

        var width = Bounds.Width > 1 ? Bounds.Width : Math.Max(Width, MinWidth);
        var height = Bounds.Height > 1 ? Bounds.Height : Math.Max(Height, MinHeight);
        var layoutScale = Math.Clamp(Math.Min(width / 1120d, height / 760d), 0.90, 1.18);

        var titleBarHeight = Math.Clamp(48d * layoutScale, 44d, 58d);
        var titleBarButtonWidth = Math.Clamp(40d * layoutScale, 36d, 48d);
        var titleBarButtonHeight = Math.Clamp(32d * layoutScale, 30d, 38d);
        var titleFontSize = Math.Clamp(12d * layoutScale, 11d, 14d);
        var titleBarIconSize = Math.Clamp(16d * layoutScale, 15d, 20d);
        var drawerTitleFontSize = Math.Clamp(16d * layoutScale, 14d, 20d);
        var chromePadding = Math.Clamp(12d * layoutScale, 10d, 18d);
        var restartSpacing = Math.Clamp(6d * layoutScale, 6d, 10d);

        ExtendClientAreaTitleBarHeightHint = titleBarHeight;

        WindowTitleBarHost.Height = titleBarHeight;
        WindowTitleBarHost.Padding = new Thickness(chromePadding, 0, chromePadding, 0);

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

        CloseWindowButton.Width = titleBarButtonWidth;
        CloseWindowButton.Height = titleBarButtonHeight;
        CloseWindowButtonIcon.FontSize = titleBarIconSize;

        DrawerTitleTextBlock.FontSize = drawerTitleFontSize;

        RootNavigationView.OpenPaneLength = Math.Clamp(283d * layoutScale, 248d, 320d);
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
            "GridDots" => Symbol.GridDots,
            "PuzzlePiece" => Symbol.PuzzlePiece,
            "Info" => Symbol.Info,
            "ArrowSync" => Symbol.ArrowSync,
            _ => Symbol.Settings
        };
    }
}
