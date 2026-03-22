using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopEditCommitMathTests
{
    [Fact]
    public void IsPendingCommitValid_ReturnsTrueOnlyForMatchingActiveVersion()
    {
        Assert.True(DesktopEditCommitMath.IsPendingCommitValid(isPending: true, scheduledVersion: 4, currentVersion: 4));
        Assert.False(DesktopEditCommitMath.IsPendingCommitValid(isPending: false, scheduledVersion: 4, currentVersion: 4));
        Assert.False(DesktopEditCommitMath.IsPendingCommitValid(isPending: true, scheduledVersion: 4, currentVersion: 5));
    }
}
