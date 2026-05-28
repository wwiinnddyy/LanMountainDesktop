namespace LanMountainDesktop.Launcher.Update;

internal static class UpdateEngineFactory
{
    public static IUpdateEngine Create(DeploymentLocator deploymentLocator, IUpdateProgressReporter? progressReporter = null) =>
        new UpdateEngineFacade(deploymentLocator, progressReporter);
}
