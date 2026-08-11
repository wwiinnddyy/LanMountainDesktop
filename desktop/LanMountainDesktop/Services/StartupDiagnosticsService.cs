using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LanMountainDesktop.Services;

public sealed class StartupDiagnosticsResult
{
    public string ExecutablePath { get; init; } = string.Empty;

    public string BaseDirectory { get; init; } = string.Empty;

    public string ExecutableName { get; init; } = string.Empty;

    public bool IsLegacyExecutableLaunch { get; init; }

    public IReadOnlyList<string> FoundLegacyArtifacts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeletedLegacyArtifacts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FailedLegacyArtifacts { get; init; } = Array.Empty<string>();
}

public static class StartupDiagnosticsService
{
    private const string CurrentExecutableName = "LanMountainDesktop.exe";

    private static readonly string[] LegacyArtifactNames =
    [
        "LanMontainDesktop.exe",
        "LanMontainDesktop.dll",
        "LanMontainDesktop.deps.json",
        "LanMontainDesktop.runtimeconfig.json",
        "LanMontainDesktop.pdb",
        "LanMontainDesktop.exe.WebView2"
    ];

    public static StartupDiagnosticsResult Run(string[] args)
    {
        var executablePath = ResolveExecutablePath();
        var baseDirectory = AppContext.BaseDirectory;
        var executableName = Path.GetFileName(executablePath);
        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var fileVersion = string.Empty;

        try
        {
            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                fileVersion = FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? string.Empty;
            }
        }
        catch
        {
            // Keep diagnostics best-effort.
        }

        AppLogger.Info(
            "Startup",
            $"Application starting. ExecutablePath={executablePath}; BaseDirectory={baseDirectory}; ExecutableName={executableName}; AssemblyVersion={assemblyVersion}; FileVersion={fileVersion}; Args=[{string.Join(", ", args)}]");

        var foundLegacyArtifacts = LegacyArtifactNames
            .Select(name => Path.Combine(baseDirectory, name))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var deletedLegacyArtifacts = new List<string>();
        var failedLegacyArtifacts = new List<string>();
        foreach (var legacyArtifact in foundLegacyArtifacts)
        {
            if (string.Equals(legacyArtifact, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryDeleteLegacyArtifact(legacyArtifact, out var error))
            {
                deletedLegacyArtifacts.Add(legacyArtifact);
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                failedLegacyArtifacts.Add($"{legacyArtifact} ({error})");
            }
        }

        if (foundLegacyArtifacts.Count > 0)
        {
            AppLogger.Warn(
                "StartupDiagnostics",
                $"Found legacy artifacts: {string.Join("; ", foundLegacyArtifacts)}");
        }

        if (deletedLegacyArtifacts.Count > 0)
        {
            AppLogger.Info(
                "StartupDiagnostics",
                $"Deleted legacy artifacts: {string.Join("; ", deletedLegacyArtifacts)}");
        }

        if (failedLegacyArtifacts.Count > 0)
        {
            AppLogger.Warn(
                "StartupDiagnostics",
                $"Failed to delete legacy artifacts: {string.Join("; ", failedLegacyArtifacts)}");
        }

        var isLegacyExecutableLaunch = string.Equals(
            executableName,
            "LanMontainDesktop.exe",
            StringComparison.OrdinalIgnoreCase);

        if (isLegacyExecutableLaunch)
        {
            AppLogger.Warn(
                "StartupDiagnostics",
                $"Legacy executable launch detected. Current executable should be '{CurrentExecutableName}', but actual executable is '{executableName}'.");
        }

        return new StartupDiagnosticsResult
        {
            ExecutablePath = executablePath,
            BaseDirectory = baseDirectory,
            ExecutableName = executableName,
            IsLegacyExecutableLaunch = isLegacyExecutableLaunch,
            FoundLegacyArtifacts = foundLegacyArtifacts,
            DeletedLegacyArtifacts = deletedLegacyArtifacts,
            FailedLegacyArtifacts = failedLegacyArtifacts
        };
    }

    public static void ShowLegacyExecutableWarningIfNeeded(StartupDiagnosticsResult diagnostics)
    {
        if (!diagnostics.IsLegacyExecutableLaunch)
        {
            return;
        }

        var message =
            "检测到当前是从旧残留可执行文件启动的。\r\n\r\n" +
            $"当前文件: {diagnostics.ExecutableName}\r\n" +
            $"当前路径: {diagnostics.ExecutablePath}\r\n\r\n" +
            $"请改用 {CurrentExecutableName} 启动，以免继续读取旧残留文件。\r\n" +
            $"日志目录: {AppLogger.LogDirectory}";

        WindowsNativeDialogService.ShowWarning("LanMountainDesktop 启动诊断", message);
    }

    private static string ResolveExecutablePath()
    {
        try
        {
            return Environment.ProcessPath ??
                   Process.GetCurrentProcess().MainModule?.FileName ??
                   string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryDeleteLegacyArtifact(string path, out string? error)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                error = null;
                return true;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                error = null;
                return true;
            }

            error = null;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
