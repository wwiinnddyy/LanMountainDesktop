using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class HostShutdownGateTests
{
    [Fact]
    public void Submit_WhenFirstExitRequest_AcceptsAndRecordsExit()
    {
        var gate = new HostShutdownGate();

        var submission = gate.Submit(HostShutdownMode.Exit);

        Assert.True(submission.Accepted);
        Assert.True(submission.IsFirstSubmission);
        Assert.Equal(HostShutdownMode.Exit, submission.EffectiveMode);
        Assert.True(gate.IsShutdownRequested);
        Assert.Equal(HostShutdownMode.Exit, gate.EffectiveMode);
    }

    [Fact]
    public void Submit_WhenDuplicateSameMode_AcceptsButDoesNotExecuteAgain()
    {
        var gate = new HostShutdownGate();
        gate.Submit(HostShutdownMode.Exit);

        var duplicate = gate.Submit(HostShutdownMode.Exit);

        Assert.True(duplicate.Accepted);
        Assert.False(duplicate.IsFirstSubmission);
        Assert.Equal(HostShutdownMode.Exit, duplicate.EffectiveMode);
    }

    [Fact]
    public void Submit_WhenExitArrivesAfterRestart_DoesNotOverwriteRestart()
    {
        var gate = new HostShutdownGate();
        gate.Submit(HostShutdownMode.Restart);

        var conflictingExit = gate.Submit(HostShutdownMode.Exit);

        Assert.False(conflictingExit.Accepted);
        Assert.False(conflictingExit.IsFirstSubmission);
        Assert.Equal(HostShutdownMode.Restart, conflictingExit.EffectiveMode);
        Assert.Equal(HostShutdownMode.Restart, gate.EffectiveMode);
    }
}
