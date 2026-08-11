using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.DesktopHost;

public sealed class DesktopShellHost : IDesktopShellHost
{
    private readonly Action _initializeAirAppRuntime;
    private readonly Action _initializeTrayIcon;
    private readonly Action<IClassicDesktopStyleApplicationLifetime> _createAndAssignMainWindow;
    private readonly Action _performExitCleanup;
    private readonly Action _startActivationListener;
    private readonly Action _startWeatherRefresh;

    public DesktopShellHost(
        Action initializeAirAppRuntime,
        Action initializeTrayIcon,
        Action<IClassicDesktopStyleApplicationLifetime> createAndAssignMainWindow,
        Action performExitCleanup,
        Action startActivationListener,
        Action startWeatherRefresh)
    {
        _initializeAirAppRuntime = initializeAirAppRuntime;
        _initializeTrayIcon = initializeTrayIcon;
        _createAndAssignMainWindow = createAndAssignMainWindow;
        _performExitCleanup = performExitCleanup;
        _startActivationListener = startActivationListener;
        _startWeatherRefresh = startWeatherRefresh;
    }

    public void Initialize()
    {
        throw new InvalidOperationException("An application instance is required to initialize the desktop shell.");
    }

    public void Initialize(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _initializeAirAppRuntime();
        _initializeTrayIcon();

        if (application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => _performExitCleanup();
            _startActivationListener();
            _createAndAssignMainWindow(desktop);
        }

        _startWeatherRefresh();
    }
}
