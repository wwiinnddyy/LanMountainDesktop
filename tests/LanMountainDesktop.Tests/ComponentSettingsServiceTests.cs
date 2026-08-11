using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentSettingsServiceTests
{
    [Fact]
    public void Load_MigratesLegacySnapshotFileToCanonicalDocument()
    {
        using var sandbox = new ComponentSettingsSandbox();
        File.WriteAllText(
            sandbox.SettingsPath,
            """
            {
              "DesktopClockSecondHandMode": "Sweep",
              "ImportedClassSchedules": [
                {
                  "Id": "spring-2026",
                  "DisplayName": "Spring 2026",
                  "FilePath": "C:\\Schedules\\spring-2026.yaml"
                }
              ],
              "ActiveImportedClassScheduleId": "spring-2026"
            }
            """);

        var service = sandbox.CreateService();

        var snapshot = service.Load();

        Assert.Equal("Sweep", snapshot.DesktopClockSecondHandMode);
        Assert.Single(snapshot.ImportedClassSchedules);

        Assert.True(File.Exists(sandbox.DatabasePath));
        Assert.False(File.Exists(sandbox.SettingsPath));
        Assert.True(File.Exists(sandbox.SettingsBackupPath));

        ComponentSettingsService.ResetCacheForTests();
        var reloadedService = sandbox.CreateService();
        var reloaded = reloadedService.Load();
        Assert.Equal("Sweep", reloaded.DesktopClockSecondHandMode);
    }

    [Fact]
    public void Load_ReadsPascalCaseDocumentAndRewritesToCanonicalDocument()
    {
        using var sandbox = new ComponentSettingsSandbox();
        File.WriteAllText(
            sandbox.SettingsPath,
            """
            {
              "DefaultSettings": {
                "DesktopClockSecondHandMode": "Tick"
              },
              "InstanceSettings": {
                "DesktopClock::clock-2x2": {
                  "DesktopClockSecondHandMode": "Sweep"
                }
              },
              "PluginSettings": {
                "DesktopClock::clock-2x2": {
                  "SampleFlag": true
                }
              }
            }
            """);

        var service = sandbox.CreateService();

        var snapshot = service.LoadForComponent("DesktopClock", "clock-2x2");
        var pluginSettings = service.LoadPluginSettings<SamplePluginSettings>("DesktopClock", "clock-2x2");

        Assert.Equal("Sweep", snapshot.DesktopClockSecondHandMode);
        Assert.True(pluginSettings.SampleFlag);

        Assert.True(File.Exists(sandbox.DatabasePath));
        Assert.False(File.Exists(sandbox.SettingsPath));
        Assert.True(File.Exists(sandbox.SettingsBackupPath));

        ComponentSettingsService.ResetCacheForTests();
        var reloadedService = sandbox.CreateService();
        var reloadedSnapshot = reloadedService.LoadForComponent("DesktopClock", "clock-2x2");
        var reloadedPluginSettings = reloadedService.LoadPluginSettings<SamplePluginSettings>("DesktopClock", "clock-2x2");
        Assert.Equal("Sweep", reloadedSnapshot.DesktopClockSecondHandMode);
        Assert.True(reloadedPluginSettings.SampleFlag);
    }

    [Fact]
    public void SaveForComponent_RoundTripsInstanceAndPluginSettingsAcrossNewService()
    {
        using var sandbox = new ComponentSettingsSandbox();
        var service = sandbox.CreateService();

        service.SaveForComponent(
            "DesktopClock",
            "clock-2x2",
            new ComponentSettingsSnapshot
            {
                DesktopClockSecondHandMode = "Sweep"
            });
        service.SaveForComponent(
            "DesktopClassSchedule",
            "class-schedule-2x2",
            new ComponentSettingsSnapshot
            {
                ImportedClassSchedules =
                [
                    new ImportedClassScheduleSnapshot
                    {
                        Id = "spring-2026",
                        DisplayName = "Spring 2026",
                        FilePath = "C:\\Schedules\\spring-2026.yaml"
                    }
                ],
                ActiveImportedClassScheduleId = "spring-2026"
            });
        service.SavePluginSettings(
            "DesktopClassSchedule",
            "class-schedule-2x2",
            new SamplePluginSettings
            {
                SampleFlag = true,
                Title = "schedule-settings"
            });

        ComponentSettingsService.ResetCacheForTests();
        var reloadedService = sandbox.CreateService();

        var clockSnapshot = reloadedService.LoadForComponent("DesktopClock", "clock-2x2");
        var classScheduleSnapshot = reloadedService.LoadForComponent("DesktopClassSchedule", "class-schedule-2x2");
        var pluginSettings = reloadedService.LoadPluginSettings<SamplePluginSettings>(
            "DesktopClassSchedule",
            "class-schedule-2x2");

        Assert.Equal("Sweep", clockSnapshot.DesktopClockSecondHandMode);
        Assert.Single(classScheduleSnapshot.ImportedClassSchedules);
        Assert.Equal("spring-2026", classScheduleSnapshot.ActiveImportedClassScheduleId);
        Assert.True(pluginSettings.SampleFlag);
        Assert.Equal("schedule-settings", pluginSettings.Title);

        Assert.True(File.Exists(sandbox.DatabasePath));
    }

    private sealed class ComponentSettingsSandbox : IDisposable
    {
        private readonly string _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop.ComponentSettingsTests",
            Guid.NewGuid().ToString("N"));

        public ComponentSettingsSandbox()
        {
            Directory.CreateDirectory(_directoryPath);
            ComponentSettingsService.ResetCacheForTests();
        }

        public string SettingsPath => Path.Combine(_directoryPath, "component-settings.json");

        public string SettingsBackupPath => $"{SettingsPath}.migrated.bak";

        public string DatabasePath => Path.Combine(_directoryPath, "component-state.db");

        public ComponentSettingsService CreateService()
        {
            return new ComponentSettingsService(_directoryPath);
        }

        public void Dispose()
        {
            ComponentSettingsService.ResetCacheForTests();

            try
            {
                if (Directory.Exists(_directoryPath))
                {
                    Directory.Delete(_directoryPath, true);
                }
            }
            catch
            {
                // Temporary test directories are best-effort cleanup.
            }
        }
    }

    private sealed class SamplePluginSettings
    {
        public bool SampleFlag { get; set; }

        public string Title { get; set; } = string.Empty;
    }
}
