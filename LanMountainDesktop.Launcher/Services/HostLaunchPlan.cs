using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed record HostLaunchPlan(
    string HostPath,
    string PackageRoot,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    AppVersionInfo VersionInfo);

internal static class HostLaunchPlanBuilder
{
    private static readonly string[] LauncherOnlyOptions =
    [
        "debug", "show-loading-details", "plugins-dir", "source", "result",
        "app-root",
        LauncherIpcConstants.LauncherPidEnvVar,
        LauncherIpcConstants.PackageRootEnvVar,
        LauncherIpcConstants.VersionEnvVar,
        LauncherIpcConstants.CodenameEnvVar
    ];

    public static HostLaunchPlan Build(
        CommandContext context,
        DeploymentLocator deploymentLocator,
        HostResolutionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deploymentLocator);
        ArgumentNullException.ThrowIfNull(resolution);

        if (string.IsNullOrWhiteSpace(resolution.ResolvedHostPath))
        {
            throw new InvalidOperationException("Host path must be resolved before building a launch plan.");
        }

        var hostPath = Path.GetFullPath(resolution.ResolvedHostPath);
        var packageRoot = ResolvePackageRoot(hostPath, resolution.AppRoot, resolution.ResolutionSource);
        var versionInfo = deploymentLocator.GetVersionInfo();
        var arguments = BuildForwardedArguments(context, packageRoot, versionInfo);
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LauncherIpcConstants.LauncherPidEnvVar] = Environment.ProcessId.ToString(),
            [LauncherIpcConstants.PackageRootEnvVar] = packageRoot,
            [LauncherIpcConstants.VersionEnvVar] = versionInfo.Version,
            [LauncherIpcConstants.CodenameEnvVar] = versionInfo.Codename
        };

        return new HostLaunchPlan(
            hostPath,
            packageRoot,
            Directory.Exists(packageRoot)
                ? packageRoot
                : Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory,
            arguments,
            environment,
            versionInfo);
    }

    public static string FormatArgumentsForLog(IReadOnlyList<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string ResolvePackageRoot(string hostPath, string appRoot, string? resolutionSource)
    {
        var fullAppRoot = string.IsNullOrWhiteSpace(appRoot)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(appRoot);

        var hostDirectory = Path.GetDirectoryName(hostPath);
        if (hostDirectory is not null &&
            Directory.Exists(fullAppRoot) &&
            IsAppDeploymentDirectory(hostDirectory) &&
            IsParentOf(fullAppRoot, hostDirectory))
        {
            return fullAppRoot;
        }

        if (string.Equals(resolutionSource, "published_deployment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolutionSource, "explicit_app_root_deployment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resolutionSource, "legacy_fallback", StringComparison.OrdinalIgnoreCase))
        {
            return fullAppRoot;
        }

        return hostDirectory ?? fullAppRoot;
    }

    private static IReadOnlyList<string> BuildForwardedArguments(
        CommandContext context,
        string packageRoot,
        AppVersionInfo versionInfo)
    {
        var arguments = new List<string>();

        for (var index = 0; index < context.RawArgs.Count; index++)
        {
            var arg = context.RawArgs[index];

            if (index == 0 &&
                !arg.StartsWith("--", StringComparison.Ordinal) &&
                string.Equals(arg, context.Command, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index == 1 &&
                !arg.StartsWith("--", StringComparison.Ordinal) &&
                string.Equals(arg, context.SubCommand, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                var equalsIndex = key.IndexOf('=');
                if (equalsIndex >= 0)
                {
                    key = key[..equalsIndex];
                }

                if (LauncherOnlyOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    if (equalsIndex < 0 &&
                        index + 1 < context.RawArgs.Count &&
                        !context.RawArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        index++;
                    }

                    continue;
                }
            }

            arguments.Add(arg);
        }

        arguments.Add($"--{LauncherIpcConstants.LauncherPidEnvVar}={Environment.ProcessId}");
        arguments.Add($"--{LauncherIpcConstants.PackageRootEnvVar}={packageRoot}");
        arguments.Add($"--{LauncherIpcConstants.VersionEnvVar}={versionInfo.Version}");
        arguments.Add($"--{LauncherIpcConstants.CodenameEnvVar}={versionInfo.Codename}");

        return arguments;
    }

    private static bool IsAppDeploymentDirectory(string path)
    {
        var fileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return fileName.StartsWith("app-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParentOf(string parent, string child)
    {
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var childPath = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(parentPath, childPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return childPath.StartsWith(
            parentPath + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Contains('"') && !value.Contains(' ') && !value.Contains('\t'))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append("\\\"");
            }
            else
            {
                builder.Append(ch);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
