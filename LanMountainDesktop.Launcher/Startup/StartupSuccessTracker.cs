using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal enum LaunchSuccessPolicy
{
    Foreground,
    RestartBackground,
    RestartTray
}

internal sealed record StartupSuccessState(
    StartupStage Stage,
    string Code,
    string Message);

internal sealed class StartupSuccessTracker
{
    private readonly LaunchSuccessPolicy _policy;
    private bool _trayReady;
    private bool _backgroundReady;

    public string PolicyKey => _policy.ToString();

    public StartupSuccessTracker(CommandContext context)
    {
        var restartPresentation = LauncherRuntimeMetadata.GetRestartPresentationMode(context.RawArgs);
        var isRestartLaunch = string.Equals(context.LaunchSource, "restart", StringComparison.OrdinalIgnoreCase);

        _policy = !isRestartLaunch
            ? LaunchSuccessPolicy.Foreground
            : restartPresentation switch
            {
                RestartPresentationMode.Tray => LaunchSuccessPolicy.RestartTray,
                RestartPresentationMode.Minimized => LaunchSuccessPolicy.RestartBackground,
                _ => LaunchSuccessPolicy.Foreground
            };
    }

    public bool TryResolve(StartupStage stage, out StartupSuccessState successState)
    {
        switch (stage)
        {
            case StartupStage.ActivationRedirected:
                successState = new StartupSuccessState(
                    stage,
                    "activation_redirected",
                    "Launcher activation was redirected to the existing desktop instance.");
                return true;

            case StartupStage.DesktopVisible:
                successState = new StartupSuccessState(
                    stage,
                    _policy == LaunchSuccessPolicy.Foreground ? "ok" : "desktop_visible_fallback",
                    _policy == LaunchSuccessPolicy.Foreground
                        ? "Desktop is visible and ready."
                        : "Desktop recovered in a visible state.");
                return true;

            case StartupStage.Ready:
                successState = new StartupSuccessState(
                    stage,
                    _policy == LaunchSuccessPolicy.Foreground ? "ready" : "background_ready",
                    "Desktop reported that startup is ready.");
                return true;

            case StartupStage.TrayReady:
                _trayReady = true;
                break;

            case StartupStage.BackgroundReady:
                _backgroundReady = true;
                break;
        }

        if (_policy == LaunchSuccessPolicy.RestartBackground && _backgroundReady)
        {
            successState = new StartupSuccessState(
                StartupStage.BackgroundReady,
                "background_ready",
                "Desktop restart completed in the background.");
            return true;
        }

        if (_policy == LaunchSuccessPolicy.RestartTray && _trayReady && _backgroundReady)
        {
            successState = new StartupSuccessState(
                StartupStage.BackgroundReady,
                "background_ready",
                "Desktop restart completed with tray recovery ready.");
            return true;
        }

        successState = default!;
        return false;
    }

    public bool TryResolve(PublicShellStatus? status, out StartupSuccessState successState)
    {
        if (status is not null &&
            (status.DesktopVisible || status.MainWindowVisible || status.MainWindowOpened))
        {
            successState = new StartupSuccessState(
                status.DesktopVisible || status.MainWindowVisible
                    ? StartupStage.DesktopVisible
                    : StartupStage.Ready,
                _policy == LaunchSuccessPolicy.Foreground ? "ok" : "background_ready",
                status.DesktopVisible || status.MainWindowVisible
                    ? "Desktop shell is visible and ready."
                    : "Desktop shell window has opened.");
            return true;
        }

        successState = default!;
        return false;
    }

    public StartupSuccessState BuildRecoverySuccessState()
    {
        return _policy switch
        {
            LaunchSuccessPolicy.RestartTray => new StartupSuccessState(
                StartupStage.DesktopVisible,
                "recovery_activation_requested",
                "Launcher requested a visible recovery because the background restart never confirmed tray readiness."),
            LaunchSuccessPolicy.RestartBackground => new StartupSuccessState(
                StartupStage.DesktopVisible,
                "recovery_activation_requested",
                "Launcher requested a visible recovery because the background restart never confirmed readiness."),
            _ => new StartupSuccessState(
                StartupStage.DesktopVisible,
                "recovery_activation_requested",
                "Launcher requested a visible recovery from the running desktop instance.")
        };
    }
}
