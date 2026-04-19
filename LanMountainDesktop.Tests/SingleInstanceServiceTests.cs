using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task TryNotifyPrimaryInstance_ReturnsTrue_WhenPrimaryAcknowledges()
    {
        var mutexName = $"Local\\LanMountainDesktop.Tests.SingleInstance.{Guid.NewGuid():N}";
        var pipeName = $"LanMountainDesktop.Tests.Activate.{Guid.NewGuid():N}";

        using var primary = CreateService(mutexName, pipeName);
        using var secondary = CreateSecondaryService(mutexName, pipeName);
        Assert.True(primary.IsPrimaryInstance);
        MarkAsSecondaryForTest(secondary);

        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartActivationListener(() => activated.TrySetResult());

        var acknowledged = secondary.TryNotifyPrimaryInstance(TimeSpan.FromSeconds(2), out var failureReason);

        Assert.True(acknowledged);
        Assert.Null(failureReason);

        var completed = await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(activated.Task, completed);
    }

    [Fact]
    public void TryNotifyPrimaryInstance_ReturnsFalse_WhenListenerIsNotRunning()
    {
        var mutexName = $"Local\\LanMountainDesktop.Tests.SingleInstance.{Guid.NewGuid():N}";
        var pipeName = $"LanMountainDesktop.Tests.Activate.{Guid.NewGuid():N}";

        using var primary = CreateService(mutexName, pipeName);
        using var secondary = CreateSecondaryService(mutexName, pipeName);
        Assert.True(primary.IsPrimaryInstance);
        MarkAsSecondaryForTest(secondary);

        var acknowledged = secondary.TryNotifyPrimaryInstance(TimeSpan.FromMilliseconds(300), out var failureReason);

        Assert.False(acknowledged);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    private static SingleInstanceService CreateService(string mutexName, string pipeName)
    {
        var ctor = typeof(SingleInstanceService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(string)],
            modifiers: null);

        Assert.NotNull(ctor);
        return (SingleInstanceService)ctor!.Invoke([mutexName, pipeName]);
    }

    private static SingleInstanceService CreateSecondaryService(string mutexName, string pipeName)
    {
        SingleInstanceService? created = null;
        Exception? creationError = null;
        var thread = new Thread(() =>
        {
            try
            {
                created = CreateService(mutexName, pipeName);
            }
            catch (Exception ex)
            {
                creationError = ex;
            }
        });

        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (creationError is not null)
        {
            throw new InvalidOperationException("Failed to create secondary SingleInstanceService.", creationError);
        }

        Assert.NotNull(created);
        return created!;
    }

    private static void MarkAsSecondaryForTest(SingleInstanceService service)
    {
        var ownsMutexField = typeof(SingleInstanceService).GetField(
            "_ownsMutex",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ownsMutexField);
        ownsMutexField!.SetValue(service, false);
        Assert.False(service.IsPrimaryInstance);
    }
}
