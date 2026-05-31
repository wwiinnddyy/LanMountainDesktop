namespace LanMountainDesktop.AirAppRuntime;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = AirAppRuntimeOptions.Parse(args);
        AirAppRuntimeLogger.Info(
            $"Starting. AppRoot='{options.AppRoot ?? string.Empty}'; DataRoot='{options.DataRoot ?? string.Empty}'; " +
            $"LauncherPid={options.LauncherProcessId}; RequesterPid={options.RequesterProcessId}.");

        try
        {
            var lifecycleService = new AirAppLifecycleService(
                new AirAppProcessStarter(
                    new AirAppHostLocator(),
                    () => options.AppRoot,
                    () => null,
                    () => options.DataRoot));
            var lifetime = new AirAppRuntimeLifetime(options, lifecycleService);
            var controlService = new AirAppRuntimeControlService(lifetime);

            using var ipcHost = new AirAppRuntimeIpcHost(lifecycleService, controlService);
            ipcHost.Start();

            while (lifetime.ShouldKeepAlive())
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            AirAppRuntimeLogger.Info("Exiting because launcher, host, requester, and AirApp windows are gone.");
            return 0;
        }
        catch (Exception ex)
        {
            AirAppRuntimeLogger.Error("Unhandled runtime failure.", ex);
            return 1;
        }
    }
}
