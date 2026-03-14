using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Services.Settings;

public enum SettingsWindowAnchorTarget
{
    DesktopDockTrailingEdge = 0
}

public enum SettingsWindowFallbackMode
{
    None = 0,
    ScreenBottomRight = 1
}

public readonly record struct SettingsWindowOpenRequest(
    string Source,
    Window? Owner = null,
    string? PageId = null,
    SettingsWindowAnchorTarget AnchorTarget = SettingsWindowAnchorTarget.DesktopDockTrailingEdge,
    SettingsWindowFallbackMode FallbackMode = SettingsWindowFallbackMode.ScreenBottomRight);

public interface ISettingsWindowAnchorProvider
{
    bool TryGetSettingsWindowAnchorBounds(out PixelRect anchorBounds);
}

public interface ISettingsWindowService
{
    bool IsOpen { get; }

    event EventHandler? StateChanged;

    void Open(SettingsWindowOpenRequest request);

    void Close();

    void Toggle(SettingsWindowOpenRequest request);
}

internal sealed class SettingsWindowService : ISettingsWindowService
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF3B82F6");
    private readonly ISettingsPageRegistry _pageRegistry;
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService;
    private SettingsWindowViewModel _viewModel = null!;
    private SettingsWindow? _window;

    public SettingsWindowService(
        ISettingsPageRegistry pageRegistry,
        IHostApplicationLifecycle hostApplicationLifecycle,
        ISettingsFacadeService settingsFacade)
    {
        _pageRegistry = pageRegistry;
        _hostApplicationLifecycle = hostApplicationLifecycle;
        _settingsFacade = settingsFacade;
        _localizationService = new();
        _settingsFacade.Settings.Changed += OnSettingsChanged;
    }

    private string L(string key)
    {
        var regionState = _settingsFacade.Region.Get();
        var languageCode = regionState.LanguageCode ?? "zh-CN";
        return _localizationService.GetString(languageCode, key, key);
    }

    public bool IsOpen => _window is { IsVisible: true };
    public event EventHandler? StateChanged;

    public void Open(SettingsWindowOpenRequest request)
    {
        _pageRegistry.Rebuild();
        _window ??= CreateWindow();
        var themeState = _settingsFacade.Theme.Get();
        _window.ApplyChromeMode(themeState.UseSystemChrome);
        ApplyTheme(_window, themeState);
        _window.ReloadPages(request.PageId);
        PositionWindow(_window, request);

        if (!_window.IsVisible)
        {
            if (request.Owner is not null && request.Owner.IsVisible)
            {
                _window.Show(request.Owner);
            }
            else
            {
                _window.Show();
            }

            NotifyStateChanged();
            PositionWindowLater(_window, request);
            return;
        }

        _window.Activate();
        PositionWindowLater(_window, request);
    }

    public void Close()
    {
        _window?.Close();
    }

    public void Toggle(SettingsWindowOpenRequest request)
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        Open(request);
    }

    private SettingsWindow CreateWindow()
    {
        var regionState = _settingsFacade.Region.Get();
        var languageCode = regionState.LanguageCode ?? "zh-CN";

        _viewModel = new SettingsWindowViewModel(_localizationService, languageCode).Initialize();

        var themeState = _settingsFacade.Theme.Get();
        var useSystemChrome = themeState.UseSystemChrome;

        var window = new SettingsWindow(
            _viewModel,
            _pageRegistry,
            _hostApplicationLifecycle,
            useSystemChrome);
        ApplyTheme(window, themeState);
        window.ShowInTaskbar = false;
        window.Closed += (_, _) =>
        {
            _window = null;
            NotifyStateChanged();
        };
        return window;
    }

    private void PositionWindowLater(SettingsWindow window, SettingsWindowOpenRequest request)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!window.IsVisible)
                {
                    return;
                }

                PositionWindow(window, request);
            },
            DispatcherPriority.Background);
    }

    private static void PositionWindow(SettingsWindow window, SettingsWindowOpenRequest request)
    {
        if (request.AnchorTarget == SettingsWindowAnchorTarget.DesktopDockTrailingEdge &&
            request.Owner is ISettingsWindowAnchorProvider anchorProvider &&
            anchorProvider.TryGetSettingsWindowAnchorBounds(out var anchorBounds))
        {
            PositionWindowAboveAnchor(window, anchorBounds, request);
            return;
        }

        if (request.FallbackMode == SettingsWindowFallbackMode.ScreenBottomRight)
        {
            PositionWindowNearScreenBottomRight(window, request);
        }
    }

    private static void PositionWindowAboveAnchor(Window window, PixelRect anchorBounds, SettingsWindowOpenRequest request)
    {
        var workingArea = GetWorkingArea(window, request);
        
        if (anchorBounds.Width <= 0 || anchorBounds.Height <= 0 ||
            anchorBounds.Right < workingArea.X || anchorBounds.Y > workingArea.Bottom)
        {
            PositionWindowNearScreenBottomRight(window, request);
            return;
        }
        
        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1d;
        var width = ResolveWindowWidth(window, scale);
        var height = ResolveWindowHeight(window, scale);
        var inset = (int)Math.Round(24 * scale);
        var gap = (int)Math.Round(16 * scale);

        var x = anchorBounds.Right - width - inset;
        var y = anchorBounds.Y - height - gap;
        x = Math.Clamp(x, workingArea.X + inset, Math.Max(workingArea.X + inset, workingArea.Right - width - inset));
        y = Math.Clamp(y, workingArea.Y + inset, Math.Max(workingArea.Y + inset, workingArea.Bottom - height - inset));
        window.Position = new PixelPoint(x, y);
    }

    private static void PositionWindowNearScreenBottomRight(Window window, SettingsWindowOpenRequest request)
    {
        var workingArea = GetWorkingArea(window, request);
        var scale = window.RenderScaling > 0 ? window.RenderScaling : 1d;
        var width = ResolveWindowWidth(window, scale);
        var height = ResolveWindowHeight(window, scale);
        var inset = (int)Math.Round(24 * scale);

        var x = Math.Max(workingArea.X + inset, workingArea.Right - width - inset);
        var y = Math.Max(workingArea.Y + inset, workingArea.Bottom - height - inset);
        window.Position = new PixelPoint(x, y);
    }

    private static PixelRect GetWorkingArea(Window window, SettingsWindowOpenRequest request)
    {
        if (request.Owner is not null && request.Owner.Screens?.ScreenFromWindow(request.Owner) is { } ownerScreen)
        {
            return ownerScreen.WorkingArea;
        }

        if (window.Screens?.ScreenFromWindow(window) is { } windowScreen)
        {
            return windowScreen.WorkingArea;
        }

        return window.Screens?.Primary?.WorkingArea
               ?? new PixelRect(
                   0,
                   0,
                   Math.Max(1280, ResolveWindowWidth(window, 1d) + 96),
                   Math.Max(720, ResolveWindowHeight(window, 1d) + 96));
    }

    private static int ResolveWindowWidth(Window window, double scale)
    {
        var widthDip = window.Bounds.Width > 1 ? window.Bounds.Width : Math.Max(window.Width, window.MinWidth);
        return Math.Max(320, (int)Math.Round(widthDip * scale));
    }

    private static int ResolveWindowHeight(Window window, double scale)
    {
        var heightDip = window.Bounds.Height > 1 ? window.Bounds.Height : Math.Max(window.Height, window.MinHeight);
        return Math.Max(240, (int)Math.Round(heightDip * scale));
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != SettingsScope.App)
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
            var themeChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.IsNightMode), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColor), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperPath), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperType), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperColor), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.UseSystemChrome), StringComparer.OrdinalIgnoreCase);

            if (languageChanged)
            {
                var regionState = _settingsFacade.Region.Get();
                _viewModel.RefreshLanguage(regionState.LanguageCode);
                _pageRegistry.Rebuild();
                _window.ReloadPages(_viewModel.CurrentPageId);
                _window.RefreshShellText();
            }

            if (themeChanged)
            {
                var themeState = _settingsFacade.Theme.Get();
                _window.ApplyChromeMode(themeState.UseSystemChrome);
                ApplyTheme(_window, themeState);
            }
        }, DispatcherPriority.Background);
    }

    private static void ApplyTheme(SettingsWindow window, ThemeAppearanceSettingsState themeState)
    {
        window.RequestedThemeVariant = themeState.IsNightMode
            ? ThemeVariant.Dark
            : ThemeVariant.Light;

        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var wallpaperState = settingsFacade.Wallpaper.Get();
        var monetPalette = settingsFacade.Theme.BuildPalette(
            themeState.IsNightMode,
            wallpaperState.WallpaperPath,
            themeState.ThemeColor);
        var accentColor = ResolveAccentColor(themeState.ThemeColor, monetPalette);
        var context = new ThemeColorContext(
            accentColor,
            IsLightBackground: !themeState.IsNightMode,
            IsLightNavBackground: !themeState.IsNightMode,
            IsNightMode: themeState.IsNightMode,
            MonetColors: monetPalette.MonetColors);
        ThemeColorSystemService.ApplyThemeResources(window.Resources, context);
        GlassEffectService.ApplyGlassResources(window.Resources, context);
    }

    private static Color ResolveAccentColor(string? colorText, MonetPalette monetPalette)
    {
        if (monetPalette.MonetColors is { Count: > 0 })
        {
            return monetPalette.MonetColors[0];
        }

        return TryParseThemeColor(colorText);
    }

    private static Color TryParseThemeColor(string? colorText)
    {
        if (!string.IsNullOrWhiteSpace(colorText))
        {
            try
            {
                return Color.Parse(colorText);
            }
            catch
            {
            }
        }

        return DefaultAccentColor;
    }
}
