using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services;

internal sealed record ElevatedAirAppInstallResult(
    bool Success,
    string? Code,
    string? Message,
    string? ErrorMessage,
    string? InstalledPackagePath,
    string? ManifestId,
    string? ManifestName);

internal sealed class ElevatedAirAppInstallService
{
    public async Task<ElevatedAirAppInstallResult> InstallAsync(
        string sourcePackagePath,
        string airAppsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(airAppsDirectory);

        if (!OperatingSystem.IsWindows())
        {
            return new ElevatedAirAppInstallResult(
                false,
                "elevation_unsupported",
                "Elevated AirApp installation is only supported on Windows.",
                "Elevated AirApp installation is only supported on Windows.",
                null,
                null,
                null);
        }

        var launcherPath = ResolveLauncherExecutablePath();
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            return new ElevatedAirAppInstallResult(
                false,
                "launcher_not_found",
                "Launcher executable was not found for elevated AirApp installation.",
                $"Launcher executable was not found. ResolvedPath='{launcherPath ?? string.Empty}'.",
                null,
                null,
                null);
        }

        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"LanMountainDesktop.AirAppInstall.{Guid.NewGuid():N}.json");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = ResolveLauncherWorkingDirectory(launcherPath)
            };
            startInfo.ArgumentList.Add("AirApp");
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("--source");
            startInfo.ArgumentList.Add(Path.GetFullPath(sourcePackagePath));
            startInfo.ArgumentList.Add("--plugins-dir");
            startInfo.ArgumentList.Add(Path.GetFullPath(airAppsDirectory));
            startInfo.ArgumentList.Add("--result");
            startInfo.ArgumentList.Add(resultPath);

            var packageRoot = LauncherRuntimeMetadata.GetPackageRoot();
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                startInfo.ArgumentList.Add("--app-root");
                startInfo.ArgumentList.Add(Path.GetFullPath(packageRoot));
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ElevatedAirAppInstallResult(
                    false,
                    "launch_failed",
                    "Elevated AirApp installer did not start.",
                    "Elevated AirApp installer did not start.",
                    null,
                    null,
                    null);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (File.Exists(resultPath))
            {
                return ReadResult(resultPath);
            }

            return new ElevatedAirAppInstallResult(
                process.ExitCode == 0,
                process.ExitCode == 0 ? "ok" : "installer_failed",
                process.ExitCode == 0 ? "AirApp installed." : $"Elevated installer exited with code {process.ExitCode}.",
                process.ExitCode == 0 ? null : $"Elevated installer exited with code {process.ExitCode}.",
                null,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ElevatedAirAppInstallResult(
                false,
                "elevation_cancelled",
                "AirApp installation was cancelled before elevation was approved.",
                ex.Message,
                null,
                null,
                null);
        }
        catch (Exception ex)
        {
            return new ElevatedAirAppInstallResult(
                false,
                "elevation_failed",
                "Elevated AirApp installation failed.",
                ex.Message,
                null,
                null,
                null);
        }
        finally
        {
            TryDelete(resultPath);
        }
    }

    private static ElevatedAirAppInstallResult ReadResult(string resultPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
            var root = document.RootElement;
            return new ElevatedAirAppInstallResult(
                GetBoolean(root, "Success"),
                GetString(root, "Code"),
                GetString(root, "Message"),
                GetString(root, "ErrorMessage"),
                GetString(root, "InstalledPackagePath"),
                GetString(root, "ManifestId"),
                GetString(root, "ManifestName"));
        }
        catch (Exception ex)
        {
            return new ElevatedAirAppInstallResult(
                false,
                "invalid_result",
                "Elevated AirApp installer returned an invalid result.",
                ex.Message,
                null,
                null,
                null);
        }
    }

    private static string? ResolveLauncherExecutablePath()
    {
        var candidates = new[]
        {
            LauncherRuntimeMetadata.GetPackageRoot(),
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."))
        };

        foreach (var root in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            var path = Path.Combine(root!, OperatingSystem.IsWindows()
                ? "LanMountainDesktop.Launcher.exe"
                : "LanMountainDesktop.Launcher");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 解析提升进程的启动工作目录。
    /// 注意 Path.GetDirectoryName 对"只有文件名"的路径返回空串而不是 null，
    /// 所以原先的 `?? AppContext.BaseDirectory` 兜底永远不会触发；这里把空值也算作缺失。
    /// </summary>
    internal static string ResolveLauncherWorkingDirectory(string launcherPath)
    {
        var directory = Path.GetDirectoryName(launcherPath);

        return string.IsNullOrEmpty(directory)
            ? AppContext.BaseDirectory
            : directory;
    }
}
