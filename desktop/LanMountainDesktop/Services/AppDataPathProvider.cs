using LanMountainDesktop.Shared.Data;

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

        MigrateLegacyMarketDirectory();
    }

    /// <summary>
    /// 市场目录在 AirApp 改名时从 PluginMarket 变成了 AirAppMarket，但当时没有搬旧数据，
    /// 于是已安装实例里留下一份再也读不到的 PluginMarket。这里把它改名归位。
    /// </summary>
    internal static void MigrateLegacyMarketDirectory()
    {
        var root = GetDataRoot();

        TryMoveLegacyPath(
            Path.Combine(root, "PluginMarket"),
            Path.Combine(root, "AirAppMarket"),
            "market directory");
    }

    /// <summary>
    /// 把遗留的磁盘名（文件或目录）改名归位：只改名、不合并、不删除。
    /// 两侧同时存在时保持原样并告警，因为无法判断哪一份才是用户要的。
    /// 幂等——改名后旧路径不再存在，重复调用是空操作。
    /// </summary>
    internal static bool TryMoveLegacyPath(string legacyPath, string currentPath, string description)
    {
        if (!Directory.Exists(legacyPath) && !File.Exists(legacyPath))
        {
            return false;
        }

        if (Directory.Exists(currentPath) || File.Exists(currentPath))
        {
            AppLogger.Warn(
                "AppDataPath",
                $"Both legacy and current {description} exist ('{legacyPath}' and '{currentPath}'); leaving the legacy one untouched.");
            return false;
        }

        try
        {
            if (Directory.Exists(legacyPath))
            {
                Directory.Move(legacyPath, currentPath);
            }
            else
            {
                File.Move(legacyPath, currentPath);
            }

            AppLogger.Info("AppDataPath", $"Migrated legacy {description} to '{currentPath}'.");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AppDataPath", $"Failed to migrate legacy {description} '{legacyPath}'.", ex);
            return false;
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
            UserDataRoot.FolderName);
    }

    public static string GetSettingsDirectory()
    {
        return GetDataRoot();
    }

    public static string GetAirAppMarketDirectory()
    {
        return Path.Combine(GetDataRoot(), "AirAppMarket");
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
