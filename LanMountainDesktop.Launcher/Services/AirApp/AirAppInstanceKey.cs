namespace LanMountainDesktop.Launcher.Services.AirApp;

internal static class AirAppInstanceKey
{
    public static string Build(string appId, string? sourceComponentId, string? sourcePlacementId)
    {
        var normalizedAppId = Normalize(appId, "unknown");
        var normalizedComponentId = Normalize(sourceComponentId, "none");
        var normalizedPlacementId = Normalize(sourcePlacementId, "none");
        return $"{normalizedAppId}:{normalizedComponentId}:{normalizedPlacementId}";
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
