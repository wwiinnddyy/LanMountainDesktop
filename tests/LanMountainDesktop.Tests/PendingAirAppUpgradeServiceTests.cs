using System.IO.Compression;
using LanMountainDesktop.AirAppPackaging;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PendingAirAppUpgradeServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(PendingAirAppUpgradeServiceTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddPendingInstallOrUpgrade_ReplacesExistingOperationForSameAirApp()
    {
        var airAppsDirectory = CreateAirAppsDirectory();
        var firstPackage = CreateAirAppPackage("first.laapp", "plugin.queue.sample", "Sample AirApp", "1.0.0");
        var secondPackage = CreateAirAppPackage("second.laapp", "plugin.queue.sample", "Sample AirApp", "2.0.0");
        var service = new PendingAirAppUpgradeService(airAppsDirectory);

        service.AddPendingInstallOrUpgrade("plugin.queue.sample", firstPackage, "1.0.0");
        service.AddPendingInstallOrUpgrade("plugin.queue.sample", secondPackage, "2.0.0");

        var pending = service.GetPendingUpgrades();
        var operation = Assert.Single(pending);
        Assert.Equal("plugin.queue.sample", operation.PluginId);
        Assert.Equal("2.0.0", operation.TargetVersion);
        Assert.Equal(PendingAirAppOperation.InstallOrUpgrade, operation.Operation);
        Assert.Equal(Path.GetFullPath(secondPackage), operation.SourcePackagePath);
    }

    [Fact]
    public void ApplyPendingOperations_InstallsPackageAndClearsSuccessfulOperation()
    {
        var airAppsDirectory = CreateAirAppsDirectory();
        var packagePath = CreateAirAppPackage("sample.laapp", "plugin.install.queue", "Queued AirApp", "1.0.0");
        var service = new PendingAirAppUpgradeService(airAppsDirectory);
        service.AddPendingInstallOrUpgrade("plugin.install.queue", packagePath, "1.0.0");

        var result = service.ApplyPendingOperations();

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.True(File.Exists(Path.Combine(airAppsDirectory, "plugin.install.queue.laapp")));
        Assert.Empty(service.GetPendingUpgrades());
    }

    [Fact]
    public void ApplyPendingOperations_ReplacesExistingPackageWithSameAirAppId()
    {
        var airAppsDirectory = CreateAirAppsDirectory();
        var firstPackage = CreateAirAppPackage("first.laapp", "plugin.replace.queue", "Old AirApp", "1.0.0");
        var secondPackage = CreateAirAppPackage("second.laapp", "plugin.replace.queue", "New AirApp", "2.0.0");
        File.Copy(firstPackage, Path.Combine(airAppsDirectory, "plugin.replace.queue.laapp"));

        var service = new PendingAirAppUpgradeService(airAppsDirectory);
        service.AddPendingInstallOrUpgrade("plugin.replace.queue", secondPackage, "2.0.0");

        var result = service.ApplyPendingOperations();

        Assert.Equal(1, result.SuccessCount);
        var installedPackages = Directory.EnumerateFiles(airAppsDirectory, "*.laapp", SearchOption.TopDirectoryOnly).ToArray();
        var installedPackage = Assert.Single(installedPackages);
        var manifest = ReadManifestFromPackage(installedPackage);
        Assert.Equal("plugin.replace.queue", manifest.Id);
        Assert.Equal("New AirApp", manifest.Name);
        Assert.Equal("2.0.0", manifest.Version);
    }

    [Fact]
    public void ApplyPendingOperations_KeepsFailedOperationQueued()
    {
        var airAppsDirectory = CreateAirAppsDirectory();
        var invalidPackage = Path.Combine(_tempRoot, "invalid.laapp");
        Directory.CreateDirectory(_tempRoot);
        using (ZipFile.Open(invalidPackage, ZipArchiveMode.Create))
        {
        }

        var service = new PendingAirAppUpgradeService(airAppsDirectory);
        service.AddPendingInstallOrUpgrade("plugin.invalid.queue", invalidPackage, "1.0.0");

        var result = service.ApplyPendingOperations();

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(service.GetPendingUpgrades());
    }

    [Fact]
    public void ApplyPendingOperations_KeepsMissingPackageOperationQueued()
    {
        var airAppsDirectory = CreateAirAppsDirectory();
        var missingPackage = Path.Combine(_tempRoot, "missing.laapp");
        var service = new PendingAirAppUpgradeService(airAppsDirectory);
        service.AddPendingInstallOrUpgrade("plugin.missing.queue", missingPackage, "1.0.0");

        var result = service.ApplyPendingOperations();

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(service.GetPendingUpgrades());
    }

    private string CreateAirAppsDirectory()
    {
        var directory = Path.Combine(_tempRoot, "Extensions", "AirApps");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private string CreateAirAppPackage(string fileName, string airAppId, string pluginName, string version)
    {
        Directory.CreateDirectory(_tempRoot);
        var packagePath = Path.Combine(_tempRoot, fileName);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(AirAppSdkInfo.ManifestFileName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(
            $$"""
              {
                "id": "{{airAppId}}",
                "name": "{{pluginName}}",
                "version": "{{version}}",
                "apiVersion": "1.0.0",
                "entranceAssembly": "{{airAppId}}.dll"
              }
              """);
        return packagePath;
    }

    private static LanMountainDesktop.AirAppSdk.AirAppManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(AirAppSdkInfo.ManifestFileName)
            ?? throw new InvalidOperationException("Missing plugin manifest.");
        using var stream = entry.Open();
        return LanMountainDesktop.AirAppSdk.AirAppManifest.Load(stream, $"{packagePath}!/{entry.FullName}");
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
