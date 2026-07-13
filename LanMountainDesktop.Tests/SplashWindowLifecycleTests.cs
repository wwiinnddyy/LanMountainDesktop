using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Shell;
using LanMountainDesktop.Launcher.Views;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SplashWindowLifecycleTests
{
    [AvaloniaFact]
    public async Task DismissAsync_WhenCalledConcurrentlyOffUiThread_ClosesExactlyOnce()
    {
        var window = new SplashWindow();
        var closedCount = 0;
        window.Closed += (_, _) => Interlocked.Increment(ref closedCount);
        window.Show();

        var dismissTasks = await Task.Run(() =>
            Enumerable.Range(0, 8)
                .Select(_ => window.DismissAsync())
                .ToArray());

        Assert.All(dismissTasks, task => Assert.Same(dismissTasks[0], task));
        await Task.WhenAll(dismissTasks);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.False(window.IsVisible);
            Assert.False(window.IsHitTestVisible);
            Assert.Equal(1, closedCount);
        });
    }

    [AvaloniaFact]
    public async Task CloseWindowsAsync_WaitsUntilSplashIsNoLongerVisible()
    {
        var window = new SplashWindow();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();

        await Task.Run(() => LaunchUiPresenter.CloseWindowsAsync(window, loadingDetailsWindow: null));

        Assert.True(closed.Task.IsCompleted);
        await Dispatcher.UIThread.InvokeAsync(() => Assert.False(window.IsVisible));
    }

    [AvaloniaFact]
    public async Task DismissAsync_WhenCloseIsCancelled_LeavesWindowHiddenAndNonHitTestable()
    {
        var window = new SplashWindow();
        var closeWasCancelled = false;

        void CancelClose(object? sender, WindowClosingEventArgs args)
        {
            closeWasCancelled = true;
            args.Cancel = true;
        }

        window.Closing += CancelClose;
        window.Show();

        await Task.Run(() => window.DismissAsync());

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.True(closeWasCancelled);
            Assert.False(window.IsVisible);
            Assert.False(window.IsHitTestVisible);

            window.Closing -= CancelClose;
            window.Close();
        });
    }
}
