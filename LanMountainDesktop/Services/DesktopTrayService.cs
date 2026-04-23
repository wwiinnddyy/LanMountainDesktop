using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

internal enum TrayAvailabilityState
{
    Unavailable = 0,
    Initializing = 1,
    Ready = 2,
    Recovering = 3,
    Failed = 4
}

internal sealed class DesktopTrayService : IDisposable
{
    private readonly Application _application;
    private readonly IAppLogoService _appLogoService;
    private readonly Func<string, string, string> _localize;
    private readonly Func<bool> _shouldShowComponentLibraryMenuItem;
    private readonly EventHandler _onShowDesktop;
    private readonly EventHandler _onSettings;
    private readonly EventHandler _onComponentLibrary;
    private readonly EventHandler _onRestart;
    private readonly EventHandler _onExit;
    private readonly DispatcherTimer _watchdogTimer;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showDesktopMenuItem;
    private NativeMenuItem? _settingsMenuItem;
    private NativeMenuItem? _componentLibraryMenuItem;
    private NativeMenuItem? _restartMenuItem;
    private NativeMenuItem? _exitMenuItem;
    private int _consecutiveRecoveryFailures;
    private bool _disposed;

    public DesktopTrayService(
        Application application,
        IAppLogoService appLogoService,
        Func<string, string, string> localize,
        Func<bool> shouldShowComponentLibraryMenuItem,
        EventHandler onShowDesktop,
        EventHandler onSettings,
        EventHandler onComponentLibrary,
        EventHandler onRestart,
        EventHandler onExit)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _appLogoService = appLogoService ?? throw new ArgumentNullException(nameof(appLogoService));
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
        _shouldShowComponentLibraryMenuItem = shouldShowComponentLibraryMenuItem ?? throw new ArgumentNullException(nameof(shouldShowComponentLibraryMenuItem));
        _onShowDesktop = onShowDesktop ?? throw new ArgumentNullException(nameof(onShowDesktop));
        _onSettings = onSettings ?? throw new ArgumentNullException(nameof(onSettings));
        _onComponentLibrary = onComponentLibrary ?? throw new ArgumentNullException(nameof(onComponentLibrary));
        _onRestart = onRestart ?? throw new ArgumentNullException(nameof(onRestart));
        _onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

        _watchdogTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, OnWatchdogTick);
    }

    public TrayAvailabilityState State { get; private set; } = TrayAvailabilityState.Unavailable;

    public bool IsReady => State == TrayAvailabilityState.Ready;

    public bool HasIcon => _trayIcon?.Icon is not null;

    public bool HasMenu => _trayIcon?.Menu is not null;

    public bool IsVisible => _trayIcon?.IsVisible == true;

    public int ConsecutiveRecoveryFailures => _consecutiveRecoveryFailures;

    public event Action<TrayAvailabilityState>? StateChanged;

    public bool EnsureReady(string reason)
    {
        if (HasHealthyTray())
        {
            _consecutiveRecoveryFailures = 0;
            SetState(TrayAvailabilityState.Ready, reason);
            return true;
        }

        return TryCreateOrRefreshTray(reason, isRecoveryAttempt: State != TrayAvailabilityState.Unavailable);
    }

    public void Refresh(string reason)
    {
        if (!EnsureReady(reason))
        {
            return;
        }

        ApplyTrayContent();
    }

    public void StartWatchdog()
    {
        if (!_watchdogTimer.IsEnabled)
        {
            _watchdogTimer.Start();
        }
    }

    public void StopWatchdog()
    {
        if (_watchdogTimer.IsEnabled)
        {
            _watchdogTimer.Stop();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopWatchdog();

        try
        {
            if (_trayIcon is not null)
            {
                _trayIcon.IsVisible = false;
            }
        }
        catch
        {
        }

        SetState(TrayAvailabilityState.Unavailable, "Dispose");
    }

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (_disposed || State == TrayAvailabilityState.Unavailable)
        {
            return;
        }

        if (HasHealthyTray())
        {
            return;
        }

        TryCreateOrRefreshTray("Watchdog", isRecoveryAttempt: true);
    }

    private bool TryCreateOrRefreshTray(string reason, bool isRecoveryAttempt)
    {
        try
        {
            SetState(
                isRecoveryAttempt ? TrayAvailabilityState.Recovering : TrayAvailabilityState.Initializing,
                reason);

            EnsureTrayObjects();
            ApplyTrayContent();
            TrayIcon.SetIcons(_application, [_trayIcon!]);

            if (!HasHealthyTray())
            {
                throw new InvalidOperationException("Tray icon did not reach a healthy state after initialization.");
            }

            _consecutiveRecoveryFailures = 0;
            SetState(TrayAvailabilityState.Ready, reason);
            return true;
        }
        catch (Exception ex)
        {
            _consecutiveRecoveryFailures++;
            SetState(TrayAvailabilityState.Failed, $"{reason}:{ex.GetType().Name}");
            AppLogger.Warn("TrayIcon", $"Tray initialization/recovery failed. Reason='{reason}'. Attempt={_consecutiveRecoveryFailures}.", ex);
            return false;
        }
    }

    private void EnsureTrayObjects()
    {
        _showDesktopMenuItem ??= CreateMenuItem(_onShowDesktop);
        _settingsMenuItem ??= CreateMenuItem(_onSettings);
        _componentLibraryMenuItem ??= CreateMenuItem(_onComponentLibrary);
        _restartMenuItem ??= CreateMenuItem(_onRestart);
        _exitMenuItem ??= CreateMenuItem(_onExit);

        if (_trayIcon is null)
        {
            var trayMenu = new NativeMenu();
            trayMenu.Items.Add(_showDesktopMenuItem);
            trayMenu.Items.Add(_settingsMenuItem);
            trayMenu.Items.Add(_componentLibraryMenuItem);
            trayMenu.Items.Add(new NativeMenuItemSeparator());
            trayMenu.Items.Add(_restartMenuItem);
            trayMenu.Items.Add(new NativeMenuItemSeparator());
            trayMenu.Items.Add(_exitMenuItem);

            _trayIcon = new TrayIcon
            {
                Menu = trayMenu
            };
        }
    }

    private void ApplyTrayContent()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Icon = _appLogoService.CreateTrayIcon();
        _trayIcon.IsVisible = true;
        if (!OperatingSystem.IsLinux())
        {
            _trayIcon.ToolTipText = _localize("tray.tooltip", "LanMountainDesktop");
        }

        if (_showDesktopMenuItem is not null)
        {
            _showDesktopMenuItem.Header = _localize("tray.menu.show_desktop", "Open Desktop");
        }

        if (_settingsMenuItem is not null)
        {
            _settingsMenuItem.Header = _localize("tray.menu.settings", "Settings");
        }

        if (_componentLibraryMenuItem is not null)
        {
            _componentLibraryMenuItem.IsVisible = _shouldShowComponentLibraryMenuItem();
            if (_componentLibraryMenuItem.IsVisible)
            {
                _componentLibraryMenuItem.Header = _localize("tray.menu.component_library", "Component Library");
            }
        }

        if (_restartMenuItem is not null)
        {
            _restartMenuItem.Header = _localize("tray.menu.restart", "Restart App");
        }

        if (_exitMenuItem is not null)
        {
            _exitMenuItem.Header = _localize("tray.menu.exit", "Exit App");
        }
    }

    private bool HasHealthyTray()
    {
        return _trayIcon is not null &&
               _trayIcon.Menu is not null &&
               _trayIcon.Icon is not null &&
               _trayIcon.IsVisible &&
               _showDesktopMenuItem is not null &&
               _settingsMenuItem is not null &&
               _componentLibraryMenuItem is not null &&
               _restartMenuItem is not null &&
               _exitMenuItem is not null;
    }

    private void SetState(TrayAvailabilityState state, string reason)
    {
        if (State == state)
        {
            if (state == TrayAvailabilityState.Failed)
            {
                StateChanged?.Invoke(state);
            }

            return;
        }

        var previous = State;
        State = state;
        AppLogger.Info("TrayIcon", $"Tray availability changed. Previous='{previous}'; Current='{state}'; Reason='{reason}'.");
        StateChanged?.Invoke(state);
    }

    private static NativeMenuItem CreateMenuItem(EventHandler clickHandler)
    {
        var item = new NativeMenuItem();
        item.Click += clickHandler;
        return item;
    }
}
