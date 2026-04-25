using System.Text;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 简单的日志记录器 - 同时输出到控制台和文件
/// </summary>
internal static class Logger
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static bool _initialized;

    /// <summary>
    /// 初始化日志记录器
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var logDir = GetLogDirectory();
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logDir, $"launcher_{timestamp}.log");
                Console.WriteLine($"[Logger] Log file initialized: {_logFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Logger] Failed to initialize log file: {ex.Message}");
        }

        _initialized = true;
    }

    /// <summary>
    /// 获取日志文件路径
    /// </summary>
    public static string? GetLogFilePath()
    {
        return _logFilePath;
    }

    /// <summary>
    /// 获取日志目录
    /// </summary>
    private static string? GetLogDirectory()
    {
        try
        {
            var appRoot = Commands.ResolveAppRoot(CommandContext.FromArgs([]));
            var resolver = new DataLocationResolver(appRoot);
            return resolver.ResolveLauncherLogsPath();
        }
        catch
        {
        }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                return Path.Combine(appData, "LanMountainDesktop", "Launcher", "logs");
            }
        }
        catch
        {
        }

        try
        {
            var launcherDir = AppContext.BaseDirectory;
            return Path.Combine(launcherDir, "Launcher", "logs");
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// 记录信息日志
    /// </summary>
    public static void Info(string message)
    {
        WriteLog("INFO", message);
    }

    /// <summary>
    /// 记录警告日志
    /// </summary>
    public static void Warn(string message)
    {
        WriteLog("WARN", message);
    }

    /// <summary>
    /// 记录错误日志
    /// </summary>
    public static void Error(string message)
    {
        WriteLog("ERROR", message);
    }

    /// <summary>
    /// 记录错误日志（带异常）
    /// </summary>
    public static void Error(string message, Exception exception)
    {
        WriteLog("ERROR", $"{message}\n{exception}");
    }

    /// <summary>
    /// 写入日志
    /// </summary>
    private static void WriteLog(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] {message}";

        Console.WriteLine(logLine);

        if (string.IsNullOrEmpty(_logFilePath))
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
