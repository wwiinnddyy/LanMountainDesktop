using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 解析应用数据目录位置。
/// </summary>
/// <remarks>
/// 安装后的目录结构：
/// <code>
/// {AppRoot}/                          ← 应用安装根目录
///   LanMountainDesktop.Launcher.exe   ← Launcher 可执行文件
///   .Launcher/                        ← Launcher 数据目录（日志、状态、配置等）
///   app-{version}/                    ← Host 部署目录
///     LanMountainDesktop.exe
///     ...
/// </code>
///
/// Launcher 数据目录固定位于应用安装根目录下的 <c>.Launcher</c> 文件夹中，
/// 与 app-* 部署目录同级。此目录不随数据位置模式改变。
///
/// Desktop（Host）数据目录则根据用户选择可位于系统目录或便携目录。
/// </remarks>
internal sealed class DataLocationResolver
{
    private const string ConfigFileName = "data-location.config.json";
    private const string DesktopFolderName = "Desktop";
    private const string LauncherDataFolderName = ".Launcher";

    private readonly string _appRoot;
    private readonly string _defaultSystemDataPath;

    public DataLocationResolver(string appRoot)
    {
        _appRoot = Path.GetFullPath(appRoot);
        _defaultSystemDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
    }

    public string AppRoot => _appRoot;

    /// <summary>
    /// 默认系统数据路径（用户目录）
    /// </summary>
    public string DefaultSystemDataPath => _defaultSystemDataPath;

    /// <summary>
    /// 默认便携模式数据路径（应用目录下的 Desktop 文件夹）
    /// </summary>
    public string DefaultPortableDataPath => Path.Combine(_appRoot, DesktopFolderName);

    /// <summary>
    /// Launcher 数据目录，固定位于应用安装根目录下的 .Launcher 文件夹。
    /// 该目录与 app-* 部署目录同级，不随数据位置模式改变。
    /// </summary>
    public string ResolveLauncherDataPath()
    {
        return Path.Combine(_appRoot, LauncherDataFolderName);
    }

    /// <summary>
    /// 桌面应用数据目录（组件、设置、插件等）
    /// </summary>
    public string ResolveDesktopDataPath()
    {
        return Path.Combine(ResolveDataRoot(), DesktopFolderName);
    }

    /// <summary>
    /// 数据位置配置文件路径（保存在 Launcher 数据目录下）
    /// </summary>
    public string ResolveConfigPath()
    {
        return Path.Combine(ResolveLauncherDataPath(), ConfigFileName);
    }

    /// <summary>
    /// 启动器日志目录
    /// </summary>
    public string ResolveLauncherLogsPath()
    {
        return Path.Combine(ResolveLauncherDataPath(), "logs");
    }

    /// <summary>
    /// 启动器状态目录
    /// </summary>
    public string ResolveLauncherStatePath()
    {
        return Path.Combine(ResolveLauncherDataPath(), "state");
    }

    /// <summary>
    /// 检查是否允许便携模式（应用目录是否可写）
    /// </summary>
    public bool IsPortableModeAllowed()
    {
        try
        {
            var testFile = Path.Combine(_appRoot, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, string.Empty);
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public DataLocationMode ResolveMode()
    {
        var config = LoadConfig();
        if (config is null)
        {
            return DataLocationMode.System;
        }

        return string.Equals(config.DataLocationMode, "Portable", StringComparison.OrdinalIgnoreCase)
            ? DataLocationMode.Portable
            : DataLocationMode.System;
    }

    /// <summary>
    /// 解析数据根目录（用户选择的位置）
    /// </summary>
    public string ResolveDataRoot()
    {
        var config = LoadConfig();
        return ResolveDataRoot(config);
    }

    private string ResolveDataRoot(DataLocationConfig? config)
    {
        if (config is null)
        {
            return _defaultSystemDataPath;
        }

        if (string.Equals(config.DataLocationMode, "Portable", StringComparison.OrdinalIgnoreCase))
        {
            var portablePath = !string.IsNullOrWhiteSpace(config.PortableDataPath)
                ? config.PortableDataPath
                : DefaultPortableDataPath;
            return Path.GetFullPath(portablePath);
        }

        return !string.IsNullOrWhiteSpace(config.SystemDataPath)
            ? Path.GetFullPath(config.SystemDataPath)
            : _defaultSystemDataPath;
    }

    public DataLocationConfig? LoadConfig()
    {
        try
        {
            var configPath = ResolveConfigPath();
            if (!File.Exists(configPath))
            {
                return null;
            }

            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.DataLocationConfig);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load data location config. Error='{ex.Message}'.");
            return null;
        }
    }

    public bool SaveConfig(DataLocationConfig config)
    {
        try
        {
            var launcherDataPath = ResolveLauncherDataPath();
            Directory.CreateDirectory(launcherDataPath);

            var configPath = ResolveConfigPath();
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.DataLocationConfig);
            File.WriteAllText(configPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to save data location config. Error='{ex.Message}'.");
            return false;
        }
    }

    public bool ApplyLocationChoice(DataLocationMode mode, string? customPath = null, bool migrateExistingData = false)
    {
        var targetDataRoot = mode == DataLocationMode.Portable && !string.IsNullOrWhiteSpace(customPath)
            ? Path.GetFullPath(customPath)
            : _defaultSystemDataPath;

        var config = new DataLocationConfig
        {
            DataLocationMode = mode.ToString(),
            SystemDataPath = _defaultSystemDataPath,
            PortableDataPath = mode == DataLocationMode.Portable ? targetDataRoot : null
        };

        // 先创建目录结构
        try
        {
            Directory.CreateDirectory(ResolveLauncherDataPath());
            Directory.CreateDirectory(Path.Combine(ResolveDataRoot(config), DesktopFolderName));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to create data directories. Error='{ex.Message}'.");
            return false;
        }

        // 保存配置
        if (!SaveConfig(config))
        {
            return false;
        }

        if (migrateExistingData && mode == DataLocationMode.Portable)
        {
            MigrateSystemDataToPortable(targetDataRoot);
        }

        return true;
    }

    public bool HasExistingSystemData()
    {
        var desktopPath = Path.Combine(_defaultSystemDataPath, DesktopFolderName);
        if (!Directory.Exists(desktopPath))
        {
            return false;
        }

        var markerFiles = new[]
        {
            Path.Combine(desktopPath, "settings.json"),
            Path.Combine(desktopPath, "component-state.db"),
            Path.Combine(desktopPath, "app.db")
        };

        return markerFiles.Any(File.Exists);
    }

    private void MigrateSystemDataToPortable(string targetDataRoot)
    {
        if (!HasExistingSystemData())
        {
            return;
        }

        var sourceDesktopPath = Path.Combine(_defaultSystemDataPath, DesktopFolderName);
        var targetDesktopPath = Path.Combine(targetDataRoot, DesktopFolderName);

        try
        {
            Directory.CreateDirectory(targetDesktopPath);

            // 迁移桌面数据
            if (Directory.Exists(sourceDesktopPath))
            {
                CopyDirectory(sourceDesktopPath, targetDesktopPath);
            }

            Logger.Info($"Data migration completed. Target='{targetDataRoot}'.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Data migration failed. Target='{targetDataRoot}'. Error='{ex.Message}'.");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
