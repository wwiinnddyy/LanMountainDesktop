using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LanMountainDesktop.Services;

internal static class LinuxDesktopEntryInstaller
{
    private const string DesktopFileName = "LanMountainDesktop.desktop";
    private const string IconFileName = "lanmountaindesktop.png";
    private const string IconName = "lanmountaindesktop";

    public static void EnsureInstalled()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var executablePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            var dataHome = ResolveDataHome();
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                return;
            }

            var applicationsDir = Path.Combine(dataHome, "applications");
            var iconDir = Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps");

            Directory.CreateDirectory(applicationsDir);
            Directory.CreateDirectory(iconDir);

            var desktopTargetPath = Path.Combine(applicationsDir, DesktopFileName);
            var iconTargetPath = Path.Combine(iconDir, IconFileName);

            TryCopyBundledIcon(iconTargetPath);

            var desktopEntryContent = BuildDesktopEntryContent(executablePath);
            WriteFileIfChanged(desktopTargetPath, desktopEntryContent);

            TryRunCommand("chmod", "+x", executablePath);
            TryRunCommand("chmod", "+x", desktopTargetPath);
            TryRunCommand("update-desktop-database", applicationsDir);
            TryRunCommand("gtk-update-icon-cache", Path.Combine(dataHome, "icons", "hicolor"));
        }
        catch
        {
            // Keep startup resilient if desktop integration fails.
        }
    }

    private static string ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        var commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Length > 0 && !string.IsNullOrWhiteSpace(commandLineArgs[0]))
        {
            return commandLineArgs[0];
        }

        return string.Empty;
    }

    private static string ResolveDataHome()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome))
        {
            return dataHome.Trim();
        }

        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homePath))
        {
            return string.Empty;
        }

        return Path.Combine(homePath, ".local", "share");
    }

    private static void TryCopyBundledIcon(string iconTargetPath)
    {
        foreach (var candidatePath in EnumerateIconSourceCandidates())
        {
            try
            {
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                File.Copy(candidatePath, iconTargetPath, overwrite: true);
                return;
            }
            catch
            {
                // Ignore failures and continue trying fallbacks.
            }
        }
    }

    private static string[] EnumerateIconSourceCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return
        [
            Path.Combine(baseDirectory, "share", "icons", "hicolor", "256x256", "apps", IconFileName),
            Path.Combine(baseDirectory, IconFileName)
        ];
    }

    private static string BuildDesktopEntryContent(string executablePath)
    {
        var escapedExecutablePath = executablePath.Replace("\"", "\\\"", StringComparison.Ordinal);
        return
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Version=1.0\n" +
            "Name=LanMountainDesktop\n" +
            "Comment=LanMountainDesktop desktop shell\n" +
            $"Exec=\"{escapedExecutablePath}\" %U\n" +
            $"Icon={IconName}\n" +
            "Terminal=false\n" +
            "Categories=Utility;Education;\n" +
            "StartupWMClass=LanMountainDesktop\n";
    }

    private static void WriteFileIfChanged(string filePath, string content)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var existing = File.ReadAllText(filePath);
                if (string.Equals(existing, content, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }
        catch
        {
            // Fall through to attempt writing the content.
        }

        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void TryRunCommand(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            _ = process.WaitForExit(2_500);
        }
        catch
        {
            // Ignore missing command or update failures.
        }
    }
}
