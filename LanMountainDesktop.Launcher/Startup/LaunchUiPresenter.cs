using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal static class LaunchUiPresenter
{
    public static async Task CloseWindowsAsync(SplashWindow splashWindow, LoadingDetailsWindow? loadingDetailsWindow)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => splashWindow.DismissAsync());
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to dismiss splash window.", ex);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (loadingDetailsWindow is not null && loadingDetailsWindow.IsVisible)
                {
                    loadingDetailsWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close loading details window.", ex);
            }
        });
    }

    public static async Task<(ErrorWindowResult Result, string? CustomPath)> ShowHostNotFoundErrorAsync()
    {
        ErrorWindow? errorWindow = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                errorWindow = new ErrorWindow();
                errorWindow.ConfigureForHostNotFound();
                errorWindow.SetErrorMessage("LanMountainDesktop host executable was not found.");
                errorWindow.Show();
                Logger.Warn("Host not found. Showing error window.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show host-not-found error window.", ex);
            }
        });

        if (errorWindow is null)
        {
            return (ErrorWindowResult.Exit, null);
        }

        ErrorWindowResult result;
        string? customPath;
        try
        {
            result = await errorWindow.WaitForChoiceAsync().ConfigureAwait(false);
            customPath = errorWindow.GetCustomHostPath();
            Logger.Info($"Host-not-found window result='{result}'; HasCustomPath={!string.IsNullOrWhiteSpace(customPath)}.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed while waiting for host-not-found window result.", ex);
            result = ErrorWindowResult.Exit;
            customPath = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (errorWindow.IsVisible && errorWindow.IsLoaded)
                {
                    errorWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close host-not-found error window.", ex);
            }
        });

        return (result, customPath);
    }

    public static async Task<MigrationResult> ShowMigrationPromptAsync(LegacyVersionInfo legacyInfo)
    {
        MigrationPromptWindow? migrationWindow = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                migrationWindow = new MigrationPromptWindow();
                migrationWindow.SetLegacyInfo(legacyInfo);
                migrationWindow.Show();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to show migration prompt window.", ex);
            }
        });

        if (migrationWindow is null)
        {
            return MigrationResult.Skipped;
        }

        MigrationResult result;
        try
        {
            result = await migrationWindow.WaitForChoiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed while waiting for migration prompt result.", ex);
            result = MigrationResult.Skipped;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                if (migrationWindow.IsVisible && migrationWindow.IsLoaded)
                {
                    migrationWindow.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to close migration prompt window.", ex);
            }
        });

        return result;
    }

    public static string MapStartupStageToSplashStage(StartupStage stage) => stage switch
    {
        StartupStage.Initializing => "initializing",
        StartupStage.LoadingSettings => "settings",
        StartupStage.LoadingPlugins => "plugins",
        StartupStage.TrayReady => "shell",
        StartupStage.InitializingUI => "ui",
        StartupStage.ShellInitialized => "shell",
        StartupStage.BackgroundReady => "ready",
        StartupStage.DesktopVisible => "ready",
        StartupStage.ActivationRedirected => "activation",
        StartupStage.ActivationFailed => "error",
        StartupStage.Ready => "ready",
        _ => "launch"
    };

    public static async Task<MultiInstancePromptResult> ShowMultiInstancePromptAsync(PublicShellStatus status)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var prompt = new MultiInstancePromptWindow();
            prompt.SetDetails(status.ProcessId, status.ShellState);
            prompt.Show();
            return await prompt.WaitForChoiceAsync().ConfigureAwait(true);
        });
    }
}
