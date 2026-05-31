using System.Diagnostics;
using LanMountainDesktop.Shared.IPC;

namespace LanMountainDesktop.AirAppRuntime;

internal interface IAirAppProcessStarter
{
    Process? Start(string appId, string sessionId, string instanceKey, string? sourceComponentId, string? sourcePlacementId);
}

internal sealed class AirAppProcessStarter : IAirAppProcessStarter
{
    private readonly AirAppHostLocator _locator;
    private readonly Func<string?> _packageRootProvider;
    private readonly Func<string?> _hostPathProvider;
    private readonly Func<string?> _dataRootProvider;

    public AirAppProcessStarter(
        AirAppHostLocator locator,
        Func<string?> packageRootProvider,
        Func<string?> hostPathProvider,
        Func<string?> dataRootProvider)
    {
        _locator = locator;
        _packageRootProvider = packageRootProvider;
        _hostPathProvider = hostPathProvider;
        _dataRootProvider = dataRootProvider;
    }

    public Process? Start(
        string appId,
        string sessionId,
        string instanceKey,
        string? sourceComponentId,
        string? sourcePlacementId)
    {
        var hostPath = _locator.Resolve(_packageRootProvider(), _hostPathProvider());
        var startInfo = CreateStartInfo(hostPath);

        AddArgument(startInfo, "--app-id", appId);
        AddArgument(startInfo, "--session-id", sessionId);
        AddArgument(startInfo, "--instance-key", instanceKey);
        AddArgument(startInfo, "--launcher-pipe", IpcConstants.AirAppRuntimePipeName);
        var dataRoot = _dataRootProvider();
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            AddArgument(startInfo, "--data-root", Path.GetFullPath(dataRoot));
        }

        if (!string.IsNullOrWhiteSpace(sourceComponentId))
        {
            AddArgument(startInfo, "--source-component-id", sourceComponentId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sourcePlacementId))
        {
            AddArgument(startInfo, "--source-placement-id", sourcePlacementId.Trim());
        }

        AirAppRuntimeLogger.Info(
            $"Starting AirAppHost. AppId='{appId}'; InstanceKey='{instanceKey}'; HostPath='{hostPath}'; DataRoot='{dataRoot ?? string.Empty}'.");
        var process = Process.Start(startInfo);
        if (process is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                try
                {
                    AirAppRuntimeLogger.Info(
                        $"AirAppHost exited. AppId='{appId}'; InstanceKey='{instanceKey}'; ProcessId={process.Id}; ExitCode={process.ExitCode}.");
                }
                catch (Exception ex)
                {
                    AirAppRuntimeLogger.Warn($"Failed to log AirAppHost exit: {ex.Message}");
                }
            };
        }

        return process;
    }

    internal static ProcessStartInfo CreateStartInfo(string hostPath)
    {
        return AirAppRuntimeProcessStarter.CreateStartInfo(hostPath);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
