using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Services.Settings;

public readonly record struct SettingsWindowOpenRequest(
    string Source,
    string? PageId = null,
    Window? ScreenReferenceWindow = null);

public interface ISettingsWindowService
{
    bool IsOpen { get; }

    event EventHandler? StateChanged;

    void Open(SettingsWindowOpenRequest request);

    void Close();
}

internal sealed class SettingsWindowService : ISettingsWindowService
{
    private readonly ISettingsPageRegistry _pageRegistry;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IAppearanceThemeService _appearanceThemeService;
    private readonly LocalizationService _localizationService;
    private SettingsWindowViewModel _viewModel = null!;
    private Window? _window;

    public SettingsWindowService(
        ISettingsPageRegistry pageRegistry,
        IHostApplicationLifecycle hostApplicationLifecycle,
        ISettingsFacadeService settingsFacade)
    {
        _pageRegistry = pageRegistry;
        _hostApplicationLifecycle = hostApplicationLifecycle;
        _settingsFacade = settingsFacade;
        _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
        _localizationService = new();
        _settingsFacade.Settings.Changed += OnSettingsChanged;
        _appearanceThemeService.Changed += OnAppearanceThemeChanged;
        AppSettingsService.SettingsSaved += OnAppSettingsSaved;
    }

    public bool IsOpen => _window is { IsVisible: true };
    public event EventHandler? StateChanged;

    public void Open(SettingsWindowOpenRequest request)
    {
        _pageRegistry.Rebuild();
        _window ??= CreateWindow();
        var appearanceSnapshot = _appearanceThemeService.GetCurrent();

        // 功能补齐后：重做版窗口与旧版回退窗口共用同一套行为面（chrome 模式、主题资源、页面重建），
        // 由静态调度器按实际窗口类型分派，不再把能力写死在某一个类上。
        ApplyChromeModeOnWindow(_window, appearanceSnapshot.UseSystemChrome);
        ApplyThemeVariantAndResources(_window);
        var targetPageId = request.PageId ?? _viewModel.CurrentPageId;
        ReloadPagesOnWindow(_window, targetPageId);

        if (!_window.IsVisible)
        {
            CenterWindow(_window, request);
            _appearanceThemeService.ApplyWindowMaterial(_window, MaterialSurfaceRole.SettingsWindowBackground);
            _window.Show();
            NotifyStateChanged();
            CenterWindowLater(_window, request);
            return;
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _appearanceThemeService.ApplyWindowMaterial(_window, MaterialSurfaceRole.SettingsWindowBackground);
        _window.Activate();
    }

    public void Close()
    {
        _window?.Close();
    }

    private Window CreateWindow()
    {
        var regionState = _settingsFacade.Region.Get();
        var languageCode = regionState.LanguageCode ?? LocalizationService.DefaultLanguageCode;

        _viewModel = new SettingsWindowViewModel(_localizationService, languageCode).Initialize();

        // 用户拍板的「彻底重做版」：注入完整服务图（注册表 / 生命周期 / 设置门面 / VM），
        // 使导航、搜索、动态 AirApp 页、语言与 Dev 模式刷新全部真实可用（§11.3）。
        var window = new ArcSettingsWindow(
            _viewModel,
            _pageRegistry,
            _hostApplicationLifecycle,
            _settingsFacade)
        {
            ShowInTaskbar = true
        };
        window.Closed += (_, _) =>
        {
            _window = null;
            NotifyStateChanged();
        };
        return window;
    }

    private void CenterWindowLater(Window window, SettingsWindowOpenRequest request)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(_window, window) || !window.IsVisible)
                {
                    return;
                }

                CenterWindow(window, request);
            },
            DispatcherPriority.Background);
    }

    private static void CenterWindow(Window window, SettingsWindowOpenRequest request)
    {
        var referenceWorkingArea =
            request.ScreenReferenceWindow is { IsVisible: true } screenReferenceWindow &&
            screenReferenceWindow.Screens?.ScreenFromWindow(screenReferenceWindow) is { } referenceScreen
                ? referenceScreen.WorkingArea
                : (PixelRect?)null;
        var width = ResolveWindowWidth(window, request.ScreenReferenceWindow);
        var height = ResolveWindowHeight(window, request.ScreenReferenceWindow);
        var workingArea = SettingsWindowPlacementHelper.ResolveWorkingArea(
            referenceWorkingArea,
            window.Screens?.Primary?.WorkingArea,
            width,
            height);
        window.Position = SettingsWindowPlacementHelper.CalculateCenteredPosition(workingArea, width, height);
    }

    private static int ResolveWindowWidth(Window window, Window? referenceWindow)
    {
        var widthDip = ResolveWindowDimensionDip(window.Bounds.Width, window.Width, window.MinWidth, 1120d);
        var scale = ResolveWindowScale(window, referenceWindow);
        return Math.Max(320, (int)Math.Round(widthDip * scale));
    }

    private static int ResolveWindowHeight(Window window, Window? referenceWindow)
    {
        var heightDip = ResolveWindowDimensionDip(window.Bounds.Height, window.Height, window.MinHeight, 760d);
        var scale = ResolveWindowScale(window, referenceWindow);
        return Math.Max(240, (int)Math.Round(heightDip * scale));
    }

    private static double ResolveWindowScale(Window window, Window? referenceWindow)
    {
        if (referenceWindow is not null && referenceWindow.RenderScaling > 0)
        {
            return referenceWindow.RenderScaling;
        }

        if (window.RenderScaling > 0)
        {
            return window.RenderScaling;
        }

        return 1d;
    }

    private static double ResolveWindowDimensionDip(double boundsDip, double configuredDip, double minimumDip, double fallbackDip)
    {
        if (boundsDip > 1)
        {
            return boundsDip;
        }

        if (!double.IsNaN(configuredDip) && configuredDip > 1)
        {
            return configuredDip;
        }

        if (!double.IsNaN(minimumDip) && minimumDip > 1)
        {
            return minimumDip;
        }

        return fallbackDip;
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 重做版（ArcSettingsWindow）与旧版回退（SettingsWindow）共用同一份行为面；
    // 集中分派，避免把能力写死在某一个窗口类型上（§11.3）。
    private static void ReloadPagesOnWindow(Window? window, string? pageId)
    {
        if (window is ArcSettingsWindow arc)
        {
            arc.ReloadPages(pageId);
        }
        else if (window is SettingsWindow legacy)
        {
            legacy.ReloadPages(pageId);
        }
    }

    private static void RefreshShellTextOnWindow(Window? window)
    {
        if (window is ArcSettingsWindow arc)
        {
            arc.RefreshShellText();
        }
        else if (window is SettingsWindow legacy)
        {
            legacy.RefreshShellText();
        }
    }

    // 重做版窗口使用系统原生标题栏、圆角走 Arc 令牌，这两项都不需要按窗口类型切换；
    // 只有旧版回退窗口（FAAppWindow 自绘标题栏）才需要 ApplyChromeMode / ApplyFluentCornerRadius。
    private static void ApplyChromeModeOnWindow(Window? window, bool useSystemChrome)
    {
        if (window is SettingsWindow legacy)
        {
            legacy.ApplyChromeMode(useSystemChrome);
        }
    }

    private static void ApplyFluentCornerRadiusOnWindow(Window? window)
    {
        if (window is SettingsWindow legacy)
        {
            legacy.ApplyFluentCornerRadius();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != AirAppSettingsScope.App)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null || _viewModel is null)
            {
                return;
            }

            var changedKeys = e.ChangedKeys?.ToArray();
            var refreshAll = changedKeys is null || changedKeys.Length == 0;
            var languageChanged = refreshAll || changedKeys.Contains(nameof(AppSettingsSnapshot.LanguageCode), StringComparer.OrdinalIgnoreCase);
            var devModeChanged = refreshAll || changedKeys.Contains(nameof(AppSettingsSnapshot.IsDevModeEnabled), StringComparer.OrdinalIgnoreCase);
            var liveAppearance = _appearanceThemeService.GetCurrent();
            var themeChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.IsNightMode), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColorMode), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.SystemMaterialMode), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.CornerRadiusStyle), StringComparer.OrdinalIgnoreCase) ||
                (string.Equals(liveAppearance.ThemeColorMode, ThemeAppearanceValues.ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase) &&
                 changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColor), StringComparer.OrdinalIgnoreCase)) ||
                (string.Equals(liveAppearance.ThemeColorMode, ThemeAppearanceValues.ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase) &&
                 (changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperPath), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperType), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperColor), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeWallpaperColorSource), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.UseNativeWallpaperChangeEvents), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.SystemWallpaperRefreshIntervalSeconds), StringComparer.OrdinalIgnoreCase))) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.UseSystemChrome), StringComparer.OrdinalIgnoreCase);

            if (languageChanged || devModeChanged)
            {
                var regionState = _settingsFacade.Region.Get();
                _localizationService.ClearCache();
                _viewModel.RefreshLanguage(regionState.LanguageCode);
                _pageRegistry.Rebuild();
                ReloadPagesOnWindow(_window, devModeChanged ? "dev" : _viewModel.CurrentPageId);
                RefreshShellTextOnWindow(_window);
            }

            if (themeChanged)
            {
                var appearanceSnapshot = _appearanceThemeService.GetCurrent();
                ApplyChromeModeOnWindow(_window, appearanceSnapshot.UseSystemChrome);
                ApplyTheme(_window);
            }
        }, DispatcherPriority.Background);
    }

    private void OnAppSettingsSaved(string instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null)
            {
                return;
            }

            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
            var devPageVisible = _pageRegistry.GetPages().Any(p => p.PageId == "dev");

            if (snapshot.IsDevModeEnabled && !devPageVisible)
            {
                _pageRegistry.Rebuild();
                ReloadPagesOnWindow(_window, "dev");
            }
            else if (!snapshot.IsDevModeEnabled && devPageVisible)
            {
                _pageRegistry.Rebuild();
                ReloadPagesOnWindow(_window, null);
            }
        }, DispatcherPriority.Background);
    }

    private static void ApplyThemeVariantAndResources(Window window, IAppearanceThemeService appearanceThemeService)
    {
        var appearanceSnapshot = appearanceThemeService.GetCurrent();
        window.RequestedThemeVariant = appearanceSnapshot.IsNightMode
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        appearanceThemeService.ApplyThemeResources(window.Resources);
        ApplyFluentCornerRadiusOnWindow(window);
    }

    private void ApplyThemeVariantAndResources(Window window)
    {
        ApplyThemeVariantAndResources(window, _appearanceThemeService);
    }

    private void ApplyTheme(Window window)
    {
        ApplyThemeVariantAndResources(window, _appearanceThemeService);
        _appearanceThemeService.ApplyWindowMaterial(window, MaterialSurfaceRole.SettingsWindowBackground);
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot e)
    {
        _ = sender;
        _ = e;

        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null || _viewModel is null)
            {
                return;
            }

            ApplyTheme(_window);
        }, DispatcherPriority.Background);
    }
}

internal static class SettingsWindowPlacementHelper
{
    internal static PixelRect ResolveWorkingArea(
        PixelRect? referenceWorkingArea,
        PixelRect? primaryWorkingArea,
        int fallbackWindowWidth,
        int fallbackWindowHeight)
    {
        if (referenceWorkingArea is { } referenceArea)
        {
            return referenceArea;
        }

        if (primaryWorkingArea is { } primaryArea)
        {
            return primaryArea;
        }

        return new PixelRect(
            0,
            0,
            Math.Max(1280, fallbackWindowWidth + 96),
            Math.Max(720, fallbackWindowHeight + 96));
    }

    internal static PixelPoint CalculateCenteredPosition(PixelRect workingArea, int windowWidth, int windowHeight)
    {
        var horizontalOffset = Math.Max(0, (workingArea.Width - windowWidth) / 2);
        var verticalOffset = Math.Max(0, (workingArea.Height - windowHeight) / 2);
        return new PixelPoint(
            workingArea.X + horizontalOffset,
            workingArea.Y + verticalOffset);
    }
}
