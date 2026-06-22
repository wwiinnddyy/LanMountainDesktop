using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using LanMountainDesktop.DesktopHost;
using LanMountainDesktop.Models;
using LanMountainDesktop.Plugins;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop;

public sealed class Program
{
    internal static string StartupRenderMode { get; private set; } = AppRenderingModeHelper.Default;

    private const string DisablePatchersEnvVar = "LAN_DESKTOP_DISABLE_PATCHERS";
    private const string DisableRenderRetryEnvVar = "LAN_DESKTOP_DISABLE_RENDER_RETRY";

    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        AppDataPathProvider.Initialize(args);
        DevPluginOptions.Parse(args);
        RegisterGlobalExceptionLogging();

        DesktopBootstrap.InitializeStartupServices(
            InitializeTelemetryIdentity,
            InitializeCrashTelemetry,
            InitializeUsageTelemetry,
            ScheduleWhiteboardNoteStartupCleanup);

        var diagnostics = StartupDiagnosticsService.Run(args);
        StartupDiagnosticsService.ShowLegacyExecutableWarningIfNeeded(diagnostics);

        var attemptedRenderModes = new List<string>();
        try
        {
            var renderMode = LoadConfiguredRenderMode();
            StartupRenderMode = renderMode;
            attemptedRenderModes.Add(renderMode);
            AppLogger.Info("Startup", $"Resolved render mode '{renderMode}'.");
            LoadChromePatchState();
            InstallChromePatchersIfNeeded();
            BuildAvaloniaApp(renderMode).StartWithClassicDesktopLifetime(args);
            AppLogger.Info("Startup", "Application exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Critical("Startup", "Application terminated during startup.", ex);
            WriteCrashDump(ex, StartupRenderMode);

            // 渲染模式安全降级：若失败且未禁用重试，且当前不是软件渲染，则用软件渲染重试一次
            if (ShouldRetryWithSoftwareRendering(StartupRenderMode, ex) &&
                !attemptedRenderModes.Contains(AppRenderingModeHelper.Software))
            {
                AppLogger.Warn("Startup", $"Retrying startup with Software rendering mode (previous='{StartupRenderMode}').");
                StartupRenderMode = AppRenderingModeHelper.Software;
                try
                {
                    BuildAvaloniaApp(AppRenderingModeHelper.Software)
                        .StartWithClassicDesktopLifetime(args);
                    AppLogger.Info("Startup", "Application exited normally after Software render retry.");
                    return;
                }
                catch (Exception retryEx)
                {
                    AppLogger.Critical("Startup", "Software render retry also failed.", retryEx);
                    WriteCrashDump(retryEx, AppRenderingModeHelper.Software);
                    throw;
                }
            }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(AppRenderingModeHelper.Default);
    }

    public static AppBuilder BuildAvaloniaApp(string renderMode)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            var configuredModes = AppRenderingModeHelper.GetWin32RenderingModes(renderMode);
            if (configuredModes is { Length: > 0 })
            {
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = configuredModes
                });
            }
        }

        return builder;
    }

    private static void ScheduleWhiteboardNoteStartupCleanup()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var deletedCount = new WhiteboardNotePersistenceService().DeleteExpiredNotesBatch(batchSize: 512);
                if (deletedCount > 0)
                {
                    AppLogger.Info("Startup", $"Deleted {deletedCount} expired whiteboard notes during startup maintenance.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Startup", "Failed to run whiteboard note startup maintenance.", ex);
            }
        });
    }

    private static string LoadConfiguredRenderMode()
    {
        try
        {
            var snapshot = HostSettingsFacadeProvider.GetOrCreate()
                .Settings
                .LoadSnapshot<AppSettingsSnapshot>(LanMountainDesktop.PluginSdk.SettingsScope.App);
            return AppRenderingModeHelper.Normalize(snapshot.AppRenderMode);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to load configured render mode. Falling back to default.", ex);
            return AppRenderingModeHelper.Default;
        }
    }

    private static void LoadChromePatchState()
    {
        try
        {
            var snapshot = HostSettingsFacadeProvider.GetOrCreate()
                .Settings
                .LoadSnapshot<AppSettingsSnapshot>(LanMountainDesktop.PluginSdk.SettingsScope.App);
            if (OperatingSystem.IsWindows())
            {
                LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome = snapshot.UseSystemChrome;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to load chrome patch state. Falling back to FA chrome.", ex);
        }
    }

    private static void InstallChromePatchersIfNeeded()
    {
        // 紧急关闭开关：设置环境变量 LAN_DESKTOP_DISABLE_PATCHERS=1 可跳过所有 patcher 安装
        // 用于诊断 patcher 导致启动崩溃的问题
        if (Environment.GetEnvironmentVariable(DisablePatchersEnvVar) == "1")
        {
            AppLogger.Warn("Startup", $"Chrome patchers skipped by environment variable '{DisablePatchersEnvVar}=1'.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        if (arch != System.Runtime.InteropServices.Architecture.X64 &&
            arch != System.Runtime.InteropServices.Architecture.X86)
        {
            return;
        }

        try
        {
            LanMountainDesktop.Platform.Windows.PatcherEntrance.InstallPatchers();
            AppLogger.Info("Startup", $"Chrome patchers installed. UseSystemChrome={LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to install chrome patchers.", ex);
        }
    }

    /// <summary>
    /// 判断是否应该用软件渲染重试。当异常看起来与渲染相关（GPU/驱动/平台初始化），
    /// 且当前渲染模式不是软件渲染，且未通过环境变量禁用重试时返回 true。
    /// </summary>
    private static bool ShouldRetryWithSoftwareRendering(string currentRenderMode, Exception ex)
    {
        if (string.Equals(currentRenderMode, AppRenderingModeHelper.Software, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Environment.GetEnvironmentVariable(DisableRenderRetryEnvVar) == "1")
        {
            return false;
        }

        // 渲染相关异常的特征关键字（覆盖 Avalonia Compositor / GPU / Vulkan / Wgl / Angle 等）
        var message = (ex.Message ?? string.Empty) + " " + (ex.GetType().FullName ?? string.Empty);
        var renderedKeywords = new[]
        {
            "render", "gpu", "vulkan", "wgl", "angle", "egl", "opengl",
            "compositor", "surface", "swapchain", "driver", "directx",
            "Win32PlatformOptions", "RenderingMode"
        };

        foreach (var keyword in renderedKeywords)
        {
            if (message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        // Avalonia 初始化阶段抛出的 TypeLoadException / InvalidOperationException 也尝试降级
        if (ex is InvalidOperationException or TypeLoadException or TypeInitializationException)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 将崩溃信息写入崩溃转储文件，供启动器读取并展示给用户。
    /// 文件位于 LocalApplicationData/LanMountainDesktop/crashes/ 目录下。
    /// </summary>
    private static void WriteCrashDump(Exception ex, string renderMode)
    {
        try
        {
            var crashDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop", "crashes");
            Directory.CreateDirectory(crashDir);

            var crashFile = Path.Combine(crashDir, $"crash-{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var sb = new StringBuilder();
            sb.AppendLine($"Time: {DateTime.Now:O}");
            sb.AppendLine($"RenderMode: {renderMode}");
            sb.AppendLine($"ProcessId: {Environment.ProcessId}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"OSArchitecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            sb.AppendLine($".NET: {Environment.Version}");
            sb.AppendLine($"WorkingDirectory: {Environment.CurrentDirectory}");
            sb.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
            sb.AppendLine();
            sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
            sb.AppendLine($"Exception Message: {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(ex.StackTrace ?? "<no stack trace>");
            sb.AppendLine();

            if (ex.InnerException is not null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"  Type: {ex.InnerException.GetType().FullName}");
                sb.AppendLine($"  Message: {ex.InnerException.Message}");
                sb.AppendLine($"  Stack Trace:");
                sb.AppendLine(ex.InnerException.StackTrace ?? "<no stack trace>");
            }

            // 保留最近的崩溃转储（最多 10 个），避免无限增长
            CleanupOldCrashDumps(crashDir);

            File.WriteAllText(crashFile, sb.ToString(), System.Text.Encoding.UTF8);
            AppLogger.Info("Startup", $"Crash dump written to {crashFile}");

            // 同时写入一个 latest 标记文件，方便启动器快速定位
            var latestMarker = Path.Combine(crashDir, "latest.txt");
            File.WriteAllText(latestMarker, crashFile, System.Text.Encoding.UTF8);
        }
        catch (Exception dumpEx)
        {
            try
            {
                AppLogger.Warn("Startup", "Failed to write crash dump.", dumpEx);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void CleanupOldCrashDumps(string crashDir)
    {
        try
        {
            var files = Directory.GetFiles(crashDir, "crash-*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(10)
                .ToArray();
            foreach (var file in files)
            {
                try { file.Delete(); }
                catch { /* 忽略单个文件删除失败 */ }
            }
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }

    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var exception = eventArgs.ExceptionObject as Exception
                ?? new Exception(eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception.");

            AppLogger.Critical(
                "UnhandledException",
                $"Unhandled exception. IsTerminating={eventArgs.IsTerminating}",
                exception);

            // 运行时未处理异常也写入崩溃转储，供启动器读取
            if (eventArgs.IsTerminating)
            {
                WriteCrashDump(exception, StartupRenderMode);
            }

            try
            {
                TelemetryServices.Crash?.CaptureUnhandledException(
                    exception,
                    "AppDomain.UnhandledException",
                    eventArgs.IsTerminating);
            }
            catch (Exception telemetryException)
            {
                AppLogger.Warn("UnhandledException", "Failed to forward unhandled exception to crash telemetry.", telemetryException);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppLogger.Error("TaskScheduler", "Unobserved task exception.", eventArgs.Exception);

            try
            {
                TelemetryServices.Crash?.CaptureTaskException(
                    eventArgs.Exception,
                    "TaskScheduler.UnobservedTaskException");
            }
            catch (Exception telemetryException)
            {
                AppLogger.Warn("TaskScheduler", "Failed to forward task exception to crash telemetry.", telemetryException);
            }

            eventArgs.SetObserved();
        };
    }

    private static void InitializeTelemetryIdentity()
    {
        try
        {
            TelemetryIdentityService.Initialize(HostSettingsFacadeProvider.GetOrCreate());
            AppLogger.Info(
                "Startup",
                $"Telemetry identity initialized. InstallId={TelemetryIdentityService.Instance.InstallId}; TelemetryId={TelemetryIdentityService.Instance.TelemetryId}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize telemetry identity service.", ex);
        }
    }

    private static void InitializeCrashTelemetry()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var crashTelemetry = new SentryCrashTelemetryService(settingsFacade);
            TelemetryServices.Crash = crashTelemetry;
            crashTelemetry.Initialize();
            AppLogger.Info("Startup", $"Crash telemetry initialized. Enabled={crashTelemetry.IsEnabled}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize crash telemetry service.", ex);
        }
    }

    private static void InitializeUsageTelemetry()
    {
        try
        {
            var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
            var usageTelemetry = new PostHogUsageTelemetryService(settingsFacade);
            TelemetryServices.Usage = usageTelemetry;
            usageTelemetry.Initialize();
            AppLogger.Info("Startup", $"Usage telemetry initialized. Enabled={usageTelemetry.IsUsageEnabled}.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "Failed to initialize usage telemetry service.", ex);
        }
    }
}
