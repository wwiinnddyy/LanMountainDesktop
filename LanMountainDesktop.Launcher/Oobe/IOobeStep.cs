namespace LanMountainDesktop.Launcher.Oobe;

internal interface IOobeStep
{
    Task RunAsync(CancellationToken cancellationToken);
}
