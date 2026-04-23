using System.Text.Json;

namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum StartupVisualMode
{
    Fade,
    StaticSplash,
    SlideSplash
}

public readonly record struct StartupVisualPreferences(
    bool EnableFadeTransition,
    bool EnableSlideTransition)
{
    public static StartupVisualPreferences Default => new(true, false);

    public StartupVisualPreferences Normalize()
    {
        if (EnableSlideTransition)
        {
            return new StartupVisualPreferences(false, true);
        }

        return new StartupVisualPreferences(EnableFadeTransition, false);
    }

    public StartupVisualMode Mode => Normalize() switch
    {
        { EnableSlideTransition: true } => StartupVisualMode.SlideSplash,
        { EnableFadeTransition: false } => StartupVisualMode.StaticSplash,
        _ => StartupVisualMode.Fade
    };
}

public static class StartupVisualPreferencesResolver
{
    public static StartupVisualPreferences Resolve(string? settingsPath = null)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(settingsPath)
            ? GetDefaultSettingsPath()
            : settingsPath!;

        if (!File.Exists(resolvedPath))
        {
            return StartupVisualPreferences.Default;
        }

        try
        {
            using var stream = File.OpenRead(resolvedPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var enableFade = TryGetBoolean(root, "enableFadeTransition") ?? true;
            var enableSlide = TryGetBoolean(root, "enableSlideTransition") ?? false;
            return FromFlags(enableFade, enableSlide);
        }
        catch
        {
            return StartupVisualPreferences.Default;
        }
    }

    public static StartupVisualPreferences FromFlags(bool enableFadeTransition, bool enableSlideTransition)
    {
        return new StartupVisualPreferences(enableFadeTransition, enableSlideTransition).Normalize();
    }

    public static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LanMountainDesktop", "settings.json");
    }

    private static bool? TryGetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }
}
