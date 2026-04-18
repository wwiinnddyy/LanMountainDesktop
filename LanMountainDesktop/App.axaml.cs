using System;
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
using LanMountainDesktop.Services.Launcher;
using LanMountainDesktop.Services.Loading;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Launcher;
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
    private LauncherIpcClient? _launcherIpcClient;
    private LoadingStateManager? _loadingStateManager;
    private LoadingStateReporter? _loadingStateReporter;

    internal static SingleInstanceService? CurrentSingleInstanceService { get; set; }
    internal static IHostApplicationLifecycle? CurrentHostApplicationLifecycle =>
        (Current as App)?._hostApplicationLifecycle;
    internal static INotificationService? CurrentNotificationService =>
        (Current as App)?._notificationService;

    // 隐私政策查看事件
    public static event Action? CurrentPrivacyPolicyViewRequested;

    // 触发隐私政策查看事件的方法
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
            Owner: _mainWindow is { IsVisible: true } ? _mainWindow : null,
            PageId: pageTag));
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
        DesktopBootstrap.InitializeApplication(this, InitializeDesktopShell);

        if (!Design.IsDesignMode && OperatingSystem.IsWindows())
        {
            FusedDesktopManagerServiceFactory.GetOrCreate().Initialize();
        }

        base.OnFrameworkInitializationCompleted();
        
        // IPC 初始化移到窗口创建之后，避免 async void 中的 await 导致窗口创建延迟
        // 使用 fire-and-forget 模式，不阻塞主流程
        _ = InitializeLauncherIpcAsync();
    }
    
    private async Task InitializeLauncherIpcAsync()
    {
        if (!LauncherIpcClient.IsLaunchedByLauncher())
            return;
        
        try
        {
            _launcherIpcClient = new LauncherIpcClient();
            var connected = await _launcherIpcClient.ConnectAsync();
            
            if (connected)
            {
                AppLogger.Info("LauncherIpc", "Connected to Launcher IPC server.");
                
                // 初始化加载状态管理器
                _loadingStateManager = new LoadingStateManager();
                _loadingStateReporter = new LoadingStateReporter(_loadingStateManager, _launcherIpcClient);
                _loadingStateReporter.Start();
                
                // 注册系统初始化加载项
                _loadingStateManager.RegisterItem("system.init", LoadingItemType.System, "系统初始化", "初始化系统核心组件");
                _loadingStateManager.StartItem("system.init", "已连接启动器");
                
                ReportStartupProgress(StartupStage.Initializing, 10, "正在初始化...");
                ReportStartupProgress(StartupStage.LoadingSettings, 20, "正在加载设置...");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to initialize Launcher IPC: {ex.Message}");
        }
    }

    /// <summary>
    /// 向 Launcher 报告启动进度（fire-and-forget，不阻塞主流程）
    /// </summary>
    private void ReportStartupProgress(StartupStage stage, int percent, string message)
    {
        if (_launcherIpcClient is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _launcherIpcClient.ReportProgressAsync(new StartupProgressMessage
                {
                    Stage = stage,
                    ProgressPercent = percent,
                    Message = message
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 向 Launcher 报告关键启动进度，使用后台线程避免阻塞 UI
    /// 用于 Ready 等关键状态报告
    /// </summary>
    private void ReportStartupProgressSync(StartupStage stage, int percent, string message)
    {
        if (_launcherIpcClient is null)
            return;

        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _launcherIpcClient.ReportProgressAsync(new StartupProgressMessage
                    {
                        Stage = stage,
                        ProgressPercent = percent,
                        Message = message
                    });
                    AppLogger.Info("LauncherIpc", $"Successfully reported stage: {stage}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to launch progress report task: {ex.Message}");
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
                ReportStartupProgress(StartupStage.InitializingUI, 60, "正在初始化界面...");
                CreateAndAssignMainWindow(desktop, "FrameworkInitialization");
            },
            () =>
            {
                AppLogger.Info("App", "Desktop lifetime exit triggered.");
                PerformExitCleanup();
            },
            () => CurrentSingleInstanceService?.StartActivationListener(ActivateMainWindow),
            StartWeatherLocationRefreshIfNeeded);
        _desktopShellHost.Initialize(this);
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
        
        // 仅在 Windows 上支持融合桌面功能
        if (!OperatingSystem.IsWindows())
        {
            AppLogger.Warn("FusedDesktop", "Fused desktop is only supported on Windows.");
            return;
        }

        // 切换进入编辑模式，隐藏常态零散的小部件
        FusedDesktopManagerServiceFactory.GetOrCreate().EnterEditMode();
        
        // 确保透明覆盖层窗口存在并显示
        EnsureTransparentOverlayWindow();
        
        // 打开融合桌面组件库窗口
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // 确保覆盖层窗口已显示（组件要渲染在上面，必须先 Show）
                if (_transparentOverlayWindow is not null && !_transparentOverlayWindow.IsVisible)
                {
                    _transparentOverlayWindow.Show();
                }
                
                var window = new FusedDesktopComponentLibraryWindow();
                
                if (_transparentOverlayWindow is not null)
                {
                    window.SetOverlayWindow(_transparentOverlayWindow);
                }
                
                // 当组件库关闭时，退出编辑态
                window.Closed += (s, ev) => 
                {
                    if (_transparentOverlayWindow is not null)
                    {
                        // 触发画布保存，并隐藏画布
                        _transparentOverlayWindow.SaveLayoutAndHide();
                    }
                    
                    // 让管理器根据已存储的最新快照重建生成所有实体小组件
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
        ReportStartupProgress(StartupStage.LoadingPlugins, 30, "正在加载插件...");
        try
        {
            _pluginRuntimeService?.Dispose();
            _pluginRuntimeService = new PluginRuntimeService(_settingsFacade);
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
        }
        catch (Exception ex)
        {
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

        // 仅在 Windows 上支持融合桌面功能
        if (!OperatingSystem.IsWindows())
        {
            _trayComponentLibraryMenuItem.IsVisible = false;
            return;
        }

        // 检查融合桌面功能是否启用
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
                if (_transparentOverlayWindow is not null && _transparentOverlayWindow.IsVisible)
                {
                    _transparentOverlayWindow.Hide();
                }

                var mainWindow = GetOrCreateMainWindow(desktop, source);
                mainWindow.PrepareEnterAnimation();

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
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopShell", $"Failed to restore desktop shell. Source='{source}'.", ex);
            }
        }, DispatcherPriority.Send);
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
                // 清除本地化缓存，强制重新加载语言文件
                _localizationService.ClearCache();
                ApplyCurrentCultureFromSettings();
                RefreshTrayIconContent();
            }

            // 检查融合桌面设置是否变更
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

        _settingsWindowService?.Close();
        if (_settingsPageRegistry is IDisposable disposableRegistry)
        {
            disposableRegistry.Dispose();
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
            ShowInTaskbar = true
        };

        AttachMainWindow(mainWindow);
        desktop.MainWindow = mainWindow;
        AppLogger.Info("App", $"Main window created. Reason='{reason}'. LogFile={AppLogger.LogFilePath}");
        LogBrowserStartupDiagnostics();
        SetDesktopShellState(DesktopShellState.ForegroundDesktop, $"MainWindowCreated:{reason}");
        
        // 延迟报告 Ready 直到窗口实际打开并可见
        // 使用 Opened 事件确保所有资源已加载完毕
        mainWindow.Opened += OnMainWindowOpened;
        
        // 兜底机制：如果 Opened 事件 10 秒内未触发，强制发送 Ready 信号
        // 防止因渲染问题导致 Opened 不触发，启动器 Splash 窗口一直显示
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (_launcherIpcClient is not null && _launcherIpcClient.IsConnected)
            {
                try
                {
                    await _launcherIpcClient.ReportProgressAsync(new StartupProgressMessage
                    {
                        Stage = StartupStage.Ready,
                        ProgressPercent = 100,
                        Message = "就绪"
                    });
                    AppLogger.Warn("App", "Ready signal sent via fallback (Opened event did not fire within 10s)");
                }
                catch { }
            }
        });
        
        return mainWindow;
    }

    /// <summary>
    /// 主窗口打开完成事件 - 此时所有组件、资源及功能模块均已完全加载
    /// </summary>
    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is MainWindow mainWindow)
        {
            mainWindow.Opened -= OnMainWindowOpened;
            
            AppLogger.Info("App", "Main window opened and ready. Reporting Ready to Launcher...");
            
            // 完成系统初始化加载项
            _loadingStateManager?.CompleteItem("system.init", "系统初始化完成");
            
            // 报告 Ready 状态，启动器可以安全关闭 Splash 窗口
            ReportStartupProgressSync(StartupStage.Ready, 100, "就绪");
            
            // 停止加载状态上报
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
            
            // 检查三指滑动功能是否启用
            var appSnapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            if (appSnapshot.EnableThreeFingerSwipe)
            {
                // 显示透明覆盖层窗口
                EnsureTransparentOverlayWindow();
                _transparentOverlayWindow?.Show();
            }
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
        var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        return _localizationService.GetString(languageCode, key, fallback);
    }
}
