using LanMountainDesktop.Launcher.Services;
using System.IO.Compression;
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

        var pluginsDirectory = CreateUserScopedPluginsDirectory();
        var service = new PluginInstallerService();

        var result = service.InstallPackage(packagePath, pluginsDirectory);

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
    public void InstallPackage_ReplacesExistingPackageWithSamePluginId()
    {
        Directory.CreateDirectory(_tempRoot);
        var firstPackagePath = Path.Combine(_tempRoot, "sample-1.laapp");
        var secondPackagePath = Path.Combine(_tempRoot, "sample-2.laapp");
        CreatePluginPackage(firstPackagePath, "plugin.json", "plugin.replace.sample", "Sample Plugin v1");
        CreatePluginPackage(secondPackagePath, "plugin.json", "plugin.replace.sample", "Sample Plugin v2");

        var pluginsDirectory = CreateUserScopedPluginsDirectory();
        var service = new PluginInstallerService();

        var first = service.InstallPackage(firstPackagePath, pluginsDirectory);
        var second = service.InstallPackage(secondPackagePath, pluginsDirectory);

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

        var pluginsDirectory = CreateUserScopedPluginsDirectory();
        var service = new PluginInstallerService();

        var result = service.InstallPackage(packagePath, pluginsDirectory);

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

    private static string CreateUserScopedPluginsDirectory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Tests",
            nameof(PluginInstallerServiceTests),
            Guid.NewGuid().ToString("N"),
            "Extensions",
            "Plugins");
        Directory.CreateDirectory(root);
        return root;
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
