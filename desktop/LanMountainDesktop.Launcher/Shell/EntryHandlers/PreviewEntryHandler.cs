using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Shell.EntryHandlers;

internal static class PreviewEntryHandler
{
    public static bool TryHandle(CommandContext context, IClassicDesktopStyleApplicationLifetime desktop)
    {
        switch (context.Command.ToLowerInvariant())
        {
            case "preview-splash":
                RunSplashPreview(desktop);
                return true;
            case "preview-error":
                RunErrorPreview(desktop);
                return true;
            case "preview-multi-instance":
                RunMultiInstancePreview(desktop);
                return true;
            case "preview-oobe":
                RunOobePreview(desktop);
                return true;
            case "preview-debug":
                new DevDebugWindow().Show();
                return true;
            default:
                return false;
        }
    }

    private static void RunSplashPreview(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splashWindow = LaunchEntryHandler.CreateSplashWindow();
        splashWindow.SetDebugMode(true);
        splashWindow.Show();
        _ = SimulateSplashPreviewAsync(desktop, splashWindow);
    }

    private static void RunErrorPreview(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errorWindow = new ErrorWindow();
        errorWindow.SetErrorMessage(Strings.Preview_ErrorMessage);
        errorWindow.Show();
        _ = WaitForWindowCloseAsync(desktop, errorWindow);
    }

    private static void RunMultiInstancePreview(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var promptWindow = new MultiInstancePromptWindow();
        promptWindow.SetDetails(Environment.ProcessId, "ForegroundDesktop");
        promptWindow.Show();
        _ = WaitForWindowCloseAsync(desktop, promptWindow);
    }

    private static void RunOobePreview(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var oobeWindow = new OobeWindow();
        oobeWindow.Show();
        _ = SimulateOobePreviewAsync(desktop, oobeWindow);
    }

    private static async Task SimulateSplashPreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow window)
    {
        var stages = new[] { "initializing", "plugins", "launch", "ready" };
        var messages = new[]
        {
            Strings.Preview_SplashInitializing,
            Strings.Preview_SplashCheckingPlugins,
            Strings.Preview_SplashLaunchingHost,
            Strings.Preview_SplashReady
        };
        var reporter = (ISplashStageReporter)window;

        for (var i = 0; i < stages.Length; i++)
        {
            reporter.Report(stages[i], messages[i]);
            await Task.Delay(800).ConfigureAwait(false);
        }

        await Task.Delay(5000).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private static async Task SimulateOobePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, OobeWindow window)
    {
        try
        {
            await window.WaitForCompletionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("OOBE preview failed.", ex);
        }

        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }

    private static async Task WaitForWindowCloseAsync(IClassicDesktopStyleApplicationLifetime desktop, Window window)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => tcs.TrySetResult();
        await tcs.Task.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0));
    }
}
