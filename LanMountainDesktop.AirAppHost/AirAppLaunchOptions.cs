namespace LanMountainDesktop.AirAppHost;

public sealed record AirAppLaunchOptions(
    string AppId,
    string SessionId,
    string? SourceComponentId,
    string? SourcePlacementId,
    string? LauncherPipeName,
    string? InstanceKey,
    string? DataRoot)
{
    public const string WorldClockAppId = "world-clock";
    public const string WhiteboardAppId = "whiteboard";

    public static AirAppLaunchOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var equalsIndex = key.IndexOf('=');
            if (equalsIndex > 0)
            {
                var inlineValue = key[(equalsIndex + 1)..];
                key = key[..equalsIndex].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = inlineValue;
                }

                continue;
            }

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[index + 1];
                index++;
            }
            else
            {
                values[key] = "true";
            }
        }

        return new AirAppLaunchOptions(
            GetValue(values, "app-id", WorldClockAppId),
            GetValue(values, "session-id", Guid.NewGuid().ToString("N")),
            GetOptionalValue(values, "source-component-id"),
            GetOptionalValue(values, "source-placement-id"),
            GetOptionalValue(values, "launcher-pipe"),
            GetOptionalValue(values, "instance-key"),
            GetOptionalValue(values, "data-root"));
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static string? GetOptionalValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }
}
