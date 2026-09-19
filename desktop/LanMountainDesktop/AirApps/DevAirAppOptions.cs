using System;
using System.Collections.Generic;
using System.Linq;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.AirApps;

public sealed class DevAirAppOptions
{
    // --dev-airapp 是当前命名；--dev-AirApp / -dp 是 PluginSdk 时代的别名，保留兼容。
    private static readonly string[] DevAirAppPathArgs = ["--dev-airapp", "--dev-AirApp", "-dp"];
    private static readonly string[] DevModeArgs = ["--dev-mode", "-dev"];
    private static readonly string[] HotReloadArgs = ["--hot-reload", "-hr"];
    private static readonly string EnvAirAppPath = "LMD_DEV_AIRAPP";
    private static readonly string EnvAirAppPathLegacy = "LMD_DEV_PLUGIN";
    private static readonly string EnvDevMode = "LMD_DEV_MODE";

    public static DevAirAppOptions Current { get; } = new();

    public bool IsDevMode { get; private set; }

    public string? DevAirAppPath { get; private set; }

    public bool EnableHotReload { get; private set; }

    public IReadOnlyList<string> DevAirAppPaths { get; private set; } = Array.Empty<string>();

    private DevAirAppOptions() { }

    public static DevAirAppOptions Parse(string[] args)
    {
        var options = Current;

        options.IsDevMode = TryGetFlag(args, DevModeArgs) ||
                            string.Equals(Environment.GetEnvironmentVariable(EnvDevMode), "1", StringComparison.Ordinal) ||
                            string.Equals(Environment.GetEnvironmentVariable(EnvDevMode), "true", StringComparison.OrdinalIgnoreCase);

        options.DevAirAppPath = TryGetValue(args, DevAirAppPathArgs) ??
                                Environment.GetEnvironmentVariable(EnvAirAppPath)?.Trim() ??
                                Environment.GetEnvironmentVariable(EnvAirAppPathLegacy)?.Trim();

        options.EnableHotReload = TryGetFlag(args, HotReloadArgs);

        if (!options.IsDevMode && !string.IsNullOrWhiteSpace(options.DevAirAppPath))
        {
            options.IsDevMode = true;
        }

        options.DevAirAppPaths = ResolveDevAirAppPaths(options.DevAirAppPath);

        if (options.IsDevMode)
        {
            AppLogger.Info(
                "DevAirApp",
                $"Developer mode enabled. DevAirAppPath='{options.DevAirAppPath}'; EnableHotReload={options.EnableHotReload}; ResolvedPaths={options.DevAirAppPaths.Count}.");
        }

        return options;
    }

    internal void ApplySettingsFromSnapshot(bool isDevMode, string? devAirAppPath)
    {
        if (isDevMode && !IsDevMode)
        {
            IsDevMode = true;
        }

        if (!string.IsNullOrWhiteSpace(devAirAppPath) && string.IsNullOrWhiteSpace(DevAirAppPath))
        {
            DevAirAppPath = devAirAppPath;
        }

        var allPaths = new List<string>(DevAirAppPaths);
        if (!string.IsNullOrWhiteSpace(devAirAppPath))
        {
            foreach (var path in ResolveDevAirAppPaths(devAirAppPath))
            {
                if (!allPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    allPaths.Add(path);
                }
            }
        }

        DevAirAppPaths = allPaths;
    }

    private static IReadOnlyList<string> ResolveDevAirAppPaths(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Array.Empty<string>();
        }

        var paths = rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var resolved = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    resolved.Add(fullPath);
                }
                else
                {
                    AppLogger.Warn("DevAirApp", $"Developer AirApp path '{path}' does not exist. It will be skipped.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DevAirApp", $"Failed to resolve developer AirApp path '{path}': {ex.Message}");
            }
        }

        return resolved;
    }

    private static bool TryGetFlag(string[] args, string[] names)
    {
        return args.Any(arg => names.Any(name => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? TryGetValue(string[] args, string[] names)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (names.Any(name => string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)))
            {
                return args[i + 1]?.Trim();
            }
        }

        return null;
    }
}
