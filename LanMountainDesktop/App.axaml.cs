using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaWebView;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop;

public partial class App : Application
{
    private enum DesktopShellState
    {
        ForegroundDesktop = 0,
        MinimizedToTaskbar = 1,
        TrayOnly = 2
    }

    private enum ShutdownIntent
    {
        None = 0,
        ExitRequested = 1,
        RestartRequested = 2
    }

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle = new HostApplicationLifecycleService();
    private bool _exitCleanupCompleted;
    private DesktopShellState _desktopShellState = DesktopShellState.ForegroundDesktop;
    private ShutdownIntent _shutdownIntent;

    private TrayIcons? _trayIcons;
    private PluginRuntimeService? _pluginRuntimeService;
    private MainWindow? _mainWindow;
    private bool _mainWindowClosed;

    internal static SingleInstanceService? CurrentSingleInstanceService { get; set; }
    internal static IHostApplicationLifecycle? CurrentHostApplicationLifecycle =>
        (Current as App)?._hostApplicationLifecycle;

    public PluginRuntimeService? PluginRuntimeService => _pluginRuntimeService;
    public IHostApplicationLifecycle HostApplicationLifecycle => _hostApplicationLifecycle;

    internal void OpenIndependentSettingsModule(string source, string? pageTag = null)
    {
        AppLogger.Info(
            "SettingsFacade",
            $"Settings UI entry is disabled by hard-cut migration. Source='{source}'; PageTag='{pageTag ?? "<default>"}'.");
    }

    public override void Initialize()
    {
        AppLogger.Info("App", "Initializing application resources.");
        ConfigureWebViewUserDataFolder();
        AvaloniaWebViewBuilder.Initialize(default);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLogger.Info("App", "Framework initialization completed.");
        LinuxDesktopEntryInstaller.EnsureInstalled();
        InitializePluginRuntime();
        AppSettingsService.SettingsSaved += OnAppSettingsSaved;
        InitializeTrayIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                AppLogger.Info("App", "Desktop lifetime exit triggered.");
                PerformExitCleanup();
            };

            CreateAndAssignMainWindow(desktop, "FrameworkInitialization");
            CurrentSingleInstanceService?.StartActivationListener(ActivateMainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        _ = _hostApplicationLifecycle.TryExit(new HostApplicationLifecycleRequest(
            Source: "TrayMenu",
            Reason: "User selected Exit App from the tray menu."));
    }

    private void OnTrayShowDesktopClick(object? sender, EventArgs e)
    {
        RestoreOrCreateMainWindow(showSingleInstanceNotice: false, source: "TrayMenu");
    }

    private void OnTrayRestartClick(object? sender, EventArgs e)
    {
        _ = _hostApplicationLifecycle.TryRestart(new HostApplicationLifecycleRequest(
            Source: "TrayMenu",
            Reason: "User selected Restart App from the tray menu."));
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static void ConfigureWebViewUserDataFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string userDataFolderEnvVar = "WEBVIEW2_USER_DATA_FOLDER";
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(userDataFolderEnvVar)))
            {
                return;
            }

            var userDataFolder = WebView2RuntimeProbe.ResolveUserDataFolder();
            Environment.SetEnvironmentVariable(
                userDataFolderEnvVar,
                userDataFolder,
                EnvironmentVariableTarget.Process);
        }
        catch (Exception ex)
        {
            // Keep startup resilient if user profile folders are unavailable.
            AppLogger.Warn("WebView2", "Failed to configure WebView2 user data folder.", ex);
        }
    }

    private void InitializePluginRuntime()
    {
        try
        {
            _pluginRuntimeService?.Dispose();
            _pluginRuntimeService = new PluginRuntimeService();
            _pluginRuntimeService.LoadInstalledPlugins();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PluginRuntime", "Failed to initialize plugin runtime.", ex);
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            DisposeTrayIcon();

            using var iconStream = AssetLoader.Open(new Uri("avares://LanMountainDesktop/Assets/avalonia-logo.ico"));
            var trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = L("tray.tooltip", "LanMountainDesktop"),
                Menu = BuildTrayMenu(),
                IsVisible = true
            };

            _trayIcons = [trayIcon];
            TrayIcon.SetIcons(this, _trayIcons);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TrayIcon", "Failed to initialize tray icon.", ex);
        }
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();

        var showDesktopItem = new NativeMenuItem(L("tray.menu.show_desktop", "Open Desktop"));
        showDesktopItem.Click += OnTrayShowDesktopClick;
        menu.Items.Add(showDesktopItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var restartItem = new NativeMenuItem(L("tray.menu.restart", "Restart App"));
        restartItem.Click += OnTrayRestartClick;
        menu.Items.Add(restartItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem(L("tray.menu.exit", "Exit App"));
        exitItem.Click += OnTrayExitClick;
        menu.Items.Add(exitItem);

        return menu;
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcons is null)
        {
            return;
        }

        TrayIcon.SetIcons(this, null);
        foreach (var trayIcon in _trayIcons)
        {
            trayIcon.Dispose();
        }

        _trayIcons = null;
    }

    private void ActivateMainWindow()
    {
        RestoreOrCreateMainWindow(showSingleInstanceNotice: true, source: "SingleInstance");
    }

    private void RestoreOrCreateMainWindow(bool showSingleInstanceNotice, string source)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            try
            {
                var mainWindow = GetOrCreateMainWindow(desktop, source);
                mainWindow.ShowInTaskbar = true;

                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }

                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }

                if (mainWindow.WindowState != WindowState.FullScreen)
                {
                    mainWindow.WindowState = WindowState.FullScreen;
                }

                mainWindow.Activate();
                mainWindow.Topmost = true;
                mainWindow.Topmost = false;
                SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"Restore:{source}");
                AppLogger.Info(
                    "DesktopShell",
                    $"Desktop restored. Source='{source}'; MainWindowClosed={_mainWindowClosed}; ShowSingleInstanceNotice={showSingleInstanceNotice}; WindowState='{mainWindow.WindowState}'.");

                if (showSingleInstanceNotice)
                {
                    mainWindow.ShowSingleInstanceNotice();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopShell", $"Failed to restore desktop shell. Source='{source}'.", ex);
            }
        }, DispatcherPriority.Send);
    }

    internal void PrepareForShutdown(bool isRestart, string source)
    {
        void Mark()
        {
            _shutdownIntent = isRestart
                ? ShutdownIntent.RestartRequested
                : ShutdownIntent.ExitRequested;
            AppLogger.Info(
                "DesktopShell",
                $"Shutdown intent marked. Intent='{_shutdownIntent}'; Source='{source}'; CurrentShellState='{_desktopShellState}'.");
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Mark();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(Mark, DispatcherPriority.Send).GetAwaiter().GetResult();
    }

    internal void ResetShutdownIntent(string source)
    {
        void Reset()
        {
            if (_shutdownIntent == ShutdownIntent.None)
            {
                return;
            }

            AppLogger.Warn(
                "DesktopShell",
                $"Shutdown intent cleared without process exit. PreviousIntent='{_shutdownIntent}'; Source='{source}'.");
            _shutdownIntent = ShutdownIntent.None;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Reset();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(Reset, DispatcherPriority.Send).GetAwaiter().GetResult();
    }

    private void OnAppSettingsSaved(string _)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcons is not null)
            {
                InitializeTrayIcon();
            }
        }, DispatcherPriority.Background);
    }

    private void PerformExitCleanup()
    {
        if (_exitCleanupCompleted)
        {
            return;
        }

        _exitCleanupCompleted = true;
        AppSettingsService.SettingsSaved -= OnAppSettingsSaved;

        try
        {
            _pluginRuntimeService?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PluginRuntime", "Failed to dispose plugin runtime during shutdown.", ex);
        }
        finally
        {
            _pluginRuntimeService = null;
        }

        AudioRecorderServiceFactory.DisposeSharedServices();
        StudyAnalyticsServiceFactory.DisposeSharedService();
        DisposeTrayIcon();
    }

    private MainWindow CreateAndAssignMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        string reason)
    {
        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
            ShowInTaskbar = true
        };

        AttachMainWindow(mainWindow);
        desktop.MainWindow = mainWindow;
        AppLogger.Info("App", $"Main window created. Reason='{reason}'. LogFile={AppLogger.LogFilePath}");
        LogBrowserStartupDiagnostics();
        SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"MainWindowCreated:{reason}");
        return mainWindow;
    }

    private MainWindow GetOrCreateMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        string reason)
    {
        if (_mainWindow is not null && !_mainWindowClosed)
        {
            return _mainWindow;
        }

        if (desktop.MainWindow is MainWindow desktopMainWindow && !_mainWindowClosed)
        {
            AttachMainWindow(desktopMainWindow);
            return desktopMainWindow;
        }

        return CreateAndAssignMainWindow(desktop, reason);
    }

    private void AttachMainWindow(MainWindow mainWindow)
    {
        if (ReferenceEquals(_mainWindow, mainWindow))
        {
            _mainWindowClosed = false;
            return;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.Closing -= OnMainWindowClosing;
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow.PropertyChanged -= OnMainWindowPropertyChanged;
        }

        _mainWindow = mainWindow;
        _mainWindowClosed = false;
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.Closed += OnMainWindowClosed;
        mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (sender is not MainWindow mainWindow)
        {
            return;
        }

        AppLogger.Info(
            "DesktopShell",
            $"Main window closing requested. Intent='{_shutdownIntent}'; ShellState='{_desktopShellState}'; WindowState='{mainWindow.WindowState}'; IsVisible={mainWindow.IsVisible}.");

        if (_shutdownIntent is ShutdownIntent.ExitRequested or ShutdownIntent.RestartRequested)
        {
            AppLogger.Info(
                "DesktopShell",
                $"Main window close allowed. Intent='{_shutdownIntent}'; ShellState='{_desktopShellState}'.");
            return;
        }

        e.Cancel = true;
        HideMainWindowToTray(mainWindow, "MainWindowClosing");
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow.Closing -= OnMainWindowClosing;
        mainWindow.Closed -= OnMainWindowClosed;
        mainWindow.PropertyChanged -= OnMainWindowPropertyChanged;

        if (ReferenceEquals(_mainWindow, mainWindow))
        {
            _mainWindow = null;
        }

        _mainWindowClosed = true;
        AppLogger.Info(
            "DesktopShell",
            $"Main window closed. Intent='{_shutdownIntent}'; ShellState='{_desktopShellState}'.");

        if (_shutdownIntent == ShutdownIntent.None)
        {
            SetDesktopShellState(DesktopShellState.TrayOnly, "MainWindowClosedUnexpected");
        }
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not MainWindow mainWindow)
        {
            return;
        }

        if (e.Property != Window.WindowStateProperty)
        {
            return;
        }

        if (_shutdownIntent != ShutdownIntent.None || !mainWindow.IsVisible)
        {
            return;
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            SetDesktopShellState(DesktopShellState.MinimizedToTaskbar, "MainWindowMinimized");
            return;
        }

        SetDesktopShellState(DesktopShellState.ForegroundDesktop, "MainWindowRestored");
    }

    private void HideMainWindowToTray(MainWindow mainWindow, string source)
    {
        try
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.Hide();
            SetDesktopShellState(DesktopShellState.TrayOnly, source);
            AppLogger.Info(
                "DesktopShell",
                $"Main window hidden to tray. Source='{source}'; WindowState='{mainWindow.WindowState}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopShell", $"Failed to hide main window to tray. Source='{source}'.", ex);
        }
    }

    private void SetDesktopShellState(DesktopShellState state, string source)
    {
        if (_desktopShellState == state)
        {
            return;
        }

        var previous = _desktopShellState;
        _desktopShellState = state;
        AppLogger.Info(
            "DesktopShell",
            $"Shell state changed. Previous='{previous}'; Current='{state}'; Source='{source}'.");
    }

    private void LogBrowserStartupDiagnostics()
    {
        try
        {
            var snapshot = new DesktopLayoutSettingsService().Load();
            var browserPlacements = snapshot.DesktopComponentPlacements
                .Where(placement => string.Equals(
                    placement.ComponentId,
                    BuiltInComponentIds.DesktopBrowser,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var runtimeAvailability = WebView2RuntimeProbe.GetAvailability();

            AppLogger.Info(
                "StartupDiagnostics",
                $"Browser component diagnostics. HasBrowserPlacement={browserPlacements.Count > 0}; " +
                $"ActivePageHasBrowser={browserPlacements.Any(item => item.PageIndex == snapshot.CurrentDesktopSurfaceIndex)}; " +
                $"CurrentDesktopSurfaceIndex={snapshot.CurrentDesktopSurfaceIndex}; " +
                $"WebViewRuntimeAvailable={runtimeAvailability.IsAvailable}; " +
                $"WebViewRuntimeVersion={runtimeAvailability.Version ?? string.Empty}; " +
                $"WebViewRuntimeMessage={runtimeAvailability.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("StartupDiagnostics", "Failed to log browser component diagnostics.", ex);
        }
    }

    private string L(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        return _localizationService.GetString(languageCode, key, fallback);
    }
}
