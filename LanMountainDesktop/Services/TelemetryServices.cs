namespace LanMountainDesktop.Services;

public static class TelemetryServices
{
    public static TelemetryIdentityService? Identity { get; set; }

    public static PostHogUsageTelemetryService? Usage { get; set; }

    public static SentryCrashTelemetryService? Crash { get; set; }
}
