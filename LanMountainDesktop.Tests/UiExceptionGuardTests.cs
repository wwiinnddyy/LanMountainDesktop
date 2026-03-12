using System;
using System.Threading.Tasks;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class UiExceptionGuardTests
{
    [Fact]
    public async Task RunGuardedUiActionAsync_SwallowsNonFatalException_AndInvokesHandler()
    {
        var handlerCalled = false;

        await UiExceptionGuard.RunGuardedUiActionAsync(
            () => throw new InvalidOperationException("boom"),
            "UnitTest.NonFatal",
            onHandledException: ex =>
            {
                handlerCalled = ex is InvalidOperationException;
                return Task.CompletedTask;
            });

        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task RunGuardedUiActionAsync_RethrowsFatalException()
    {
        await Assert.ThrowsAsync<OutOfMemoryException>(() =>
            UiExceptionGuard.RunGuardedUiActionAsync(
                () => throw new OutOfMemoryException("fatal"),
                "UnitTest.Fatal"));
    }

    [Fact]
    public void IsFatalException_ReturnsExpectedClassification()
    {
        Assert.True(UiExceptionGuard.IsFatalException(new OutOfMemoryException()));
        Assert.True(UiExceptionGuard.IsFatalException(new AccessViolationException()));
        Assert.False(UiExceptionGuard.IsFatalException(new InvalidOperationException()));
    }
}
