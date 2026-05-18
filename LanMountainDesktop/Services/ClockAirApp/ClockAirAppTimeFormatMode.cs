namespace LanMountainDesktop.Services.ClockAirApp;

public static class ClockAirAppTimeFormatMode
{
    public const string System = "system";
    public const string TwentyFourHour = "24h";
    public const string TwelveHour = "12h";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            TwentyFourHour => TwentyFourHour,
            TwelveHour => TwelveHour,
            _ => System
        };
    }
}
