using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace LanMountainDesktop.Shared.Contracts.Launcher;

public static class AppVersionProvider
{
    private const string DefaultVersion = "0.0.0";
    private const string DefaultCodename = "Administrate";
    private const string VersionFileName = "version.json";

    public static AppVersionInfo ResolveForCurrentProcess(
        IReadOnlyList<string>? commandLineArgs = null,
        string? executablePath = null,
        string? deploymentDirectory = null)
    {
        var args = commandLineArgs ?? Environment.GetCommandLineArgs();
        return Resolve(
            packageRoot: LauncherRuntimeMetadata.GetPackageRoot(args),
            deploymentDirectory: deploymentDirectory ?? AppContext.BaseDirectory,
            executablePath: executablePath ?? Environment.ProcessPath,
            versionOverride: LauncherRuntimeMetadata.GetForwardedVersion(args),
            codenameOverride: LauncherRuntimeMetadata.GetForwardedCodename(args));
    }

    public static AppVersionInfo ResolveFromDeploymentDirectory(
        string? deploymentDirectory,
        string? executablePath = null,
        string? versionOverride = null,
        string? codenameOverride = null)
    {
        return Resolve(
            packageRoot: null,
            deploymentDirectory: deploymentDirectory,
            executablePath: executablePath,
            versionOverride: versionOverride,
            codenameOverride: codenameOverride);
    }

    public static AppVersionInfo ResolveFromPackageRoot(
        string? packageRoot,
        string executableName,
        string? versionOverride = null,
        string? codenameOverride = null)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return CreateFallback(versionOverride, codenameOverride);
        }

        var deploymentDirectory = FindCurrentDeploymentDirectory(packageRoot, executableName);
        var executablePath = !string.IsNullOrWhiteSpace(deploymentDirectory)
            ? Path.Combine(deploymentDirectory, executableName)
            : null;

        return Resolve(
            packageRoot: packageRoot,
            deploymentDirectory: deploymentDirectory,
            executablePath: executablePath,
            versionOverride: versionOverride,
            codenameOverride: codenameOverride);
    }

    public static AppVersionInfo Resolve(
        string? packageRoot,
        string? deploymentDirectory,
        string? executablePath,
        string? versionOverride = null,
        string? codenameOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(versionOverride))
        {
            return Create(versionOverride, codenameOverride);
        }

        var normalizedDeploymentDirectory = NormalizeExistingDirectory(deploymentDirectory)
            ?? ResolveDeploymentFromPackageRoot(packageRoot, executablePath);

        if (!string.IsNullOrWhiteSpace(normalizedDeploymentDirectory) &&
            TryReadVersionFile(normalizedDeploymentDirectory, out var fileInfo))
        {
            return OverrideMissingParts(fileInfo, versionOverride, codenameOverride);
        }

        var normalizedExecutablePath = NormalizeExistingFile(executablePath)
            ?? ResolveExecutableFromDeployment(normalizedDeploymentDirectory, executablePath);

        if (!string.IsNullOrWhiteSpace(normalizedExecutablePath) &&
            TryReadExecutableVersion(normalizedExecutablePath, out var executableInfo))
        {
            return OverrideMissingParts(executableInfo, versionOverride, codenameOverride);
        }

        var versionFromDirectory = TryParseVersionFromDeploymentDirectory(normalizedDeploymentDirectory);
        if (!string.IsNullOrWhiteSpace(versionFromDirectory))
        {
            return Create(versionFromDirectory, codenameOverride);
        }

        return CreateFallback(versionOverride, codenameOverride);
    }

    public static string NormalizeVersionText(string? rawValue, string fallback = DefaultVersion)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        var normalized = TrimSurroundingQuotes(rawValue)
            .Split('+', 2, StringSplitOptions.TrimEntries)[0]
            .Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    public static string NormalizeCodename(string? rawValue, string fallback = DefaultCodename)
    {
        var normalized = TrimSurroundingQuotes(rawValue);
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    private static AppVersionInfo OverrideMissingParts(
        AppVersionInfo source,
        string? versionOverride,
        string? codenameOverride)
    {
        return new AppVersionInfo
        {
            Version = NormalizeVersionText(versionOverride ?? source.Version),
            Codename = NormalizeCodename(codenameOverride ?? source.Codename)
        };
    }

    private static AppVersionInfo CreateFallback(string? versionOverride, string? codenameOverride)
    {
        return Create(versionOverride ?? DefaultVersion, codenameOverride ?? DefaultCodename);
    }

    private static AppVersionInfo Create(string version, string? codename)
    {
        return new AppVersionInfo
        {
            Version = NormalizeVersionText(version),
            Codename = NormalizeCodename(codename)
        };
    }

    private static bool TryReadVersionFile(string deploymentDirectory, out AppVersionInfo info)
    {
        info = default!;
        var versionFilePath = Path.Combine(deploymentDirectory, VersionFileName);
        if (!File.Exists(versionFilePath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(versionFilePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var version = ReadStringProperty(root, nameof(AppVersionInfo.Version));
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var codename = ReadStringProperty(root, nameof(AppVersionInfo.Codename));
            info = new AppVersionInfo
            {
                Version = NormalizeVersionText(version),
                Codename = NormalizeCodename(codename)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadExecutableVersion(string executablePath, out AppVersionInfo info)
    {
        info = default!;

        try
        {
            var fileInfo = FileVersionInfo.GetVersionInfo(executablePath);
            var version = NormalizeVersionText(fileInfo.ProductVersion);
            if (string.Equals(version, DefaultVersion, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(fileInfo.FileVersion))
            {
                version = NormalizeVersionText(fileInfo.FileVersion);
            }

            if (string.Equals(version, DefaultVersion, StringComparison.Ordinal))
            {
                var assemblyNameVersion = AssemblyName.GetAssemblyName(executablePath).Version;
                if (assemblyNameVersion is not null)
                {
                    version = NormalizeVersionText(assemblyNameVersion.ToString());
                }
            }

            info = new AppVersionInfo
            {
                Version = version,
                Codename = DefaultCodename
            };
            return !string.Equals(version, DefaultVersion, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveDeploymentFromPackageRoot(string? packageRoot, string? executablePath)
    {
        var normalizedPackageRoot = NormalizeExistingDirectory(packageRoot);
        if (string.IsNullOrWhiteSpace(normalizedPackageRoot))
        {
            return null;
        }

        var normalizedExecutablePath = NormalizeExistingFile(executablePath);
        if (!string.IsNullOrWhiteSpace(normalizedExecutablePath))
        {
            var executableDirectory = NormalizeExistingDirectory(Path.GetDirectoryName(normalizedExecutablePath));
            if (!string.IsNullOrWhiteSpace(executableDirectory) &&
                executableDirectory.StartsWith(normalizedPackageRoot, StringComparison.OrdinalIgnoreCase))
            {
                return executableDirectory;
            }
        }

        var executableName = Path.GetFileName(normalizedExecutablePath);
        return FindCurrentDeploymentDirectory(normalizedPackageRoot, executableName);
    }

    private static string? ResolveExecutableFromDeployment(string? deploymentDirectory, string? executablePath)
    {
        var normalizedExecutablePath = NormalizeExistingFile(executablePath);
        if (!string.IsNullOrWhiteSpace(normalizedExecutablePath))
        {
            return normalizedExecutablePath;
        }

        var normalizedDeploymentDirectory = NormalizeExistingDirectory(deploymentDirectory);
        if (string.IsNullOrWhiteSpace(normalizedDeploymentDirectory))
        {
            return null;
        }

        foreach (var candidateName in GetExecutableCandidates(executablePath))
        {
            var candidatePath = Path.Combine(normalizedDeploymentDirectory, candidateName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetExecutableCandidates(string? executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return [fileName];
        }

        return OperatingSystem.IsWindows()
            ? ["LanMountainDesktop.exe"]
            : ["LanMountainDesktop"];
    }

    private static string? FindCurrentDeploymentDirectory(string packageRoot, string? executableName)
    {
        try
        {
            var candidates = Directory.GetDirectories(packageRoot, "app-*", SearchOption.TopDirectoryOnly)
                .Where(path => !File.Exists(Path.Combine(path, ".destroy")))
                .Where(path => !File.Exists(Path.Combine(path, ".partial")))
                .Select(path => new
                {
                    Path = path,
                    IsCurrent = File.Exists(Path.Combine(path, ".current")),
                    HasExecutable = string.IsNullOrWhiteSpace(executableName) || File.Exists(Path.Combine(path, executableName)),
                    Version = TryParseVersionFromDeploymentDirectory(path)
                })
                .Where(item => item.HasExecutable)
                .OrderByDescending(item => item.IsCurrent)
                .ThenByDescending(item => item.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return candidates.FirstOrDefault()?.Path;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryParseVersionFromDeploymentDirectory(string? deploymentDirectory)
    {
        if (string.IsNullOrWhiteSpace(deploymentDirectory))
        {
            return null;
        }

        var directoryName = Path.GetFileName(deploymentDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directoryName) ||
            !directoryName.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var remaining = directoryName["app-".Length..];
        var segments = remaining.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            ? NormalizeVersionText(segments[0])
            : null;
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadStringProperty(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string TrimSurroundingQuotes(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var normalized = rawValue.Trim();
        while (normalized.Length >= 2)
        {
            var first = normalized[0];
            var last = normalized[^1];
            if ((first == '\'' && last == '\'') ||
                (first == '"' && last == '"'))
            {
                normalized = normalized[1..^1].Trim();
                continue;
            }

            break;
        }

        return normalized;
    }
}
