using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class DataLocationResolver
{
    private const string ConfigFileName = "data-location.config.json";
    private const string PortableDataFolderName = "AppData";

    private readonly string _appRoot;
    private readonly string _configPath;
    private readonly string _defaultSystemDataPath;

    public DataLocationResolver(string appRoot)
    {
        _appRoot = Path.GetFullPath(appRoot);
        _configPath = Path.Combine(_appRoot, ConfigFileName);
        _defaultSystemDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
    }

    public string AppRoot => _appRoot;

    public string ConfigPath => _configPath;

    public string DefaultSystemDataPath => _defaultSystemDataPath;

    public string DefaultPortableDataPath => Path.Combine(_appRoot, PortableDataFolderName);

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

    public DataLocationConfig? LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return null;
            }

            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.DataLocationConfig);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load data location config from '{_configPath}'. Error='{ex.Message}'.");
            return null;
        }
    }

    public bool SaveConfig(DataLocationConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.DataLocationConfig);
            File.WriteAllText(_configPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to save data location config to '{_configPath}'. Error='{ex.Message}'.");
            return false;
        }
    }

    public string ResolveDataRoot()
    {
        var config = LoadConfig();
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

    public bool HasExistingSystemData()
    {
        var systemPath = _defaultSystemDataPath;
        if (!Directory.Exists(systemPath))
        {
            return false;
        }

        var markerFiles = new[]
        {
            Path.Combine(systemPath, "settings.json"),
            Path.Combine(systemPath, "launcher-settings.json"),
            Path.Combine(systemPath, "component-state.db"),
            Path.Combine(systemPath, "app.db")
        };

        return markerFiles.Any(File.Exists);
    }

    public bool ApplyLocationChoice(DataLocationMode mode, bool migrateExistingData)
    {
        var config = new DataLocationConfig
        {
            DataLocationMode = mode.ToString(),
            SystemDataPath = _defaultSystemDataPath,
            PortableDataPath = DefaultPortableDataPath
        };

        if (!SaveConfig(config))
        {
            return false;
        }

        var targetDataRoot = mode == DataLocationMode.Portable
            ? DefaultPortableDataPath
            : _defaultSystemDataPath;

        try
        {
            Directory.CreateDirectory(targetDataRoot);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to create data directory '{targetDataRoot}'. Error='{ex.Message}'.");
            return false;
        }

        if (migrateExistingData && mode == DataLocationMode.Portable)
        {
            MigrateSystemDataToPortable();
        }

        return true;
    }

    private void MigrateSystemDataToPortable()
    {
        if (!HasExistingSystemData())
        {
            return;
        }

        var sourcePath = _defaultSystemDataPath;
        var targetPath = DefaultPortableDataPath;

        try
        {
            Directory.CreateDirectory(targetPath);

            var filesToMigrate = Directory.GetFiles(sourcePath, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in filesToMigrate)
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(targetPath, fileName);
                try
                {
                    File.Copy(file, destFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to migrate file '{fileName}'. Error='{ex.Message}'.");
                }
            }

            var dirsToMigrate = Directory.GetDirectories(sourcePath, "*", SearchOption.TopDirectoryOnly);
            foreach (var dir in dirsToMigrate)
            {
                var dirName = Path.GetFileName(dir);
                if (string.Equals(dirName, ".launcher", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileName(sourcePath), "LanMountainDesktop", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destDir = Path.Combine(targetPath, dirName);
                try
                {
                    CopyDirectory(dir, destDir);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to migrate directory '{dirName}'. Error='{ex.Message}'.");
                }
            }

            Logger.Info($"Data migration completed. Source='{sourcePath}'; Target='{targetPath}'.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Data migration failed. Source='{sourcePath}'; Target='{targetPath}'. Error='{ex.Message}'.");
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
