using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaWebView;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.DesktopHost;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.ExternalIpc;
using LanMountainDesktop.Services.Launcher;
using LanMountainDesktop.Services.Loading;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using LanMountainDesktop.Theme;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop;

public partial class App : Application
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF3B82F6");
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

    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private readonly IAppearanceThemeService _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
    private readonly IAppLogoService _appLogoService = HostAppLogoProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly FontFamilyService _fontFamilyService = new();
    private readonly IHostApplicationLifecycle _hostApplicationLifecycle = new HostApplicationLifecycleService();
    private readonly IDetachedComponentLibraryWindowService _detachedComponentLibraryWindowService = new DetachedComponentLibraryWindowService();
    private readonly ILocationService _locationService = HostLocationServiceProvider.GetOrCreate();
    private readonly DateTimeOffset _startupAt = DateTimeOffset.UtcNow;
    private ISettingsPageRegistry? _settingsPageRegistry;
    private ISettingsWindowService? _settingsWindowService;
    private WeatherLocationRefreshService? _weatherLocationRefreshService;
    private INotificationService? _notificationService;
    private bool _exitCleanupCompleted;
    private DesktopShellState _desktopShellState = DesktopShellState.ForegroundDesktop;
    private ShutdownIntent _shutdownIntent;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayShowDesktopMenuItem;
    private NativeMenuItem? _traySettingsMenuItem;
    private NativeMenuItem? _trayComponentLibraryMenuItem;
    private NativeMenuItem? _trayRestartMenuItem;
    private NativeMenuItem? _trayExitMenuItem;
    private PluginRuntimeService? _pluginRuntimeService;
    private MainWindow? _mainWindow;
    private TransparentOverlayWindow? _transparentOverlayWindow;
    private bool _mainWindowClosed;
    private bool _uiUnhandledExceptionHooked;
    private DesktopShellHost? _desktopShellHost;
    private PublicIpcHostService? _publicIpcHostService;
    private LoadingStateManager? _loadingStateManager;
    private LoadingStateReporter? _loadingStateReporter;
    private bool _singleInstanceReleased;
    private int _forcedExitScheduled;
    private bool _mainWindowOpened;
    private bool _trayInitialized;
    private readonly object _launcherProgressLock = new();
    private readonly List<StartupProgressMessage> _pendingLauncherProgressMessages = [];

    internal static SingleInstanceService? CurrentSingleInstanceService { get; set; }
    internal static IHostApplicationLifecycle? CurrentHostApplicationLifecycle =>
        (Current as App)?._hostApplicationLifecycle;
    internal static INotificationService? CurrentNotificationService =>
        (Current as App)?._notificationService;

    // 闅愮鏀跨瓥鏌ョ湅浜嬩欢
    public static event Action? CurrentPrivacyPolicyViewRequested;

    public static void RaisePrivacyPolicyViewRequested()
    {
        CurrentPrivacyPolicyViewRequested?.Invoke();
    }

    public PluginRuntimeService? PluginRuntimeService => _pluginRuntimeService;
    public ISettingsFacadeService SettingsFacade => _settingsFacade;
    public IHostApplicationLifecycle HostApplicationLifecycle => _hostApplicationLifecycle;
    internal ISettingsWindowService? SettingsWindowService => _settingsWindowService;
    internal INotificationService? NotificationService => _notificationService;

    internal void OpenIndependentSettingsModule(string source, string? pageTag = null)
    {
        EnsureSettingsWindowService();
        AppLogger.Info(
            "SettingsFacade",
            $"Opening settings window. Source='{source}'; PageTag='{pageTag ?? "<default>"}'.");
        _settingsWindowService?.Open(new SettingsWindowOpenRequest(
            Source: source,
            PageId: pageTag,
            ScreenReferenceWindow: _mainWindow is { IsVisible: true } ? _mainWindow : null));
    }

    public App()
    {
        if (Design.IsDesignMode)
        {
            return;
        }

        _settingsFacade.Settings.Changed += OnSettingsChanged;
        _appearanceThemeService.Changed += OnAppearanceThemeChanged;
    }

    public override void Initialize()
    {
        AppLogger.Info("App", "Initializing application resources.");
        AvaloniaXamlLoader.Load(this);

        if (Design.IsDesignMode)
        {
            ApplyDesignTimeTheme();
            return;
        }

        ConfigureWebViewUserDataFolder();
        AvaloniaWebViewBuilder.Initialize(default);
        ApplyThemeFromSettings();
        ApplyCurrentCultureFromSettings();
        EnsureSettingsWindowService();
        EnsureWeatherLocationRefreshService();
        EnsureNotificationService();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppLogger.Info("App", "Framework initialization completed.");
        
        RegisterUiUnhandledExceptionGuard();
        LinuxDesktopEntryInstaller.EnsureInstalled();
        InitializePublicIpc();
        _ = InitializeLauncherIpcAsync();
        DesktopBootstrap.InitializeApplication(this, InitializeDesktopShell);

        if (!Design.IsDesignMode && OperatingSystem.IsWindows())
        {
            FusedDesktopManagerServiceFactory.GetOrCreate().Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    private async Task InitializeLauncherIpcAsync()
    {
        if (_loadingStateManager is not null)
            return;

        try
        {
            bool hadBufferedMessages;
            lock (_launcherProgressLock)
            {
                hadBufferedMessages = _pendingLauncherProgressMessages.Count > 0;
            }

            _loadingStateManager = new LoadingStateManager();
            _loadingStateReporter = new LoadingStateReporter(_loadingStateManager, _publicIpcHostService);
            _loadingStateReporter.Start();

            _loadingStateManager.RegisterItem("system.init", LoadingItemType.System, "System Initialization", "Initialize core application services.");
            _loadingStateManager.StartItem("system.init", "Public IPC host ready.");
            await FlushPendingLauncherProgressAsync();

            if (!hadBufferedMessages)
            {
                ReportStartupProgress(StartupStage.Initializing, 10, "Initializing application...");
                ReportStartupProgress(StartupStage.LoadingSettings, 20, "Loading settings...");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to initialize Launcher IPC: {ex.Message}");
        }
    }

    private void ReportStartupProgress(StartupStage stage, int percent, string message)
    {
        QueueOrSendLauncherProgress(new StartupProgressMessage
        {
            Stage = stage,
            ProgressPercent = percent,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        }, logSuccess: false);
    }

    private void ReportStartupProgressSync(StartupStage stage, int percent, string message)
    {
        QueueOrSendLauncherProgress(new StartupProgressMessage
        {
            Stage = stage,
            ProgressPercent = percent,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        }, logSuccess: true);
    }

    private void QueueOrSendLauncherProgress(StartupProgressMessage message, bool logSuccess)
    {
        var publicIpcHostService = _publicIpcHostService;
        if (publicIpcHostService is null)
        {
            lock (_launcherProgressLock)
            {
                _pendingLauncherProgressMessages.Add(message);
            }

            AppLogger.Info("LauncherIpc", $"Buffered launcher stage '{message.Stage}' because IPC is not connected yet.");
            return;
        }

        _ = SendLauncherProgressAsync(publicIpcHostService, message, logSuccess);
    }

    private async Task FlushPendingLauncherProgressAsync()
    {
        var publicIpcHostService = _publicIpcHostService;
        if (publicIpcHostService is null)
        {
            return;
        }

        StartupProgressMessage[] pendingMessages;
        lock (_launcherProgressLock)
        {
            pendingMessages = _pendingLauncherProgressMessages.ToArray();
            _pendingLauncherProgressMessages.Clear();
        }

        foreach (var pendingMessage in pendingMessages)
        {
            await SendLauncherProgressAsync(publicIpcHostService, pendingMessage, logSuccess: false);
        }
    }

    private async Task SendLauncherProgressAsync(PublicIpcHostService publicIpcHostService, StartupProgressMessage message, bool logSuccess)
    {
        try
        {
            await publicIpcHostService.PublishStartupProgressAsync(message);
            if (logSuccess)
            {
                AppLogger.Info("LauncherIpc", $"Successfully reported stage: {message.Stage}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");

            lock (_launcherProgressLock)
            {
                _pendingLauncherProgressMessages.Add(message);
            }
        }
    }
    private void ApplyDesignTimeTheme()
    {
        RequestedThemeVariant = ThemeVariant.Light;

        try
        {
            ApplyAdaptiveThemeResources();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Previewer", "Failed to apply adaptive theme resources in design mode.", ex);
        }
    }

    private void InitializeDesktopShell()
    {
        _desktopShellHost ??= new DesktopShellHost(
            InitializePluginRuntime,
            InitializeTrayIcon,
            desktop =>
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                ReportStartupProgress(StartupStage.InitializingUI, 60, "姝ｅ湪鍒濆鍖栫晫闈?..");
                CreateAndAssignMainWindow(desktop, "FrameworkInitialization");
            },
            OnDesktopLifetimeExit,
            () => CurrentSingleInstanceService?.StartActivationListener(ActivateMainWindow),
            StartWeatherLocationRefreshIfNeeded);
        _desktopShellHost.Initialize(this);
    }

    private void OnDesktopLifetimeExit()
    {
        AppLogger.Info("App", "Desktop lifetime exit triggered.");
        PerformExitCleanup();
        ReleaseSingleInstanceAfterExit("DesktopLifetimeExit");
        ScheduleForcedProcessTermination("DesktopLifetimeExit");
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

    private void OnTraySettingsClick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        OpenIndependentSettingsModule("TrayMenu");
    }

    private void OnTrayComponentLibraryClick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        
        if (!OperatingSystem.IsWindows())
        {
            AppLogger.Warn("FusedDesktop", "Fused desktop is only supported on Windows.");
            return;
        }

        FusedDesktopManagerServiceFactory.GetOrCreate().EnterEditMode();
        
        // 纭繚閫忔槑瑕嗙洊灞傜獥鍙ｅ瓨鍦ㄥ苟鏄剧ず
        EnsureTransparentOverlayWindow();
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_transparentOverlayWindow is not null && !_transparentOverlayWindow.IsVisible)
                {
                    _transparentOverlayWindow.Show();
                }
                
                var window = new FusedDesktopComponentLibraryWindow();
                
                if (_transparentOverlayWindow is not null)
                {
                    window.SetOverlayWindow(_transparentOverlayWindow);
                }
                
                window.Closed += (s, ev) =>
                {
                    if (_transparentOverlayWindow is not null)
                    {
                        // 瑙﹀彂鐢诲竷淇濆瓨锛屽苟闅愯棌鐢诲竷
                        _transparentOverlayWindow.SaveLayoutAndHide();
                    }
                    
                    // 璁╃鐞嗗櫒鏍规嵁宸插瓨鍌ㄧ殑鏈€鏂板揩鐓ч噸寤虹敓鎴愭墍鏈夊疄浣撳皬缁勪欢
                    FusedDesktopManagerServiceFactory.GetOrCreate().ExitEditMode();
                };

                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktop", "Failed to open fused desktop component library.", ex);
            }
        }, DispatcherPriority.Send);
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
        ReportStartupProgress(StartupStage.LoadingPlugins, 30, "姝ｅ湪鍔犺浇鎻掍欢...");
        try
        {
            _pluginRuntimeService?.Dispose();
            _pluginRuntimeService = new PluginRuntimeService(_settingsFacade, _publicIpcHostService);
            HostSettingsFacadeProvider.BindPluginRuntime(_pluginRuntimeService);
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
            if (_trayIcon is null)
            {
                _trayShowDesktopMenuItem = new NativeMenuItem();
                _trayShowDesktopMenuItem.Click += OnTrayShowDesktopClick;

                _traySettingsMenuItem = new NativeMenuItem();
                _traySettingsMenuItem.Click += OnTraySettingsClick;

                _trayComponentLibraryMenuItem = new NativeMenuItem();
                _trayComponentLibraryMenuItem.Click += OnTrayComponentLibraryClick;

                _trayRestartMenuItem = new NativeMenuItem();
                _trayRestartMenuItem.Click += OnTrayRestartClick;

                _trayExitMenuItem = new NativeMenuItem();
                _trayExitMenuItem.Click += OnTrayExitClick;

                var trayMenu = new NativeMenu();
                trayMenu.Items.Add(_trayShowDesktopMenuItem);
                trayMenu.Items.Add(_traySettingsMenuItem);
                trayMenu.Items.Add(_trayComponentLibraryMenuItem);
                trayMenu.Items.Add(new NativeMenuItemSeparator());
                trayMenu.Items.Add(_trayRestartMenuItem);
                trayMenu.Items.Add(new NativeMenuItemSeparator());
                trayMenu.Items.Add(_trayExitMenuItem);

                _trayIcon = new TrayIcon
                {
                    Icon = _appLogoService.CreateTrayIcon(),
                    Menu = trayMenu,
                    IsVisible = true
                };

                TrayIcon.SetIcons(this, [_trayIcon]);
            }

            RefreshTrayIconContent();
            _trayInitialized = true;
            AppLogger.Info("TrayIcon", $"Tray initialized successfully. Pid={Environment.ProcessId}.");
        }
        catch (Exception ex)
        {
            _trayInitialized = false;
            AppLogger.Warn("TrayIcon", "Failed to initialize tray icon.", ex);
        }
    }

    private void RefreshTrayIconContent()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = true;
            if (!OperatingSystem.IsLinux())
            {
                _trayIcon.ToolTipText = L("tray.tooltip", "LanMountainDesktop");
            }
        }

        if (_trayShowDesktopMenuItem is not null)
        {
            _trayShowDesktopMenuItem.Header = L("tray.menu.show_desktop", "Open Desktop");
        }

        if (_traySettingsMenuItem is not null)
        {
            _traySettingsMenuItem.Header = L("tray.menu.settings", "Settings");
        }

        RefreshFusedDesktopMenuItemVisibility();

        if (_trayRestartMenuItem is not null)
        {
            _trayRestartMenuItem.Header = L("tray.menu.restart", "Restart App");
        }

        if (_trayExitMenuItem is not null)
        {
            _trayExitMenuItem.Header = L("tray.menu.exit", "Exit App");
        }
    }

    private void RefreshFusedDesktopMenuItemVisibility()
    {
        if (_trayComponentLibraryMenuItem is null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _trayComponentLibraryMenuItem.IsVisible = false;
            return;
        }

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        _trayComponentLibraryMenuItem.IsVisible = appSnapshot.EnableFusedDesktop;

        if (_trayComponentLibraryMenuItem.IsVisible)
        {
            _trayComponentLibraryMenuItem.Header = L("tray.menu.component_library", "Component Library");
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        try
        {
            _trayIcon.IsVisible = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TrayIcon", "Failed to hide tray icon during cleanup.", ex);
        }
    }

    private void EnsureSettingsWindowService()
    {
        _settingsPageRegistry ??= new SettingsPageRegistry(
            _settingsFacade,
            _hostApplicationLifecycle,
            _localizationService,
            () => _pluginRuntimeService);
        _settingsWindowService ??= new SettingsWindowService(
            _settingsPageRegistry,
            _hostApplicationLifecycle,
            _settingsFacade);
    }

    private void EnsureWeatherLocationRefreshService()
    {
        _weatherLocationRefreshService ??= new WeatherLocationRefreshService(
            _settingsFacade,
            _locationService,
            _localizationService);
    }

    private void EnsureNotificationService()
    {
        _notificationService ??= new NotificationService(_appearanceThemeService);
    }

    private void StartWeatherLocationRefreshIfNeeded()
    {
        EnsureWeatherLocationRefreshService();
        if (_weatherLocationRefreshService is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _weatherLocationRefreshService.TryRefreshOnStartupAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Weather.Location", "Failed to refresh weather location during startup.", ex);
            }
        });
    }

    private void ApplyThemeFromSettings()
    {
        var snapshot = _appearanceThemeService.GetCurrent();
        RequestedThemeVariant = snapshot.IsNightMode
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        ApplyAdaptiveThemeResources();
    }

    private void ApplyCurrentCultureFromSettings()
    {
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(languageCode);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.GetCultureInfo("zh-CN");
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        ApplyLanguageSpecificFont(languageCode);
    }

    private void ApplyLanguageSpecificFont(string languageCode)
    {
        var fontFamily = _fontFamilyService.GetFontFamilyForLanguage(languageCode);
        if (Resources.TryGetValue("AppFontFamily", out var currentFont) &&
            currentFont is FontFamily currentFontFamily &&
            currentFontFamily.Name == fontFamily.Name)
        {
            return;
        }

        Resources["AppFontFamily"] = fontFamily;
    }

    private void ActivateMainWindow()
    {
        AppLogger.Info("SingleInstance", $"Activation callback received. Pid={Environment.ProcessId}.");

        try
        {
            var restored = Dispatcher.UIThread.CheckAccess()
                ? RestoreOrCreateMainWindowCore(showSingleInstanceNotice: true, source: "SingleInstance")
                : Dispatcher.UIThread.InvokeAsync(
                    () => RestoreOrCreateMainWindowCore(showSingleInstanceNotice: true, source: "SingleInstance"),
                    DispatcherPriority.Send).GetAwaiter().GetResult();

            if (!restored)
            {
                throw new InvalidOperationException("Main window restore failed in activation callback.");
            }

            AppLogger.Info("SingleInstance", "Activation callback completed successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SingleInstance", "Activation callback failed while restoring the desktop shell.", ex);
            throw;
        }
    }

    private void RestoreOrCreateMainWindow(bool showSingleInstanceNotice, string source)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _ = RestoreOrCreateMainWindowCore(showSingleInstanceNotice, source);
        }, DispatcherPriority.Send);
    }

    private bool RestoreOrCreateMainWindowCore(bool showSingleInstanceNotice, string source)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLogger.Warn("DesktopShell", $"Restore skipped because desktop lifetime is unavailable. Source='{source}'.");
            return false;
        }

        try
        {
            AppLogger.Info("DesktopShell", $"Restoring desktop shell started. Source='{source}'.");

            if (_transparentOverlayWindow is not null && _transparentOverlayWindow.IsVisible)
            {
                _transparentOverlayWindow.Hide();
            }

            var mainWindow = GetOrCreateMainWindow(desktop, source);
            mainWindow.PrepareEnterAnimation();

            mainWindow.ShowInTaskbar = ShouldShowMainWindowInTaskbar();

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

            Dispatcher.UIThread.Post(() =>
            {
                mainWindow.PlayEnterAnimation();
            }, DispatcherPriority.Background);

            SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"Restore:{source}");
            AppLogger.Info(
                "DesktopShell",
                $"Desktop restored. Source='{source}'; MainWindowClosed={_mainWindowClosed}; ShowSingleInstanceNotice={showSingleInstanceNotice}; WindowState='{mainWindow.WindowState}'.");

            if (showSingleInstanceNotice)
            {
                mainWindow.ShowSingleInstanceNotice();
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopShell", $"Failed to restore desktop shell. Source='{source}'.", ex);
            return false;
        }
    }
    
    private void EnsureTransparentOverlayWindow()
    {
        if (_transparentOverlayWindow is null)
        {
            _transparentOverlayWindow = new TransparentOverlayWindow();
            _transparentOverlayWindow.RestoreMainWindowRequested += (s, e) =>
            {
                RestoreOrCreateMainWindow(showSingleInstanceNotice: false, source: "TransparentOverlay");
            };
        }
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

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        _ = sender;

        if (e.Scope != SettingsScope.App)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var changedKeys = e.ChangedKeys?.ToArray();
            var refreshAll = changedKeys is null || changedKeys.Length == 0;
            var liveAppearance = _appearanceThemeService.GetCurrent();
            var themeChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.IsNightMode), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.UseSystemChrome), StringComparer.OrdinalIgnoreCase) ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.CornerRadiusStyle), StringComparer.OrdinalIgnoreCase) ||
                (string.Equals(liveAppearance.ThemeColorMode, ThemeAppearanceValues.ColorModeSeedMonet, StringComparison.OrdinalIgnoreCase) &&
                 changedKeys.Contains(nameof(AppSettingsSnapshot.ThemeColor), StringComparer.OrdinalIgnoreCase)) ||
                (string.Equals(liveAppearance.ThemeColorMode, ThemeAppearanceValues.ColorModeWallpaperMonet, StringComparison.OrdinalIgnoreCase) &&
                 (changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperPath), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperType), StringComparer.OrdinalIgnoreCase) ||
                  changedKeys.Contains(nameof(AppSettingsSnapshot.WallpaperColor), StringComparer.OrdinalIgnoreCase)));
            var languageChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.LanguageCode), StringComparer.OrdinalIgnoreCase);

            if (themeChanged)
            {
                ApplyThemeFromSettings();
            }

            if (languageChanged)
            {
                // 娓呴櫎鏈湴鍖栫紦瀛橈紝寮哄埗閲嶆柊鍔犺浇璇█鏂囦欢
                _localizationService.ClearCache();
                ApplyCurrentCultureFromSettings();
                RefreshTrayIconContent();
            }

            var fusedDesktopChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.EnableFusedDesktop), StringComparer.OrdinalIgnoreCase);

            if (fusedDesktopChanged)
            {
                RefreshFusedDesktopMenuItemVisibility();
            }
        }, DispatcherPriority.Background);
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot e)
    {
        _ = sender;
        _ = e;

        Dispatcher.UIThread.Post(ApplyThemeFromSettings, DispatcherPriority.Background);
    }

    private void ApplyAdaptiveThemeResources()
    {
        _appearanceThemeService.ApplyThemeResources(Resources);
    }

    private void RegisterUiUnhandledExceptionGuard()
    {
        if (_uiUnhandledExceptionHooked)
        {
            return;
        }

        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        _uiUnhandledExceptionHooked = true;
    }

    private void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!IsKnownWebViewStartupException(e.Exception))
        {
            return;
        }

        e.Handled = true;
        AppLogger.Warn(
            "WebView2",
            "Suppressed a known WebView startup exception from AvaloniaWebView.Navigate to keep the host process alive.",
            e.Exception);
    }

    private static bool IsKnownWebViewStartupException(Exception exception)
    {
        if (exception is not NullReferenceException)
        {
            return false;
        }

        var stackTrace = exception.StackTrace ?? string.Empty;
        return stackTrace.Contains("AvaloniaWebView.WebView.Navigate", StringComparison.Ordinal) &&
               stackTrace.Contains("AvaloniaWebView.WebView.OnAttachedToVisualTree", StringComparison.Ordinal);
    }

    private void ReleaseSingleInstanceAfterExit(string source)
    {
        if (_singleInstanceReleased)
        {
            return;
        }

        _singleInstanceReleased = true;
        var singleInstance = CurrentSingleInstanceService;
        CurrentSingleInstanceService = null;
        if (singleInstance is null)
        {
            AppLogger.Info("SingleInstance", $"No single-instance handle to release. Source='{source}'.");
            return;
        }

        try
        {
            singleInstance.Dispose();
            AppLogger.Info("SingleInstance", $"Released single-instance handle. Source='{source}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SingleInstance", $"Failed to release single-instance handle. Source='{source}'.", ex);
        }
    }

    private void ScheduleForcedProcessTermination(string source)
    {
        if (Interlocked.Exchange(ref _forcedExitScheduled, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                AppLogger.Warn(
                    "DesktopShell",
                    $"Process did not terminate after desktop exit cleanup. Forcing process exit. Source='{source}'; ShutdownIntent='{_shutdownIntent}'.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopShell", $"Forced process termination scheduler failed. Source='{source}'.", ex);
            }
        });
    }

    private void PerformExitCleanup()
    {
        if (_exitCleanupCompleted)
        {
            return;
        }

        _exitCleanupCompleted = true;
        _settingsFacade.Settings.Changed -= OnSettingsChanged;
        _appearanceThemeService.Changed -= OnAppearanceThemeChanged;

        try
        {
            TelemetryServices.Usage?.Shutdown(
                _shutdownIntent == ShutdownIntent.RestartRequested,
                "App.PerformExitCleanup");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Analytics", "Failed to shut down usage telemetry during exit cleanup.", ex);
        }

        try
        {
            HostUpdateWorkflowServiceProvider.GetOrCreate().TryApplyPendingUpdateOnExit();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateWorkflow", "Failed to apply pending update during exit cleanup.", ex);
        }

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

        try
        {
            _publicIpcHostService?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PublicIpc", "Failed to dispose public IPC host during shutdown.", ex);
        }
        finally
        {
            _publicIpcHostService = null;
        }

        _settingsWindowService?.Close();
        if (_settingsPageRegistry is IDisposable disposableRegistry)
        {
            disposableRegistry.Dispose();
        }

        if (_transparentOverlayWindow is not null)
        {
            try
            {
                _transparentOverlayWindow.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopShell", "Failed to close transparent overlay during exit cleanup.", ex);
            }
            finally
            {
                _transparentOverlayWindow = null;
            }
        }

        AudioRecorderServiceFactory.DisposeSharedServices();
        StudyAnalyticsServiceFactory.DisposeSharedService();
        DisposeTrayIcon();

        try
        {
            TelemetryServices.Crash?.CaptureShutdown(
                _shutdownIntent == ShutdownIntent.RestartRequested,
                "App.PerformExitCleanup");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Analytics", "Failed to capture crash shutdown telemetry during exit cleanup.", ex);
        }

        try
        {
            TelemetryServices.Crash?.Dispose();
            TelemetryServices.Usage?.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Analytics", "Failed to dispose telemetry services during exit cleanup.", ex);
        }
    }

    private MainWindow CreateAndAssignMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        string reason)
    {
        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
            ShowInTaskbar = ShouldShowMainWindowInTaskbar()
        };

        _mainWindowOpened = false;
        AttachMainWindow(mainWindow);
        desktop.MainWindow = mainWindow;
        AppLogger.Info("App", $"Main window created. Reason='{reason}'. LogFile={AppLogger.LogFilePath}");
        LogBrowserStartupDiagnostics();
        SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"MainWindowCreated:{reason}");
        ReportStartupProgress(StartupStage.ShellInitialized, 85, "Desktop shell initialized.");
        AppLogger.Info(
            "App",
            $"Shell initialized. Reason='{reason}'; TrayInitialized={_trayInitialized}; MainWindowVisible={mainWindow.IsVisible}.");

        mainWindow.Opened += OnMainWindowOpened;

        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (!_mainWindowOpened)
            {
                AppLogger.Warn("App", "Main window Opened event did not fire within 10 seconds. DesktopVisible was not reported.");
            }
        });

        return mainWindow;
    }
    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is MainWindow mainWindow)
        {
            mainWindow.Opened -= OnMainWindowOpened;
            _mainWindowOpened = true;

            AppLogger.Info(
                "App",
                $"Main window opened. Reporting DesktopVisible. TrayInitialized={_trayInitialized}; ShellState='{_desktopShellState}'.");

            _loadingStateManager?.CompleteItem("system.init", "System initialization completed.");
            ReportStartupProgressSync(StartupStage.DesktopVisible, 100, "Desktop visible.");
            ReportStartupProgressSync(StartupStage.Ready, 100, "Ready.");
            _loadingStateReporter?.Stop();
        }
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

    internal void HideMainWindowToTray(MainWindow mainWindow, string source)
    {
        try
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.Hide();
            SetDesktopShellState(DesktopShellState.TrayOnly, source);
            AppLogger.Info(
                "DesktopShell",
                $"Main window hidden to tray. Source='{source}'; WindowState='{mainWindow.WindowState}'.");
            
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            if (appSnapshot.EnableThreeFingerSwipe && appSnapshot.EnableFusedDesktop)
            {
                EnsureTransparentOverlayWindow();
                _transparentOverlayWindow?.Show();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopShell", $"Failed to hide main window to tray. Source='{source}'.", ex);
        }
    }

    private bool ShouldShowMainWindowInTaskbar()
    {
        return _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App).ShowInTaskbar;
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
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        return _localizationService.GetString(languageCode, key, fallback);
    }

    internal bool TryActivateMainWindowFromExternalIpc(string source)
    {
        return RestoreOrCreateMainWindowCore(showSingleInstanceNotice: false, source);
    }

    private void InitializePublicIpc()
    {
        if (_publicIpcHostService is not null)
        {
            return;
        }

        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            _publicIpcHostService = new PublicIpcHostService();
            _publicIpcHostService.PluginDescriptorProvider = BuildPublicPluginDescriptors;
            _publicIpcHostService.RegisterPublicService<IPublicAppInfoService>(
                new PublicAppInfoService(version, "Administrate", _startupAt));
            _publicIpcHostService.RegisterPublicService<IPublicShellControlService>(
                new PublicShellControlService());
            _publicIpcHostService.RegisterPublicService<IPublicPluginCatalogService>(
                new PublicPluginCatalogService(_publicIpcHostService));
            _publicIpcHostService.Start();
            AppLogger.Info("PublicIpc", $"Public IPC host started. PipeName='{IpcConstants.DefaultPipeName}'.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PublicIpc", "Failed to initialize public IPC host.", ex);
        }
    }

    private IReadOnlyList<PublicPluginDescriptor> BuildPublicPluginDescriptors()
    {
        var runtime = _pluginRuntimeService;
        if (runtime is null)
        {
            return Array.Empty<PublicPluginDescriptor>();
        }

        return runtime.Catalog
            .Select(entry => new PublicPluginDescriptor(
                entry.Manifest.Id,
                entry.Manifest.Name,
                entry.Manifest.Version,
                entry.IsLoaded,
                entry.IsEnabled))
            .ToArray();
    }
}


