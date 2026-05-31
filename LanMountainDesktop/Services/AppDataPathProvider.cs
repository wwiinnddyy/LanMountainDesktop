namespace LanMountainDesktop.Services;

public static class AppDataPathProvider
{
    private static string? _overriddenDataRoot;

    public static void Initialize(string[] args)
    {
        var dataRoot = ResolveDataRootFromArgs(args);
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            _overriddenDataRoot = Path.GetFullPath(dataRoot);
            AppLogger.Info("AppDataPath", $"Data root overridden by launcher: '{_overriddenDataRoot}'.");
        }
        else
        {
            var envDataRoot = Environment.GetEnvironmentVariable("LMD_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(envDataRoot))
            {
                _overriddenDataRoot = Path.GetFullPath(envDataRoot);
                AppLogger.Info("AppDataPath", $"Data root overridden by environment variable: '{_overriddenDataRoot}'.");
            }
        }
    }

    public static string GetDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(_overriddenDataRoot))
        {
            return _overriddenDataRoot;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
    }

    public static string GetSettingsDirectory()
    {
        return GetDataRoot();
    }

    public static string GetPluginMarketDirectory()
    {
        return Path.Combine(GetDataRoot(), "PluginMarket");
    }

    public static string GetWallpapersDirectory()
    {
        return Path.Combine(GetDataRoot(), "Wallpapers");
    }

    internal static void ResetForTests()
    {
        _overriddenDataRoot = null;
    }

    private static string? ResolveDataRootFromArgs(string[] args)
    {
        const string prefix = "--data-root=";
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }

            if (string.Equals(arg, "--data-root", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
