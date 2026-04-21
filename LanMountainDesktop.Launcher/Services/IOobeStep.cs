namespace LanMountainDesktop.Launcher.Services;

internal interface IOobeStep
{
    Task RunAsync(CancellationToken cancellationToken);
}
