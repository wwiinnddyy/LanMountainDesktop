using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Oobe;

internal sealed record OobeStepResult(bool ContinueLaunch, LauncherResult? Result = null)
{
    public static OobeStepResult Continue { get; } = new(true);

    public static OobeStepResult Complete(LauncherResult result) => new(false, result);
}

internal interface IOobeStep
{
    Task<OobeStepResult> RunAsync(CancellationToken cancellationToken);
}
