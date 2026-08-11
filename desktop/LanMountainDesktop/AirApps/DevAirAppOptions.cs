using System;
using System.Collections.Generic;
using System.Linq;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.AirApps;

public sealed class DevAirAppOptions
{
    private static readonly string[] DevPluginPathArgs = ["--dev-plugin", "-dp"];
    private static readonly string[] DevModeArgs = ["--dev-mode", "-dev"];
    private static readonly string[] HotReloadArgs = ["--hot-reload", "-hr"];
    private static readonly string EnvDevPluginPath = "LMD_DEV_PLUGIN";
    private static readonly string EnvDevMode = "LMD_DEV_MODE";

    public static DevAirAppOptions Current { get; } = new();

    public bool IsDevMode { get; private set; }

    public string? DevPluginPath { get; private set; }

    public bool EnableHotReload { get; private set; }

    public IReadOnlyList<string> DevPluginPaths { get; private set; } = Array.Empty<string>();

    private DevAirAppOptions() { }

    public static DevAirAppOptions Parse(string[] args)
    {
        var options = Current;

        options.IsDevMode = TryGetFlag(args, DevModeArgs) ||
                            string.Equals(Environment.GetEnvironmentVariable(EnvDevMode), "1", StringComparison.Ordinal) ||
                            string.Equals(Environment.GetEnvironmentVariable(EnvDevMode), "true", StringComparison.OrdinalIgnoreCase);

        options.DevPluginPath = TryGetValue(args, DevPluginPathArgs) ??
                                Environment.GetEnvironmentVariable(EnvDevPluginPath)?.Trim();

        options.EnableHotReload = TryGetFlag(args, HotReloadArgs);

        if (!options.IsDevMode && !string.IsNullOrWhiteSpace(options.DevPluginPath))
        {
            options.IsDevMode = true;
        }

        options.DevPluginPaths = ResolveDevPluginPaths(options.DevPluginPath);

        if (options.IsDevMode)
        {
            AppLogger.Info(
                "DevAirApp",
                $"Developer mode enabled. DevPluginPath='{options.DevPluginPath}'; EnableHotReload={options.EnableHotReload}; ResolvedPaths={options.DevPluginPaths.Count}.");
        }

        return options;
    }

    internal void ApplySettingsFromSnapshot(bool isDevMode, string? devAirAppPath)
    {
        if (isDevMode && !IsDevMode)
        {
            IsDevMode = true;
        }

        if (!string.IsNullOrWhiteSpace(devAirAppPath) && string.IsNullOrWhiteSpace(DevPluginPath))
        {
            DevPluginPath = devAirAppPath;
        }

        var allPaths = new List<string>(DevPluginPaths);
        if (!string.IsNullOrWhiteSpace(devAirAppPath))
        {
            foreach (var path in ResolveDevPluginPaths(devAirAppPath))
            {
                if (!allPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    allPaths.Add(path);
                }
            }
        }

        DevPluginPaths = allPaths;
    }

    private static IReadOnlyList<string> ResolveDevPluginPaths(string? rawPath)
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
                    AppLogger.Warn("DevAirApp", $"Developer plugin path '{path}' does not exist. It will be skipped.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DevAirApp", $"Failed to resolve developer plugin path '{path}': {ex.Message}");
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
