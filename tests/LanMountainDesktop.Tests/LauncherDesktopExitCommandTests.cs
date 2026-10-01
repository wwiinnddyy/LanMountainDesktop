using System.Text.Json;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Infrastructure;
using LanMountainDesktop.Launcher.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 启动器的 `desktop exit` 动词。退出通道本身早就在（宿主的 IPublicShellControlService.ExitAsync
/// 挂在公共 IPC 上），这一族钉的是"动词接对了、而且只接了这一条"：
/// 动词层要真路由（不是掉进 unsupported_command 兜底），启动器里请求退出的落点要恰好一个。
/// 用例一律不真发退出请求——跑测试的机器上可能正开着桌面，发出去就是把用户的会话关了。
/// </summary>
public sealed class LauncherDesktopExitCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.LauncherDesktopExitCommandTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DesktopCommand_RoutesToSubCommandLayer()
    {
        Directory.CreateDirectory(_root);
        var resultPath = Path.Combine(_root, "result.json");
        var context = CommandContext.FromArgs(["desktop", "teleport", "--app-root", _root, "--result", resultPath]);

        var exitCode = await Commands.RunCliCommandAsync(context);
        var result = ReadResult(resultPath);

        Assert.Equal(1, exitCode);
        Assert.Equal("desktop", result.Stage);
        Assert.Equal("unsupported_subcommand", result.Code);
    }

    [Fact]
    public void ExitVerbIsTheOnlyExitCallerInTheLauncher()
    {
        var launcherDir = Path.Combine(ResolveRepositoryRoot(), "desktop", "LanMountainDesktop.Launcher");
        var sources = Directory.GetFiles(launcherDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .ToList();

        var exitCallers = sources
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, index, line))
                .Where(row => !row.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && row.line.Contains(".ExitAsync(", StringComparison.Ordinal))
                .Select(row => $"{Path.GetFileName(row.path)}:{row.index + 1}"))
            .ToList();

        Assert.True(
            exitCallers.Count == 1,
            $"启动器里请求退出的落点应有且只有 1 处（desktop exit 动词），现量 {exitCallers.Count} 处：" +
            string.Join(" / ", exitCallers) +
            "——多出来的那一处是第二条退出路径，#G1-CU 明确禁的正是它");
        Assert.Equal("Commands.cs", exitCallers[0].Split(':')[0]);

        // 自己抄一遍连接就等于绕开 PublicIpcConnection 那一家（超时与"连不上"的口径会分叉）。
        var commandsSource = File.ReadAllText(Path.Combine(launcherDir, "Infrastructure", "Commands.cs"));
        Assert.False(
            commandsSource.Contains(".ConnectAsync(", StringComparison.Ordinal),
            "Commands.cs 里不该自己 ipcClient.ConnectAsync()——连接归 PublicIpcConnection 那一家");

        // 宿主的 TryExit 只有宿主自己该碰；启动器直接调它（跨进程也调不动，只会诱出一条假通道）。
        var hostExitCalls = sources
            .Count(path => File.ReadAllLines(path)
                .Any(line => line.Contains("TryExit(", StringComparison.Ordinal)));
        Assert.Equal(0, hostExitCalls);
    }

    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"从 {AppContext.BaseDirectory} 往上没找到 LanMountainDesktop.slnx——这条门扫的是启动器源码，仓库根定错位就等于没扫");
    }

    private static LauncherResult ReadResult(string path)
    {
        var result = JsonSerializer.Deserialize<LauncherResult>(File.ReadAllText(path));
        return result ?? throw new InvalidOperationException("Launcher result was not written.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
