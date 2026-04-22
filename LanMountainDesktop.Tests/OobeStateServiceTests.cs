using System.Text.Json;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class OobeStateServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Tests", nameof(OobeStateServiceTests), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Evaluate_ReturnsFirstRun_ForNormalLaunch_WhenStateIsMissing()
    {
        var service = CreateService();
        var context = CommandContext.FromArgs(["launch"]);

        var decision = service.Evaluate(context);

        Assert.Equal(OobeStateStatus.FirstRun, decision.Status);
        Assert.True(decision.ShouldShowOobe);
        Assert.Equal("normal", decision.LaunchSource);
    }

    [Fact]
    public void Evaluate_ReturnsCompleted_WhenStateFileExists()
    {
        var statePath = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var state = new OobeStateFile
        {
            SchemaVersion = 1,
            CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            UserName = "tester",
            UserSid = "S-1-5-test",
            LaunchSource = "normal"
        };
        File.WriteAllText(statePath, JsonSerializer.Serialize(state));

        var service = CreateService();
        var context = CommandContext.FromArgs(["launch"]);

        var decision = service.Evaluate(context);

        Assert.Equal(OobeStateStatus.Completed, decision.Status);
        Assert.False(decision.ShouldShowOobe);
    }

    [Fact]
    public void Evaluate_MigratesLegacyMarker_AndTreatsItAsCompleted()
    {
        var legacyMarkerPath = GetLegacyMarkerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(legacyMarkerPath)!);
        File.WriteAllText(legacyMarkerPath, DateTimeOffset.UtcNow.ToString("O"));

        var service = CreateService();
        var context = CommandContext.FromArgs(["launch"]);

        var decision = service.Evaluate(context);

        Assert.Equal(OobeStateStatus.Completed, decision.Status);
        Assert.True(decision.UsedLegacyMarker);
        Assert.True(decision.MigratedLegacyMarker);
        Assert.True(File.Exists(GetStatePath()));
        Assert.False(File.Exists(legacyMarkerPath));
    }

    [Fact]
    public void Evaluate_SuppressesOobe_ForElevatedFirstRun()
    {
        var service = CreateService(new LauncherExecutionSnapshot(true, "tester", "S-1-5-test"));
        var context = CommandContext.FromArgs(["launch"]);

        var decision = service.Evaluate(context);

        Assert.Equal(OobeStateStatus.Suppressed, decision.Status);
        Assert.False(decision.ShouldShowOobe);
        Assert.Equal("oobe_suppressed_elevated", decision.ResultCode);
    }

    [Fact]
    public void Evaluate_ReturnsUnavailable_ForInvalidStateFile()
    {
        var statePath = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, "{ this is not valid json }");

        var service = CreateService();
        var context = CommandContext.FromArgs(["launch"]);

        var decision = service.Evaluate(context);

        Assert.Equal(OobeStateStatus.Unavailable, decision.Status);
        Assert.False(decision.ShouldShowOobe);
        Assert.Equal("oobe_state_unavailable", decision.ResultCode);
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

    private OobeStateService CreateService(LauncherExecutionSnapshot? executionSnapshot = null)
    {
        return new OobeStateService(
            appRoot: _tempRoot,
            stateRootOverride: _tempRoot,
            executionSnapshot: executionSnapshot ?? new LauncherExecutionSnapshot(false, "tester", "S-1-5-test"));
    }

    private string GetStatePath() => Path.Combine(_tempRoot, ".launcher", "state", "oobe-state.json");

    private string GetLegacyMarkerPath() => Path.Combine(_tempRoot, ".launcher", "state", "first_run_completed");
}
