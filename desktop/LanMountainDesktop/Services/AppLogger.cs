using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LanMountainDesktop.Services;

public static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static string _logDirectory = string.Empty;
    private static string _logFilePath = string.Empty;

    public static string LogDirectory
    {
        get
        {
            EnsureInitialized();
            return _logDirectory;
        }
    }

    public static string LogFilePath
    {
        get
        {
            EnsureInitialized();
            return _logFilePath;
        }
    }

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            var preferredDirectory = Path.Combine(AppContext.BaseDirectory, "log");
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop",
                "log");

            var preferredReady = TryPrepareDirectory(preferredDirectory, out var preferredError);
            var fallbackReady = false;
            string? fallbackError = null;

            if (preferredReady)
            {
                _logDirectory = preferredDirectory;
            }
            else
            {
                fallbackReady = TryPrepareDirectory(fallbackDirectory, out fallbackError);
                _logDirectory = fallbackReady ? fallbackDirectory : preferredDirectory;
            }

            _logFilePath = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
            _initialized = true;

            WriteCore(
                "INFO",
                "Logger",
                $"Initialized. Directory={_logDirectory}; File={_logFilePath}; PreferredDirectory={preferredDirectory}");

            if (!preferredReady && !string.IsNullOrWhiteSpace(preferredError))
            {
                WriteCore(
                    "WARN",
                    "Logger",
                    $"Failed to use program log directory '{preferredDirectory}'. Falling back to '{_logDirectory}'. Reason: {preferredError}");
            }

            if (!preferredReady && !fallbackReady && !string.IsNullOrWhiteSpace(fallbackError))
            {
                Trace.WriteLine(
                    $"[LanMountainDesktop][Logger][ERROR] Failed to initialize fallback log directory '{fallbackDirectory}': {fallbackError}");
            }
        }
    }

    public static void Info(string category, string message)
    {
        Write("INFO", category, message, null);
    }

    public static void Warn(string category, string message, Exception? exception = null)
    {
        Write("WARN", category, message, exception);
    }

    public static void Error(string category, string message, Exception? exception = null)
    {
        Write("ERROR", category, message, exception);
    }

    public static void Critical(string category, string message, Exception? exception = null)
    {
        Write("CRITICAL", category, message, exception);
    }

    private static void Write(string level, string category, string message, Exception? exception)
    {
        EnsureInitialized();

        var payload = exception is null
            ? message
            : $"{message}{Environment.NewLine}{exception}";
        WriteCore(level, category, payload);
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Initialize();
    }

    private static void WriteCore(string level, string category, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] [{category}] {message}";

        lock (SyncRoot)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LanMountainDesktop][Logger][ERROR] {ex}");
            }
        }

        Trace.WriteLine(line);
    }

    private static bool TryPrepareDirectory(string directory, out string? error)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
