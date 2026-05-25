using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.Services;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryWindow : Window
{
    private TransparentOverlayWindow? _overlayWindow;

    public FusedDesktopComponentLibraryWindow()
    {
        InitializeComponent();
        ApplyFluentCornerRadius();

        LibraryControl.AddComponentRequested += OnAddComponentRequested;
        KeyDown += OnWindowKeyDown;

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.RegisterFusedLibraryWindow(this);
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

    public bool PreserveEditModeOnClose { get; private set; }

    public void SetOverlayWindow(TransparentOverlayWindow overlayWindow)
    {
        _overlayWindow = overlayWindow;
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
        if (_overlayWindow is null)
        {
            AppLogger.Warn("FusedDesktopLibrary", "Overlay window is not set.");
            return;
        }

        _overlayWindow.AddComponentToCenter(componentId);
        AppLogger.Info("FusedDesktopLibrary", $"Added component '{componentId}' at fused desktop grid center.");

        PreserveEditModeOnClose = true;
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        PreserveEditModeOnClose = false;
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
            PreserveEditModeOnClose = false;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        LibraryControl.AddComponentRequested -= OnAddComponentRequested;
        KeyDown -= OnWindowKeyDown;
        base.OnClosed(e);

        var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
        mainWindow?.UnregisterFusedLibraryWindow(this);
    }
}
