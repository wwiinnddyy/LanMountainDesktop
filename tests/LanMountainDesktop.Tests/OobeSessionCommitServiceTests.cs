using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class OobeSessionCommitServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(OobeSessionCommitServiceTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveDataRoot_ForChoice_DoesNotWriteConfigOrState()
    {
        var resolver = new DataLocationResolver(_tempRoot);

        var dataRoot = resolver.ResolveDataRoot(DataLocationMode.Portable);

        Assert.Equal(Path.Combine(_tempRoot, "Desktop"), dataRoot);
        Assert.False(File.Exists(resolver.ResolveConfigPath()));
        Assert.False(File.Exists(GetCompletedStatePath(dataRoot)));
    }

    [Fact]
    public void Commit_WritesSettingsAndCompletedState_OnlyAfterFinalDraft()
    {
        var resolver = new DataLocationResolver(_tempRoot);
        var oobeState = new OobeStateService(
            _tempRoot,
            executionSnapshot: new LauncherExecutionSnapshot(false, "tester", "S-1-5-test"));
        var context = CommandContext.FromArgs(["launch"]);
        var service = new OobeSessionCommitService(
            resolver,
            oobeState,
            context,
            setWindowsStartup: _ => true);
        var draft = CreateDraft();

        var result = service.Commit(draft);
        var dataRoot = resolver.ResolveDataRoot();

        Assert.True(result.Success);
        Assert.True(File.Exists(resolver.ResolveConfigPath()));
        var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(dataRoot);
        Assert.True(File.Exists(settingsPath));

        // 向导第五步的两个开关必须落在宿主读的那份文件上（此前它们写进 privacy-config.json，全仓没有读者）。
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.False(doc.RootElement.GetProperty(HostAppSettingsOobeMerger.UploadAnonymousCrashDataKey).GetBoolean());
        Assert.False(doc.RootElement.GetProperty(HostAppSettingsOobeMerger.UploadAnonymousUsageDataKey).GetBoolean());

        Assert.True(File.Exists(Path.Combine(resolver.ResolveLauncherDataPath(), "privacy-agreement.state.json")));
        Assert.True(File.Exists(GetCompletedStatePath(dataRoot)));
    }

    [Fact]
    public void Commit_DoesNotWriteCompletedState_WhenFinalSaveFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var resolver = new DataLocationResolver(_tempRoot);
        var oobeState = new OobeStateService(
            _tempRoot,
            executionSnapshot: new LauncherExecutionSnapshot(false, "tester", "S-1-5-test"));
        var context = CommandContext.FromArgs(["launch"]);
        var service = new OobeSessionCommitService(
            resolver,
            oobeState,
            context,
            setWindowsStartup: _ => false);

        var result = service.Commit(CreateDraft());
        var dataRoot = resolver.ResolveDataRoot();

        Assert.False(result.Success);
        Assert.Equal("windows_startup_save_failed", result.ResultCode);
        Assert.False(File.Exists(GetCompletedStatePath(dataRoot)));
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

    private static OobeSessionDraft CreateDraft() =>
        new()
        {
            DataLocationMode = DataLocationMode.Portable,
            MigrateExistingData = false,
            StartupChoices = new HostAppSettingsStartupChoices(
                ShowInTaskbar: true,
                EnableFadeTransition: true,
                EnableSlideTransition: false,
                FusedPopupExperience: false,
                AutoStartWithWindows: false),
            PrivacyConfig = new PrivacyConfig
            {
                CrashTelemetryEnabled = false,
                UsageTelemetryEnabled = false,
                TelemetryId = "test-telemetry"
            },
            PrivacyAgreementAccepted = true,
            PrivacyUserId = "test-telemetry",
            PrivacyDeviceId = "test-device"
        };

    private static string GetCompletedStatePath(string dataRoot) =>
        Path.Combine(dataRoot, "Launcher", "state", "oobe-state.json");
}
