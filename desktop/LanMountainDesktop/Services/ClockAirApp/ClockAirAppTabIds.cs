namespace LanMountainDesktop.Services.ClockAirApp;

public static class ClockAirAppTabIds
{
    public const string Last = "last";
    public const string WorldClock = "world";
    public const string Stopwatch = "stopwatch";
    public const string Timer = "timer";
    public const string Settings = "settings";

    public static string Normalize(string? value, string fallback = WorldClock)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Last => Last,
            WorldClock => WorldClock,
            Stopwatch => Stopwatch,
            Timer => Timer,
            Settings => Settings,
            _ => fallback
        };
    }
}
