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
    private readonly HostShutdownGate _shutdownGate = new();
    private readonly IDetachedComponentLibraryWindowService _detachedComponentLibraryWindowService = new DetachedComponentLibraryWindowService();
    private readonly ILocationService _locationService = HostLocationServiceProvider.GetOrCreate();
    private readonly DateTimeOffset _startupAt = DateTimeOffset.UtcNow;
    private readonly string _launchSource = LauncherRuntimeMetadata.GetLaunchSource(Environment.GetCommandLineArgs()) ?? "normal";
    private readonly RestartPresentationMode? _requestedRestartPresentationMode =
        LauncherRuntimeMetadata.GetRestartPresentationMode(Environment.GetCommandLineArgs());
    private ISettingsPageRegistry? _settingsPageRegistry;
    private ISettingsWindowService? _settingsWindowService;
    private WeatherLocationRefreshService? _weatherLocationRefreshService;
    private INotificationService? _notificationService;
    private bool _exitCleanupCompleted;
    private DesktopShellState _desktopShellState = DesktopShellState.ForegroundDesktop;
    private ShutdownIntent _shutdownIntent;

    private DesktopTrayService? _desktopTrayService;
    private DispatcherTimer? _shellRecoveryTimer;
    private PluginRuntimeService? _pluginRuntimeService;
    private MainWindow? _mainWindow;
    private TransparentOverlayWindow? _transparentOverlayWindow;
    private FusedDesktopComponentLibraryWindow? _fusedComponentLibraryWindow;
    private bool _mainWindowClosed;
    private bool _uiUnhandledExceptionHooked;
    private DesktopShellHost? _desktopShellHost;
    private PublicIpcHostService? _publicIpcHostService;
    private LoadingStateManager? _loadingStateManager;
    private LoadingStateReporter? _loadingStateReporter;
    private bool _singleInstanceReleased;
    private int _forcedExitScheduled;
    private volatile bool _desktopShellInitializationStarted;
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
    internal bool IsShutdownInProgress => _shutdownGate.IsShutdownRequested || _shutdownIntent != ShutdownIntent.None;
    internal RestartPresentationMode GetCurrentRestartPresentationMode()
    {
        return _desktopShellState switch
        {
            DesktopShellState.TrayOnly => RestartPresentationMode.Tray,
            DesktopShellState.MinimizedToTaskbar => RestartPresentationMode.Minimized,
            _ => RestartPresentationMode.Foreground
        };
    }

    internal void OpenIndependentSettingsModule(string source, string? pageTag = null)
    {
        if (IsShutdownInProgress)
        {
            AppLogger.Info(
                "SettingsFacade",
                $"Settings open ignored because shutdown is in progress. Source='{source}'; PageTag='{pageTag ?? "<default>"}'.");
            return;
        }

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
        CurrentSingleInstanceService?.StartActivationListener(ActivateMainWindow);
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
        _desktopShellInitializationStarted = true;
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
        if (IsShutdownInProgress)
        {
            AppLogger.Info("DesktopShell", "Tray Open Desktop ignored because shutdown is in progress.");
            return;
        }

        RestoreOrCreateMainWindow(showSingleInstanceNotice: false, source: "TrayMenu");
    }

    private void OnTrayRestartClick(object? sender, EventArgs e)
    {
        if (IsShutdownInProgress)
        {
            AppLogger.Info("HostLifecycle", "Tray Restart ignored because shutdown is already in progress.");
            return;
        }

        _ = _hostApplicationLifecycle.TryRestart(new HostApplicationLifecycleRequest(
            Source: "TrayMenu",
            Reason: "User selected Restart App from the tray menu."));
    }

    private void OnTraySettingsClick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsShutdownInProgress)
        {
            AppLogger.Info("SettingsFacade", "Tray Settings ignored because shutdown is in progress.");
            return;
        }

        OpenIndependentSettingsModule("TrayMenu");
    }

    private void OnTrayComponentLibraryClick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (IsShutdownInProgress)
        {
            AppLogger.Info("FusedDesktop", "Tray Component Library ignored because shutdown is in progress.");
            return;
        }
        
        if (!OperatingSystem.IsWindows())
        {
            AppLogger.Warn("FusedDesktop", "Fused desktop is only supported on Windows.");
            return;
        }
        
        Dispatcher.UIThread.Post(() =>
        {
            if (IsShutdownInProgress)
            {
                AppLogger.Info("FusedDesktop", "Deferred Component Library open ignored because shutdown is in progress.");
                return;
            }

            try
            {
                if (_fusedComponentLibraryWindow is { } existingWindow)
                {
                    if (!existingWindow.IsVisible)
                    {
                        existingWindow.Show();
                    }

                    existingWindow.Activate();
                    return;
                }

                var fusedDesktopManager = FusedDesktopManagerServiceFactory.GetOrCreate();
                fusedDesktopManager.EnterEditMode();

                // 纭繚閫忔槑瑕嗙洊灞傜獥鍙ｅ瓨鍦ㄥ苟鏄剧ず
                EnsureTransparentOverlayWindow();
                if (_transparentOverlayWindow is not null && !_transparentOverlayWindow.IsVisible)
                {
                    _transparentOverlayWindow.Show();
                }
                
                var window = new FusedDesktopComponentLibraryWindow();
                _fusedComponentLibraryWindow = window;
                
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
                    fusedDesktopManager.ExitEditMode();
                    if (ReferenceEquals(_fusedComponentLibraryWindow, s))
                    {
                        _fusedComponentLibraryWindow = null;
                    }
                };

                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktop", "Failed to open fused desktop component library.", ex);
                try
                {
                    _transparentOverlayWindow?.SaveLayoutAndHide();
                }
                catch (Exception overlayEx)
                {
                    AppLogger.Warn("FusedDesktop", "Failed to hide fused desktop overlay after library open failure.", overlayEx);
                }

                try
                {
                    FusedDesktopManagerServiceFactory.GetOrCreate().ExitEditMode();
                }
                catch (Exception exitEx)
                {
                    AppLogger.Warn("FusedDesktop", "Failed to exit edit mode after library open failure.", exitEx);
                }

                _fusedComponentLibraryWindow = null;
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
        EnsureDesktopTrayService();
        _desktopTrayService?.StartWatchdog();
        _trayInitialized = _desktopTrayService?.EnsureReady("Startup") == true;
        if (_trayInitialized)
        {
            ReportStartupProgress(StartupStage.TrayReady, 75, "Tray ready.");
            AppLogger.Info("TrayIcon", $"Tray initialized successfully. Pid={Environment.ProcessId}.");
            return;
        }

        AppLogger.Warn("TrayIcon", "Tray initialization did not reach the ready state.");
    }

    private void RefreshTrayIconContent()
    {
        EnsureDesktopTrayService();
        _desktopTrayService?.Refresh("RefreshTrayContent");
        _trayInitialized = _desktopTrayService?.IsReady == true;
    }

    private void RefreshFusedDesktopMenuItemVisibility()
    {
        RefreshTrayIconContent();
    }

    private void DisposeTrayIcon()
    {
        _desktopTrayService?.Dispose();
        _trayInitialized = false;
    }

    private void EnsureDesktopTrayService()
    {
        if (_desktopTrayService is not null)
        {
            return;
        }

        _desktopTrayService = new DesktopTrayService(
            this,
            _appLogoService,
            L,
            ShouldShowTrayComponentLibraryMenuItem,
            OnTrayShowDesktopClick,
            OnTraySettingsClick,
            OnTrayComponentLibraryClick,
            OnTrayRestartClick,
            OnTrayExitClick);
        _desktopTrayService.StateChanged += OnTrayAvailabilityStateChanged;
        _desktopTrayService.StartWatchdog();
        EnsureShellRecoveryWatchdog();
    }

    private void EnsureShellRecoveryWatchdog()
    {
        _shellRecoveryTimer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(10),
            DispatcherPriority.Background,
            OnShellRecoveryWatchdogTick);

        if (!_shellRecoveryTimer.IsEnabled)
        {
            _shellRecoveryTimer.Start();
        }
    }

    private void StopShellRecoveryWatchdog()
    {
        if (_shellRecoveryTimer?.IsEnabled == true)
        {
            _shellRecoveryTimer.Stop();
        }
    }

    private void OnShellRecoveryWatchdogTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (_shutdownIntent != ShutdownIntent.None)
        {
            return;
        }

        EnsureTrayReady("ShellRecoveryWatchdog");

        if (!ShouldShowMainWindowInTaskbar())
        {
            return;
        }

        if (_desktopShellState != DesktopShellState.ForegroundDesktop)
        {
            EnsureTaskbarEntry("ShellRecoveryWatchdog");
            return;
        }

        if (_mainWindow is not null && _mainWindow.IsVisible && !_mainWindow.ShowInTaskbar)
        {
            _mainWindow.ShowInTaskbar = true;
        }
    }

    private bool EnsureTrayReady(string reason)
    {
        EnsureDesktopTrayService();
        var wasReady = _trayInitialized;
        var ready = _desktopTrayService?.EnsureReady(reason) == true;
        _trayInitialized = ready;
        if (ready && !wasReady)
        {
            ReportStartupProgress(StartupStage.TrayReady, 75, "Tray ready.");
        }

        return ready;
    }

    private void OnTrayAvailabilityStateChanged(TrayAvailabilityState state)
    {
        _trayInitialized = state == TrayAvailabilityState.Ready;

        if (state != TrayAvailabilityState.Failed)
        {
            return;
        }

        if (_desktopShellState == DesktopShellState.TrayOnly)
        {
            RestoreOrCreateMainWindow(showSingleInstanceNotice: false, source: "TrayAvailabilityFailed");
            return;
        }

        var foregroundVisible = _mainWindow?.IsVisible == true &&
                                _mainWindow.WindowState != WindowState.Minimized;
        var taskbarUsable = BuildPublicTaskbarStatus().IsUsable;
        if (!foregroundVisible &&
            !taskbarUsable &&
            (_desktopTrayService?.ConsecutiveRecoveryFailures ?? 0) >= 3)
        {
            RestoreOrCreateMainWindow(showSingleInstanceNotice: false, source: "TrayAvailabilityRepeatedFailure");
        }
    }

    private bool ShouldShowTrayComponentLibraryMenuItem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        return appSnapshot.EnableFusedDesktop;
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

    internal void ActivateMainWindow()
    {
        AppLogger.Info("SingleInstance", $"Activation callback received. Pid={Environment.ProcessId}.");

        if (!_desktopShellInitializationStarted && _mainWindow is null)
        {
            AppLogger.Info("SingleInstance", "Activation acknowledged while desktop shell is still initializing.");
            return;
        }

        try
        {
            var restored = Dispatcher.UIThread.CheckAccess()
                ? RestoreOrCreateMainWindowCore(showSingleInstanceNotice: true, source: "SingleInstance")
                : Dispatcher.UIThread.InvokeAsync(
                    () => RestoreOrCreateMainWindowCore(showSingleInstanceNotice: true, source: "SingleInstance"),
                    DispatcherPriority.Send).GetAwaiter().GetResult();

            if (!restored)
            {
                AppLogger.Warn("SingleInstance", "Activation callback could not restore the main window yet.");
                return;
            }

            AppLogger.Info("SingleInstance", "Activation callback completed successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("SingleInstance", "Activation callback failed while restoring the desktop shell.", ex);
        }
    }

    private void RestoreOrCreateMainWindow(bool showSingleInstanceNotice, string source)
    {
        if (IsShutdownInProgress)
        {
            AppLogger.Info("DesktopShell", $"Restore ignored because shutdown is in progress. Source='{source}'.");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _ = RestoreOrCreateMainWindowCore(showSingleInstanceNotice, source);
        }, DispatcherPriority.Send);
    }

    private bool RestoreOrCreateMainWindowCore(bool showSingleInstanceNotice, string source)
    {
        if (IsShutdownInProgress)
        {
            AppLogger.Info("DesktopShell", $"Restore skipped because shutdown is in progress. Source='{source}'.");
            return false;
        }

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

            mainWindow.EnsureForegroundWindowLayout();

            if (mainWindow.ShouldUseFullscreenWindow() &&
                mainWindow.WindowState != WindowState.FullScreen)
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

    internal bool TrySubmitShutdown(HostShutdownMode mode, HostApplicationLifecycleRequest? request)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLogger.Warn(
                "HostLifecycle",
                $"Shutdown request ignored because desktop lifetime is unavailable. Mode='{mode}'; Source='{request?.Source ?? "Unknown"}'.");
            return false;
        }

        return Dispatcher.UIThread.CheckAccess()
            ? TrySubmitShutdownCore(mode, request, desktop)
            : Dispatcher.UIThread.InvokeAsync(
                () => TrySubmitShutdownCore(mode, request, desktop),
                DispatcherPriority.Send).GetAwaiter().GetResult();
    }

    private bool TrySubmitShutdownCore(
        HostShutdownMode mode,
        HostApplicationLifecycleRequest? request,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var source = request?.Source ?? "Unknown";
        var submission = _shutdownGate.Submit(mode);
        if (!submission.IsFirstSubmission)
        {
            AppLogger.Warn(
                "HostLifecycle",
                $"Shutdown request ignored because shutdown is already in progress. Requested='{submission.RequestedMode}'; Effective='{submission.EffectiveMode}'; Source='{source}'.");
            return submission.Accepted;
        }

        _shutdownIntent = mode == HostShutdownMode.Restart
            ? ShutdownIntent.RestartRequested
            : ShutdownIntent.ExitRequested;
        AppLogger.Info(
            "DesktopShell",
            $"Shutdown committed. Intent='{_shutdownIntent}'; Source='{source}'; Reason='{request?.Reason ?? string.Empty}'; CurrentShellState='{_desktopShellState}'.");

        ScheduleForcedProcessTermination($"ShutdownRequest:{source}");
        StopShellRecoveryWatchdog();
        PerformExitCleanup();
        ReleaseSingleInstanceAfterExit($"ShutdownRequest:{source}");

        try
        {
            desktop.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("HostLifecycle", $"Desktop lifetime shutdown failed. Source='{source}'.", ex);
            return true;
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
                RefreshTrayIconContent();
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

            var showInTaskbarChanged =
                refreshAll ||
                changedKeys.Contains(nameof(AppSettingsSnapshot.ShowInTaskbar), StringComparer.OrdinalIgnoreCase);

            if (showInTaskbarChanged)
            {
                EnsureTrayReady("SettingsChanged");
                if (ShouldShowMainWindowInTaskbar())
                {
                    EnsureTaskbarEntry("SettingsChanged");
                }
                else if (_mainWindow is not null && _mainWindow.IsVisible)
                {
                    _mainWindow.ShowInTaskbar = false;
                }
            }
        }, DispatcherPriority.Background);
    }

    private void OnAppearanceThemeChanged(object? sender, AppearanceThemeSnapshot e)
    {
        _ = sender;
        _ = e;

        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeFromSettings();
            RefreshTrayIconContent();
        }, DispatcherPriority.Background);
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
        StopShellRecoveryWatchdog();
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

        if (_fusedComponentLibraryWindow is not null)
        {
            try
            {
                _fusedComponentLibraryWindow.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("FusedDesktop", "Failed to close fused desktop component library during shutdown.", ex);
            }
            finally
            {
                _fusedComponentLibraryWindow = null;
                try
                {
                    FusedDesktopManagerServiceFactory.GetOrCreate().ExitEditMode();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("FusedDesktop", "Failed to exit fused desktop edit mode during shutdown.", ex);
                }
            }
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
            _loadingStateManager?.CompleteItem("system.init", "System initialization completed.");

            if (TryApplyStartupPresentation(mainWindow))
            {
                AppLogger.Info(
                    "App",
                    $"Main window opened and startup presentation was applied. LaunchSource='{_launchSource}'; RestartPresentation='{_requestedRestartPresentationMode?.ToString() ?? "<none>"}'; ShellState='{_desktopShellState}'.");
                ReportStartupProgressSync(StartupStage.Ready, 100, "Ready.");
                _loadingStateReporter?.Stop();
                return;
            }

            AppLogger.Info(
                "App",
                $"Main window opened. Reporting DesktopVisible. TrayInitialized={_trayInitialized}; ShellState='{_desktopShellState}'.");

            ReportStartupProgressSync(StartupStage.DesktopVisible, 100, "Desktop visible.");
            ReportStartupProgressSync(StartupStage.Ready, 100, "Ready.");
            _loadingStateReporter?.Stop();
        }
    }

    private bool TryApplyStartupPresentation(MainWindow mainWindow)
    {
        if (!string.Equals(_launchSource, "restart", StringComparison.OrdinalIgnoreCase) ||
            _requestedRestartPresentationMode is null ||
            _requestedRestartPresentationMode == RestartPresentationMode.Foreground)
        {
            return false;
        }

        switch (_requestedRestartPresentationMode)
        {
            case RestartPresentationMode.Minimized:
                mainWindow.ShowInTaskbar = true;
                mainWindow.WindowState = WindowState.Minimized;
                SetDesktopShellState(DesktopShellState.MinimizedToTaskbar, "StartupRestartPresentation");
                ReportStartupProgressSync(StartupStage.BackgroundReady, 95, "Background ready.");
                return true;

            case RestartPresentationMode.Tray:
                HideMainWindowToTray(mainWindow, "StartupRestartPresentation");
                return true;

            default:
                return false;
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
            if (EnsureTrayReady("MainWindowClosedUnexpected"))
            {
                _desktopTrayService?.StartWatchdog();
                SetDesktopShellState(DesktopShellState.TrayOnly, "MainWindowClosedUnexpected");
            }
            else
            {
                SetDesktopShellState(DesktopShellState.ForegroundDesktop, "MainWindowClosedUnexpectedWithoutTray");
            }
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
            if (ShouldShowMainWindowInTaskbar())
            {
                EnsureTrayReady($"TaskbarBackground:{source}");
                mainWindow.ShowInTaskbar = true;
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }

                mainWindow.WindowState = WindowState.Minimized;
                SetDesktopShellState(DesktopShellState.MinimizedToTaskbar, source);
                ReportStartupProgress(StartupStage.BackgroundReady, 95, "Background ready via taskbar.");
                AppLogger.Info(
                    "DesktopShell",
                    $"Main window minimized to taskbar because taskbar entry is enabled. Source='{source}'.");
                return;
            }

            if (!EnsureTrayReady($"HideToTray:{source}"))
            {
                RecoverFromTrayUnavailable(mainWindow, source);
                return;
            }

            mainWindow.ShowInTaskbar = false;
            mainWindow.Hide();
            SetDesktopShellState(DesktopShellState.TrayOnly, source);
            ReportStartupProgress(StartupStage.BackgroundReady, 95, "Background ready.");
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
            RecoverFromTrayUnavailable(mainWindow, source);
        }
    }

    private void RecoverFromTrayUnavailable(MainWindow mainWindow, string source)
    {
        AppLogger.Warn(
            "DesktopShell",
            $"Tray was unavailable. Recovering to a visible or taskbar-backed state instead of TrayOnly. Source='{source}'.");

        var showInTaskbar = ShouldShowMainWindowInTaskbar();
        if (showInTaskbar)
        {
            mainWindow.ShowInTaskbar = true;
            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            mainWindow.WindowState = WindowState.Minimized;
            SetDesktopShellState(DesktopShellState.MinimizedToTaskbar, $"TrayFallbackTaskbar:{source}");
            ReportStartupProgress(StartupStage.BackgroundReady, 95, "Background ready via taskbar fallback.");
            return;
        }

        mainWindow.ShowInTaskbar = true;
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.EnsureForegroundWindowLayout();

        if (mainWindow.ShouldUseFullscreenWindow() &&
            mainWindow.WindowState != WindowState.FullScreen)
        {
            mainWindow.WindowState = WindowState.FullScreen;
        }

        mainWindow.Activate();
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;
        SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"TrayFallbackForeground:{source}");
        ReportStartupProgress(StartupStage.DesktopVisible, 100, "Desktop restored because tray was unavailable.");
    }

    private bool ShouldShowMainWindowInTaskbar()
    {
        return _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App).ShowInTaskbar;
    }

    private bool EnsureTaskbarEntry(string source)
    {
        if (IsShutdownInProgress)
        {
            AppLogger.Info("DesktopShell", $"Taskbar repair skipped because shutdown is in progress. Source='{source}'.");
            return false;
        }

        if (!ShouldShowMainWindowInTaskbar())
        {
            return false;
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLogger.Warn("DesktopShell", $"Taskbar repair skipped because desktop lifetime is unavailable. Source='{source}'.");
            return false;
        }

        try
        {
            var mainWindow = GetOrCreateMainWindow(desktop, $"TaskbarRepair:{source}");
            mainWindow.ShowInTaskbar = true;

            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (_desktopShellState != DesktopShellState.ForegroundDesktop)
            {
                mainWindow.WindowState = WindowState.Minimized;
                SetDesktopShellState(DesktopShellState.MinimizedToTaskbar, $"TaskbarRepair:{source}");
                ReportStartupProgress(StartupStage.BackgroundReady, 95, "Background ready via taskbar repair.");
            }

            AppLogger.Info(
                "DesktopShell",
                $"Taskbar entry ensured. Source='{source}'; IsVisible={mainWindow.IsVisible}; ShowInTaskbar={mainWindow.ShowInTaskbar}; WindowState='{mainWindow.WindowState}'.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DesktopShell", $"Failed to ensure taskbar entry. Source='{source}'.", ex);
            return false;
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
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        return _localizationService.GetString(languageCode, key, fallback);
    }

    internal bool TryActivateMainWindowFromExternalIpc(string source)
    {
        return TryActivateMainWindowWithStatusFromExternalIpc(source).Accepted;
    }

    internal PublicShellActivationResult TryActivateMainWindowWithStatusFromExternalIpc(string source)
    {
        if (!_desktopShellInitializationStarted && _mainWindow is null)
        {
            return new PublicShellActivationResult(
                false,
                "startup_pending",
                "Desktop process is running, but the shell has not started yet.",
                GetPublicShellStatus());
        }

        var restored = RestoreOrCreateMainWindowCore(showSingleInstanceNotice: false, source);
        var status = GetPublicShellStatus();
        if (restored)
        {
            return new PublicShellActivationResult(true, "activated", "Desktop window activation was requested.", status);
        }

        if (IsShutdownInProgress)
        {
            return new PublicShellActivationResult(false, "shutdown_in_progress", "Desktop is shutting down.", status);
        }

        var code = status.PublicIpcReady && (!status.MainWindowCreated || !status.MainWindowOpened)
            ? "startup_pending"
            : status.PublicIpcReady && !status.DesktopVisible
                ? "shell_not_ready"
                : "activation_failed";
        var message = code switch
        {
            "startup_pending" => "Desktop process is running, but the shell is still creating the main window.",
            "shell_not_ready" => "Desktop process is running, but the shell is not ready for activation yet.",
            _ => "Desktop window activation failed."
        };
        return new PublicShellActivationResult(false, code, message, status);
    }

    internal PublicTrayStatus EnsureTrayReadyFromExternalIpc(string source)
    {
        EnsureTrayReady($"ExternalIpc:{source}");
        return BuildPublicTrayStatus();
    }

    internal PublicTaskbarStatus EnsureTaskbarEntryFromExternalIpc(string source)
    {
        EnsureTaskbarEntry($"ExternalIpc:{source}");
        return BuildPublicTaskbarStatus();
    }

    internal PublicShellStatus GetPublicShellStatus()
    {
        return new PublicShellStatus(
            Environment.ProcessId,
            _startupAt,
            _launchSource,
            _desktopShellState.ToString(),
            _mainWindow is not null && !_mainWindowClosed,
            _mainWindow?.IsVisible == true,
            _mainWindowOpened,
            _mainWindow?.IsVisible == true && _mainWindow.WindowState != WindowState.Minimized,
            _publicIpcHostService is not null,
            BuildPublicTrayStatus(),
            BuildPublicTaskbarStatus());
    }

    private PublicTrayStatus BuildPublicTrayStatus()
    {
        return new PublicTrayStatus(
            _desktopTrayService?.State.ToString() ?? TrayAvailabilityState.Unavailable.ToString(),
            _desktopTrayService?.IsReady == true,
            _desktopTrayService?.HasIcon == true,
            _desktopTrayService?.HasMenu == true,
            _desktopTrayService?.IsVisible == true,
            _desktopTrayService?.ConsecutiveRecoveryFailures ?? 0);
    }

    private PublicTaskbarStatus BuildPublicTaskbarStatus()
    {
        var requested = ShouldShowMainWindowInTaskbar();
        var mainWindowExists = _mainWindow is not null && !_mainWindowClosed;
        var showInTaskbar = _mainWindow?.ShowInTaskbar == true;
        var visible = _mainWindow?.IsVisible == true;
        var minimized = _mainWindow?.WindowState == WindowState.Minimized;

        return new PublicTaskbarStatus(
            requested,
            mainWindowExists,
            showInTaskbar,
            visible,
            minimized,
            requested && mainWindowExists && showInTaskbar && visible);
    }

    private void InitializePublicIpc()
    {
        if (_publicIpcHostService is not null)
        {
            return;
        }

        try
        {
            var versionInfo = AppVersionProvider.ResolveForCurrentProcess();
            _publicIpcHostService = new PublicIpcHostService();
            _publicIpcHostService.PluginDescriptorProvider = BuildPublicPluginDescriptors;
            _publicIpcHostService.RegisterPublicService<IPublicAppInfoService>(
                new PublicAppInfoService(_startupAt));
            _publicIpcHostService.RegisterPublicService<IPublicShellControlService>(
                new PublicShellControlService());
            _publicIpcHostService.RegisterPublicService<IPublicPluginCatalogService>(
                new PublicPluginCatalogService(_publicIpcHostService));
            _publicIpcHostService.Start();
            AppLogger.Info(
                "PublicIpc",
                $"Public IPC host started. PipeName='{IpcConstants.DefaultPipeName}'; Version='{versionInfo.Version}'; Codename='{versionInfo.Codename}'.");
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


