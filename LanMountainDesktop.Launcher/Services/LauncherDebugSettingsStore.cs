namespace LanMountainDesktop.Launcher.Services;

internal sealed record LauncherDebugSettings(bool DevModeEnabled, string? CustomHostPath);

internal static class LauncherDebugSettingsStore
{
    private const string DevModeFileName = "dev-mode.flag";
    private const string CustomHostPathFileName = "custom-host-path.txt";
    private const string LegacyDevModeFileName = "devmode.config";
    private const string LegacyCustomHostPathFileName = "custom-host-path.config";

    internal static string? ConfigBaseDirectoryOverride { get; set; }

    public static string ConfigBaseDirectory => ConfigBaseDirectoryOverride ?? ResolveConfigBaseDirectory();

    public static LauncherDebugSettings Load()
    {
        return new LauncherDebugSettings(
            LoadDevModeState(),
            LoadCustomHostPath());
    }

    public static bool IsDevModeEnabled() => Load().DevModeEnabled;

    public static string? GetSavedCustomHostPath() => Load().CustomHostPath;

    public static void Save(LauncherDebugSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigBaseDirectory);
            File.WriteAllText(GetPath(DevModeFileName), settings.DevModeEnabled.ToString());
            File.WriteAllText(GetPath(CustomHostPathFileName), settings.CustomHostPath ?? string.Empty);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to save launcher debug settings: {ex.Message}");
        }
    }

    public static void SaveDevModeState(bool enabled)
    {
        var current = Load();
        Save(current with { DevModeEnabled = enabled });
    }

    public static void SaveCustomHostPath(string? customHostPath)
    {
        var current = Load();
        Save(current with { CustomHostPath = customHostPath });
    }

    private static bool LoadDevModeState()
    {
        var newValue = TryReadText(GetPath(DevModeFileName));
        if (!string.IsNullOrWhiteSpace(newValue))
        {
            return TryParseDevMode(newValue);
        }

        var legacyValue = TryReadText(GetPath(LegacyDevModeFileName));
        return !string.IsNullOrWhiteSpace(legacyValue) && TryParseDevMode(legacyValue);
    }

    private static string? LoadCustomHostPath()
    {
        var newValue = TryReadText(GetPath(CustomHostPathFileName));
        if (!string.IsNullOrWhiteSpace(newValue))
        {
            return newValue.Trim();
        }

        var legacyValue = TryReadText(GetPath(LegacyCustomHostPathFileName));
        return string.IsNullOrWhiteSpace(legacyValue) ? null : legacyValue.Trim();
    }

    private static bool TryParseDevMode(string value)
    {
        var normalized = value.Trim();
        return normalized == "1" ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to read launcher debug setting '{path}': {ex.Message}");
            return null;
        }
    }

    private static string GetPath(string fileName) => Path.Combine(ConfigBaseDirectory, fileName);

    private static string ResolveConfigBaseDirectory()
    {
        try
        {
            var appRoot = Commands.ResolveAppRoot(CommandContext.FromArgs([]));
            var resolver = new DataLocationResolver(appRoot);
            return resolver.ResolveLauncherDataPath();
        }
        catch
        {
        }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, "LanMountainDesktop", "Launcher");
            }
        }
        catch
        {
        }

        try
        {
            return Path.Combine(AppContext.BaseDirectory, "Launcher");
        }
        catch
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "Launcher");
        }
    }
}
