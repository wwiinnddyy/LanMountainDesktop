using System.Globalization;

namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum RestartPresentationMode
{
    Foreground = 0,
    Minimized = 1,
    Tray = 2
}

public static class LauncherRuntimeMetadata
{
    public static string? GetOptionValue(string key, IReadOnlyList<string>? commandLineArgs = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var args = commandLineArgs ?? Environment.GetCommandLineArgs();
        var longPrefix = $"--{key}";

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(argument, longPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return args[index + 1];
                }

                return "true";
            }

            if (argument.Length > longPrefix.Length && argument[longPrefix.Length] == '=')
            {
                return argument[(longPrefix.Length + 1)..];
            }
        }

        return null;
    }

    public static bool HasOption(string key, IReadOnlyList<string>? commandLineArgs = null)
    {
        return !string.IsNullOrWhiteSpace(GetOptionValue(key, commandLineArgs));
    }

    public static string? GetPackageRoot(IReadOnlyList<string>? commandLineArgs = null)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable(LauncherIpcConstants.PackageRootEnvVar),
            GetOptionValue(LauncherIpcConstants.PackageRootEnvVar, commandLineArgs));
    }

    public static string? GetForwardedVersion(IReadOnlyList<string>? commandLineArgs = null)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable(LauncherIpcConstants.VersionEnvVar),
            GetOptionValue(LauncherIpcConstants.VersionEnvVar, commandLineArgs));
    }

    public static string? GetForwardedCodename(IReadOnlyList<string>? commandLineArgs = null)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable(LauncherIpcConstants.CodenameEnvVar),
            GetOptionValue(LauncherIpcConstants.CodenameEnvVar, commandLineArgs));
    }

    public static string? GetLaunchSource(IReadOnlyList<string>? commandLineArgs = null)
    {
        return GetOptionValue(LauncherIpcConstants.LaunchSourceOptionName, commandLineArgs);
    }

    public static int? GetLauncherProcessId(IReadOnlyList<string>? commandLineArgs = null)
    {
        var rawValue = FirstNonEmpty(
            Environment.GetEnvironmentVariable(LauncherIpcConstants.LauncherPidEnvVar),
            GetOptionValue(LauncherIpcConstants.LauncherPidEnvVar, commandLineArgs));

        return TryParsePositiveInt(rawValue);
    }

    public static int? GetRestartParentProcessId(IReadOnlyList<string>? commandLineArgs = null)
    {
        var rawValue = GetOptionValue(LauncherIpcConstants.RestartParentPidOptionName, commandLineArgs);
        return TryParsePositiveInt(rawValue);
    }

    public static RestartPresentationMode? GetRestartPresentationMode(IReadOnlyList<string>? commandLineArgs = null)
    {
        var rawValue = GetOptionValue(LauncherIpcConstants.RestartPresentationOptionName, commandLineArgs);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return NormalizeRestartPresentation(rawValue);
    }

    public static string FormatRestartPresentation(RestartPresentationMode mode)
    {
        return mode switch
        {
            RestartPresentationMode.Minimized => "minimized",
            RestartPresentationMode.Tray => "tray",
            _ => "foreground"
        };
    }

    public static RestartPresentationMode NormalizeRestartPresentation(string rawValue)
    {
        return rawValue.Trim().ToLowerInvariant() switch
        {
            "minimized" => RestartPresentationMode.Minimized,
            "tray" => RestartPresentationMode.Tray,
            _ => RestartPresentationMode.Foreground
        };
    }

    private static int? TryParsePositiveInt(string? rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) &&
               parsedValue > 0
            ? parsedValue
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
