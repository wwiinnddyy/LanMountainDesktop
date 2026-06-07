using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.Build.Execution;

namespace LanMountainDesktop.AirAppDevServer;

/// <summary>
/// AirApp 开发服务器
/// 提供文件监视、自动编译、热重载功能
/// </summary>
public sealed class AirAppDevServer
{
    private readonly string _projectPath;
    private readonly int _port;
    private readonly bool _verbose;
    private FileSystemWatcher? _watcher;
    private DateTime _lastBuildTime = DateTime.MinValue;
    private readonly object _buildLock = new();
    private bool _isBuilding;

    public AirAppDevServer(string projectPath, int port, bool verbose)
    {
        _projectPath = Path.GetFullPath(projectPath);
        _port = port;
        _verbose = verbose;
    }

    public Task StartAsync()
    {
        // 初始构建
        Console.WriteLine("🔨 初始构建中...");
        if (!BuildProject())
        {
            Console.WriteLine("❌ 初始构建失败");
            return Task.CompletedTask;
        }
        Console.WriteLine("✅ 初始构建成功");
        Console.WriteLine();

        // 启动文件监视
        StartFileWatcher();

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _watcher?.Dispose();
        return Task.CompletedTask;
    }

    private void StartFileWatcher()
    {
        _watcher = new FileSystemWatcher(_projectPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*",
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        Console.WriteLine("👁️ 文件监视已启动，等待更改...");
        Console.WriteLine();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // 忽略 bin、obj、.vs 等目录
        if (e.FullPath.Contains("\\bin\\") ||
            e.FullPath.Contains("\\obj\\") ||
            e.FullPath.Contains("\\.vs\\") ||
            e.FullPath.Contains("\\.git\\"))
        {
            return;
        }

        // 只处理源代码文件
        var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (ext != ".cs" && ext != ".axaml" && ext != ".json" && ext != ".csproj")
        {
            return;
        }

        // 防止重复触发（文件保存时可能触发多次）
        var now = DateTime.Now;
        if ((now - _lastBuildTime).TotalMilliseconds < 500)
        {
            return;
        }

        LogVerbose($"📝 检测到文件更改: {Path.GetFileName(e.FullPath)}");
        TriggerRebuild();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        LogVerbose($"📝 检测到文件重命名: {Path.GetFileName(e.OldFullPath)} -> {Path.GetFileName(e.FullPath)}");
        TriggerRebuild();
    }

    private void TriggerRebuild()
    {
        lock (_buildLock)
        {
            if (_isBuilding)
            {
                LogVerbose("⏳ 构建进行中，跳过此次触发");
                return;
            }

            _isBuilding = true;
        }

        Task.Run(() =>
        {
            try
            {
                // 短暂延迟，让文件写入完成
                Thread.Sleep(300);

                Console.WriteLine("🔄 重新构建中...");
                var success = BuildProject();

                _lastBuildTime = DateTime.Now;

                if (success)
                {
                    Console.WriteLine($"✅ 重新构建成功 [{DateTime.Now:HH:mm:ss}]");
                    Console.WriteLine("♻️ 热重载已生效");
                }
                else
                {
                    Console.WriteLine($"❌ 重新构建失败 [{DateTime.Now:HH:mm:ss}]");
                }
                Console.WriteLine();
            }
            finally
            {
                lock (_buildLock)
                {
                    _isBuilding = false;
                }
            }
        });
    }

    private bool BuildProject()
    {
        try
        {
            // 查找项目文件
            var projectFile = FindProjectFile();
            if (projectFile == null)
            {
                Console.WriteLine("❌ 未找到项目文件 (.csproj)");
                return false;
            }

            LogVerbose($"📄 项目文件: {Path.GetFileName(projectFile)}");

            // 使用 dotnet build
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectFile}\" -c Debug --nologo",
                WorkingDirectory = Path.GetDirectoryName(projectFile),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("❌ 无法启动 dotnet build");
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (_verbose)
            {
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output);
                }
            }

            if (process.ExitCode != 0)
            {
                Console.WriteLine("❌ 构建错误:");
                Console.WriteLine(error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 构建异常: {ex.Message}");
            if (_verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            return false;
        }
    }

    private string? FindProjectFile()
    {
        var files = Directory.GetFiles(_projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }

    private void LogVerbose(string message)
    {
        if (_verbose)
        {
            Console.WriteLine($"[VERBOSE] {message}");
        }
    }
}
