using System;
using Avalonia;

namespace LanMountainDesktop.DesktopHost;

public static class DesktopBootstrap
{
    public static void InitializeStartupServices(
        Action initializeTelemetryIdentity,
        Action initializeCrashTelemetry,
        Action initializeUsageTelemetry,
        Action scheduleStartupCleanup)
    {
        ArgumentNullException.ThrowIfNull(initializeTelemetryIdentity);
        ArgumentNullException.ThrowIfNull(initializeCrashTelemetry);
        ArgumentNullException.ThrowIfNull(initializeUsageTelemetry);
        ArgumentNullException.ThrowIfNull(scheduleStartupCleanup);

        initializeTelemetryIdentity();
        initializeCrashTelemetry();
        initializeUsageTelemetry();
        scheduleStartupCleanup();
    }

    public static void InitializeApplication(Application application, Action initializeShell)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(initializeShell);
        initializeShell();
    }
}
