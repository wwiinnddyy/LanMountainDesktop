namespace LanMountainDesktop.Launcher.Services;

internal sealed class OobeStateService
{
    private readonly string _markerPath;

    public OobeStateService(string appRoot)
    {
        // 优先使用 LocalApplicationData（用户目录，普通用户一定有权限）
        string? stateDir = null;
        Exception? lastException = null;

        // 策略1: LocalApplicationData（首选，用户目录，普通用户一定有写权限）
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop");
            stateDir = Path.Combine(appDataDir, ".launcher", "state");
            Directory.CreateDirectory(stateDir);
            Console.WriteLine($"[OobeStateService] Using LocalApplicationData: {stateDir}");
        }
        catch (Exception ex)
        {
            lastException = ex;
            Console.Error.WriteLine($"[OobeStateService] LocalApplicationData failed: {ex.Message}");
            stateDir = null;
        }

        // 策略2: 如果LocalApplicationData不行，使用用户的临时目录
        if (stateDir == null)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "LanMountainDesktop", ".launcher", "state");
                Directory.CreateDirectory(tempDir);
                stateDir = tempDir;
                Console.WriteLine($"[OobeStateService] Using TempPath: {stateDir}");
            }
            catch (Exception ex)
            {
                lastException = ex;
                Console.Error.WriteLine($"[OobeStateService] TempPath failed: {ex.Message}");
                stateDir = null;
            }
        }

        // 策略3: 最后的兜底：使用当前用户的应用程序数据目录（和Launcher同目录
        if (stateDir == null)
        {
            try
            {
                var launcherDir = AppContext.BaseDirectory;
                stateDir = Path.Combine(launcherDir, ".launcher", "state");
                Directory.CreateDirectory(stateDir);
                Console.WriteLine($"[OobeStateService] Using Launcher directory: {stateDir}");
            }
            catch (Exception ex)
            {
                lastException = ex;
                Console.Error.WriteLine($"[OobeStateService] All strategies failed! Last error: {ex.Message}");
                // 如果所有策略都失败，抛出异常让上层处理
                throw new InvalidOperationException("无法创建 OOBE 状态存储目录失败", lastException);
            }
        }

        _markerPath = Path.Combine(stateDir, "first_run_completed");
        Console.WriteLine($"[OobeStateService] Initialized successfully, marker path: {_markerPath}");
    }

    public bool IsFirstRun()
    {
        try
        {
            return !File.Exists(_markerPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeStateService] Failed to check first run: {ex.Message}");
            // 如果无法检查，默认视为首次运行，确保OOBE能显示
            return true;
        }
    }

    public void MarkCompleted()
    {
        try
        {
            var dir = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
            Console.WriteLine("[OobeStateService] Marked first run as completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeStateService] Failed to mark completed: {ex.Message}");
            // 如果无法写入也没关系，下次启动还会显示OOBE
        }
    }
}
