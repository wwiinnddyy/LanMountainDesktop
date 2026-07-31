using System.Diagnostics;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 检测安装目录下是否有正在运行的进程。
/// 公开静态 API 供 ViewModel 后续调用。
/// </summary>
public static class RunningProcessGuard
{
    /// <summary>
    /// 检查指定安装路径下是否有正在运行的进程。
    /// 如果找到进程，抛出 <see cref="InvalidOperationException"/> 并列出进程名称。
    /// </summary>
    /// <param name="installPath">要检查的安装根目录。</param>
    public static void EnsureNoRunningProcesses(string installPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var running = FindRunningProcesses(installPath);
        if (running.Count == 0)
        {
            return;
        }

        var names = string.Join("、", running);
        throw new InvalidOperationException(
            $"以下进程正在运行，无法继续操作，请先关闭后再重试：{names}");
    }

    /// <summary>
    /// 查找安装路径下正在运行的进程，返回进程名称列表。
    /// 每个进程尝试获取 MainModule 路径时捕获 Access Denied 等异常。
    /// </summary>
    public static List<string> FindRunningProcesses(string installPath)
    {
        var result = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        var normalizedInstall = Path.GetFullPath(installPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var module = process.MainModule;
                if (module == null)
                {
                    continue;
                }

                var modulePath = module.FileName;
                if (string.IsNullOrWhiteSpace(modulePath))
                {
                    continue;
                }

                var normalizedModule = Path.GetFullPath(modulePath);
                if (normalizedModule.StartsWith(normalizedInstall, StringComparison.OrdinalIgnoreCase))
                {
                    var processName = process.ProcessName;
                    if (!string.IsNullOrWhiteSpace(processName) && !result.Contains(processName))
                    {
                        result.Add(processName);
                    }
                }
            }
            catch
            {
                // 访问被拒绝或其他异常，跳过此进程
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }
}
