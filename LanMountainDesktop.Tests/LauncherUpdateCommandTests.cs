using System.Text.Json;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Infrastructure;
using LanMountainDesktop.Launcher.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LauncherUpdateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.LauncherUpdateCommandTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyUpdateCommand_IsNotHandledByLauncherCli()
    {
        Directory.CreateDirectory(_root);
        var resultPath = Path.Combine(_root, "result.json");
        var context = CommandContext.FromArgs(["apply-update", "--app-root", _root, "--result", resultPath]);

        var exitCode = await Commands.RunCliCommandAsync(context);
        var result = ReadResult(resultPath);

        Assert.Equal(1, exitCode);
        Assert.Equal("command", result.Stage);
        Assert.Equal("unsupported_command", result.Code);
    }

    [Fact]
    public async Task RollbackCommand_IsNotHandledByLauncherCli()
    {
        Directory.CreateDirectory(_root);
        var resultPath = Path.Combine(_root, "result.json");
        var context = CommandContext.FromArgs(["rollback", "--app-root", _root, "--result", resultPath]);

        var exitCode = await Commands.RunCliCommandAsync(context);
        var result = ReadResult(resultPath);

        Assert.Equal(1, exitCode);
        Assert.Equal("command", result.Stage);
        Assert.Equal("unsupported_command", result.Code);
    }

    private static LauncherResult ReadResult(string path)
    {
        var result = JsonSerializer.Deserialize<LauncherResult>(File.ReadAllText(path));
        return result ?? throw new InvalidOperationException("Launcher result was not written.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
