using LanMountainDesktop.Launcher.Plugins;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PluginInstallerServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Tests", nameof(PluginInstallerServiceTests), Guid.NewGuid().ToString("N"));

    [Fact]
    public void InstallPackage_ReturnsElevationRequired_ForOutsideUserScope_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_tempRoot);
        var packagePath = Path.Combine(_tempRoot, "sample.lmdp");
        File.WriteAllText(packagePath, "placeholder");

        var service = new PluginInstallerService();
        var result = service.InstallPackage(packagePath, Path.Combine(_tempRoot, "Plugins"));

        Assert.False(result.Success);
        Assert.Equal("plugin_elevation_required", result.Code);
    }

    [Fact]
    public void InstallPackage_InstallsLaappWithPluginJson_InsideUserScope()
    {
        var packagePath = Path.Combine(_tempRoot, "sample.laapp");
        Directory.CreateDirectory(_tempRoot);
        CreatePluginPackage(packagePath, "plugin.json", "plugin.install.sample", "Sample Plugin");

        var pluginsDirectory = CreateConfiguredPortablePluginsDirectory(out var appRoot);
        var service = new PluginInstallerService();

        var result = service.InstallPackage(packagePath, pluginsDirectory, appRoot);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Code);
        Assert.Equal("plugin.install.sample", result.ManifestId);
        Assert.Equal("Sample Plugin", result.ManifestName);
        Assert.NotNull(result.InstalledPackagePath);
        Assert.True(File.Exists(result.InstalledPackagePath));
        Assert.EndsWith(".laapp", result.InstalledPackagePath, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(pluginsDirectory, "*.incoming", SearchOption.AllDirectories));
    }

    [Fact]
    public void InstallPackage_AllowsConfiguredPortableDataRootOutsideUserScope()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_tempRoot);
        var appRoot = Path.Combine(_tempRoot, "PackageRoot");
        var portableDataRoot = Path.Combine(appRoot, "Desktop");
        var launcherDataRoot = Path.Combine(appRoot, ".Launcher");
        Directory.CreateDirectory(launcherDataRoot);
        File.WriteAllText(
            Path.Combine(launcherDataRoot, "data-location.config.json"),
            JsonSerializer.Serialize(new
            {
                DataLocationMode = "Portable",
                SystemDataPath = Path.Combine(_tempRoot, "System"),
                PortableDataPath = portableDataRoot
            }));

        var packagePath = Path.Combine(_tempRoot, "portable.laapp");
        CreatePluginPackage(packagePath, "plugin.json", "plugin.portable.sample", "Portable Plugin");

        var pluginsDirectory = Path.Combine(portableDataRoot, "Extensions", "Plugins");
        var service = new PluginInstallerService();

        var result = service.InstallPackage(packagePath, pluginsDirectory, appRoot);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Code);
        Assert.True(File.Exists(result.InstalledPackagePath));
        Assert.StartsWith(Path.GetFullPath(portableDataRoot), Path.GetFullPath(result.InstalledPackagePath!), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallPackage_ReplacesExistingPackageWithSamePluginId()
    {
        Directory.CreateDirectory(_tempRoot);
        var firstPackagePath = Path.Combine(_tempRoot, "sample-1.laapp");
        var secondPackagePath = Path.Combine(_tempRoot, "sample-2.laapp");
        CreatePluginPackage(firstPackagePath, "plugin.json", "plugin.replace.sample", "Sample Plugin v1");
        CreatePluginPackage(secondPackagePath, "plugin.json", "plugin.replace.sample", "Sample Plugin v2");

        var pluginsDirectory = CreateConfiguredPortablePluginsDirectory(out var appRoot);
        var service = new PluginInstallerService();

        var first = service.InstallPackage(firstPackagePath, pluginsDirectory, appRoot);
        var second = service.InstallPackage(secondPackagePath, pluginsDirectory, appRoot);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Single(Directory.EnumerateFiles(pluginsDirectory, "*.laapp", SearchOption.TopDirectoryOnly));
        Assert.True(File.Exists(second.InstalledPackagePath));
    }

    [Fact]
    public void InstallPackage_StillSupportsLegacyManifestJson()
    {
        var packagePath = Path.Combine(_tempRoot, "legacy.lmdp");
        Directory.CreateDirectory(_tempRoot);
        CreatePluginPackage(packagePath, "manifest.json", "plugin.legacy.sample", "Legacy Plugin");

        var pluginsDirectory = CreateConfiguredPortablePluginsDirectory(out var appRoot);
        var service = new PluginInstallerService();

        var result = service.InstallPackage(packagePath, pluginsDirectory, appRoot);

        Assert.True(result.Success);
        Assert.Equal("plugin.legacy.sample", result.ManifestId);
        Assert.True(File.Exists(result.InstalledPackagePath));
    }

    private static void CreatePluginPackage(string packagePath, string manifestFileName, string pluginId, string pluginName)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(manifestFileName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(
            $$"""
              {
                "id": "{{pluginId}}",
                "name": "{{pluginName}}",
                "version": "1.0.0"
              }
              """);
    }

    private string CreateConfiguredPortablePluginsDirectory(out string appRoot)
    {
        appRoot = Path.Combine(_tempRoot, "ConfiguredPackageRoot", Guid.NewGuid().ToString("N"));
        var portableDataRoot = Path.Combine(appRoot, "Desktop");
        var launcherDataRoot = Path.Combine(appRoot, ".Launcher");
        Directory.CreateDirectory(launcherDataRoot);
        File.WriteAllText(
            Path.Combine(launcherDataRoot, "data-location.config.json"),
            JsonSerializer.Serialize(new
            {
                DataLocationMode = "Portable",
                SystemDataPath = Path.Combine(_tempRoot, "System"),
                PortableDataPath = portableDataRoot
            }));

        var pluginsDirectory = Path.Combine(portableDataRoot, "Extensions", "Plugins");
        Directory.CreateDirectory(pluginsDirectory);
        return pluginsDirectory;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
    }
}
