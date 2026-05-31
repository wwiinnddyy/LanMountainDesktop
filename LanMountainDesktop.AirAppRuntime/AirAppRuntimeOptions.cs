using System.Globalization;

namespace LanMountainDesktop.AirAppRuntime;

internal sealed record AirAppRuntimeOptions(
    string? AppRoot,
    string? DataRoot,
    int LauncherProcessId,
    int RequesterProcessId)
{
    public static AirAppRuntimeOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var equalsIndex = key.IndexOf('=');
            if (equalsIndex >= 0)
            {
                values[key[..equalsIndex]] = key[(equalsIndex + 1)..];
                continue;
            }

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++index];
            }
            else
            {
                values[key] = "true";
            }
        }

        return new AirAppRuntimeOptions(
            GetOptionalPath(values, "app-root"),
            GetOptionalPath(values, "data-root"),
            GetInt(values, "launcher-pid"),
            GetInt(values, "requester-pid"));
    }

    private static string? GetOptionalPath(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : null;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
