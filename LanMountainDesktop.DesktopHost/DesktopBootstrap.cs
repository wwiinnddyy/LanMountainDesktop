using System;
using Avalonia;

namespace LanMountainDesktop.DesktopHost;

public static class DesktopBootstrap
{
    public static void InitializeStartupServices(Action initializeDeviceId, Action initializeCrashReporting, Action initializeUserBehaviorAnalytics, Action scheduleStartupCleanup)
    {
        ArgumentNullException.ThrowIfNull(initializeDeviceId);
        ArgumentNullException.ThrowIfNull(initializeCrashReporting);
        ArgumentNullException.ThrowIfNull(initializeUserBehaviorAnalytics);
        ArgumentNullException.ThrowIfNull(scheduleStartupCleanup);

        initializeDeviceId();
        initializeCrashReporting();
        initializeUserBehaviorAnalytics();
        scheduleStartupCleanup();
    }

    public static void InitializeApplication(Application application, Action initializeShell)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(initializeShell);
        initializeShell();
    }
}
