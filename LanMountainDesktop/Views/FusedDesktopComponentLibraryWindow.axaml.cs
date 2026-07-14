using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryWindow : Window
{
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private static readonly LocalizationService LocalizationService = new();

    public FusedDesktopComponentLibraryWindow()
    {
        InitializeComponent();
        Win32Properties.SetWindowCornerPreference(
            this,
            Win32Properties.WindowCornerPreference.DoNotRound);
        ApplyFluentCornerRadius();
        ApplyLocalization();

        Opened += OnWindowOpened;
        LibraryControl.AddComponentRequested += OnAddComponentRequested;
        KeyDown += OnWindowKeyDown;

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.RegisterFusedLibraryWindow(this);

        FusedDesktopManagerServiceFactory.GetOrCreate().EnterEditMode();
        AppLogger.Info("FusedDesktopLibrary", "Entered edit mode via library window open.");
    }

    private void ApplyLocalization()
    {
        var languageCode = HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode;
        var title = LocalizationService.GetString(
            languageCode,
            "fused_desktop.library.title",
            "Add widgets");
        Title = title;
        WindowTitleTextBlock.Text = title;
    }

    private void ApplyFluentCornerRadius()
    {
        if (RootGrid is null)
        {
            return;
        }

        var tokens = AppearanceCornerRadiusTokenFactory.Create(
            GlobalAppearanceSettings.CornerRadiusStyleFluent);
        RootGrid.Resources["DesignCornerRadiusMicro"] = tokens.Micro;
        RootGrid.Resources["DesignCornerRadiusXs"] = tokens.Xs;
        RootGrid.Resources["DesignCornerRadiusSm"] = tokens.Sm;
        RootGrid.Resources["DesignCornerRadiusMd"] = tokens.Md;
        RootGrid.Resources["DesignCornerRadiusLg"] = tokens.Lg;
        RootGrid.Resources["DesignCornerRadiusXl"] = tokens.Xl;
        RootGrid.Resources["DesignCornerRadiusIsland"] = tokens.Island;
        RootGrid.Resources["DesignCornerRadiusComponent"] = tokens.Component;
    }

    public void CenterInWorkArea(Window? referenceWindow = null)
    {
        var screen = referenceWindow is not null
            ? Screens.ScreenFromWindow(referenceWindow)
            : Screens.Primary;
        screen ??= Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling;
        var workArea = screen.WorkingArea;
        var widthPx = (int)Math.Round(Math.Max(MinWidth, Width) * scaling);
        var heightPx = (int)Math.Round(Math.Max(MinHeight, Height) * scaling);
        var x = workArea.X + Math.Max(0, (workArea.Width - widthPx) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - heightPx) / 2);
        Position = new PixelPoint(x, y);
    }

    private void OnAddComponentRequested(object? sender, string componentId)
    {
        FusedDesktopManagerServiceFactory.GetOrCreate().AddComponent(componentId, this);
        AppLogger.Info("FusedDesktopLibrary", $"Added component '{componentId}' directly to fused desktop.");
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        TryDisableNativeWindowBorder();
    }

    private void TryDisableNativeWindowBorder()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var borderColor = DwmColorNone;
            _ = DwmSetWindowAttribute(
                handle,
                DwmWindowAttributeBorderColor,
                ref borderColor,
                sizeof(uint));
        }
        catch
        {
            // DWM attributes are best-effort and unavailable on older/unsupported Windows builds.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        FusedDesktopManagerServiceFactory.GetOrCreate().ExitEditMode();
        AppLogger.Info("FusedDesktopLibrary", "Exited edit mode via library window close.");

        LibraryControl.AddComponentRequested -= OnAddComponentRequested;
        Opened -= OnWindowOpened;
        KeyDown -= OnWindowKeyDown;
        base.OnClosed(e);

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.UnregisterFusedLibraryWindow(this);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);
}
