using System.Diagnostics;
using Avalonia;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;

#if WINDOWS
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
#endif

namespace LanMountainDesktop.Launcher;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var commandContext = CommandContext.FromArgs(args);

        // 处理遗留插件安装命令
        if (commandContext.IsLegacyPluginInstall)
        {
            var installer = new PluginInstallerService();
            return await Commands.RunLegacyPluginInstallAsync(commandContext, installer).ConfigureAwait(false);
        }

        // 处理其他 CLI 命令 (update, plugin, rollback 等)
        if (!string.Equals(commandContext.Command, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return await Commands.RunCliCommandAsync(commandContext).ConfigureAwait(false);
        }

        // 主启动流程: OOBE -> Splash -> 版本选择 -> 启动主程序
        LauncherRuntimeContext.Current = commandContext;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return Environment.ExitCode;
    }

    private static int LaunchMainApplication(string[] args)
    {
        // 获取可执行文件名
        string executableName = OperatingSystem.IsWindows() 
            ? "LanMountainDesktop.exe" 
            : "LanMountainDesktop";

        // 获取安装根目录
        var rootDir = Path.GetFullPath(Path.GetDirectoryName(Environment.ProcessPath) ?? "");

        // 查找最佳版本
        var installation = FindBestVersion(rootDir, executableName);

        if (installation == null)
        {
            ShowError("找不到有效的 LanMountainDesktop 版本，可能是安装已损坏。\n请访问 https://github.com/ClassIsland/LanMountainDesktop 重新下载并安装。");
            return 1;
        }

        var exePath = Path.Combine(installation, executableName);

        // Linux/macOS: 自动添加可执行权限
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{exePath}\"",
                    CreateNoWindow = true
                });
                chmod?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"无法设置可执行权限: {ex.Message}");
            }
        }

        // 清理待删除的旧版本
        CleanupDestroyedVersions(rootDir);

        // 启动主程序
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = rootDir,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // 传递包根目录环境变量
        startInfo.EnvironmentVariables["LanMountainDesktop_PackageRoot"] = rootDir;

        try
        {
            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            ShowError($"启动主程序失败: {ex.Message}");
            return 1;
        }
    }

    private static string? FindBestVersion(string rootDir, string executableName)
    {
        return Directory.GetDirectories(rootDir)
            .Where(x =>
            {
                var dirName = Path.GetFileName(x);
                return dirName.StartsWith("app-") &&
                       !File.Exists(Path.Combine(x, ".destroy")) &&
                       !File.Exists(Path.Combine(x, ".partial")) &&
                       File.Exists(Path.Combine(x, executableName));
            })
            .OrderBy(x => File.Exists(Path.Combine(x, ".current")) ? 0 : 1)  // .current 优先
            .ThenByDescending(x => ParseVersion(Path.GetFileName(x)))         // 版本号降序
            .FirstOrDefault();
    }

    private static Version ParseVersion(string dirName)
    {
        // 从 "app-1.0.0" 格式解析版本号
        var parts = dirName.Split('-');
        if (parts.Length >= 2 && Version.TryParse(parts[1], out var version))
        {
            return version;
        }
        return new Version(0, 0, 0);
    }

    private static void CleanupDestroyedVersions(string rootDir)
    {
        try
        {
            var destroyedDirs = Directory.GetDirectories(rootDir)
                .Where(x => File.Exists(Path.Combine(x, ".destroy")));

            foreach (var dir in destroyedDirs)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // 忽略删除失败(可能文件被占用),下次启动再试
                }
            }
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private static void ShowError(string message)
    {
#if WINDOWS
        try
        {
            PInvoke.MessageBox(
                HWND.Null,
                message,
                "LanMountainDesktop Launcher",
                MESSAGEBOX_STYLE.MB_ICONERROR | MESSAGEBOX_STYLE.MB_OK
            );
        }
        catch
        {
            Console.Error.WriteLine(message);
        }
#else
        Console.Error.WriteLine(message);
#endif
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
