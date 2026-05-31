using LanMountainDesktop.Launcher;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class CommandContextTests
{
    public static TheoryData<string[], string> LaunchSourceCases => new()
    {
        { [], "normal" },
        { ["preview-oobe"], "debug-preview" },
        { ["--source", "plugin.lmdp", "--plugins-dir", "plugins", "--result", "result.json"], "plugin-install" },
        { ["launch", "--launch-source", "postinstall"], "postinstall" }
    };

    [Theory]
    [MemberData(nameof(LaunchSourceCases))]
    public void FromArgs_InfersExpectedLaunchSource(string[] args, string expectedLaunchSource)
    {
        var context = CommandContext.FromArgs(args);

        Assert.Equal(expectedLaunchSource, context.LaunchSource);
    }

    [Fact]
    public void FromArgs_DoesNotTreatAirAppBrokerAsLauncherGuiCommand()
    {
        var context = CommandContext.FromArgs(["air-app-broker", "--requester-pid", "42"]);

        Assert.False(context.IsGuiCommand);
    }
}
