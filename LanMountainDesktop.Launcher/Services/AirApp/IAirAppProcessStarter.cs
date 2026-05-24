using System.Diagnostics;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Services.AirApp;

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
    private readonly DotNetRuntimeProbeOptions? _runtimeProbeOptions;

    public AirAppProcessStarter(
        AirAppHostLocator locator,
        Func<string?> packageRootProvider,
        Func<string?> hostPathProvider,
        Func<string?> dataRootProvider,
        DotNetRuntimeProbeOptions? runtimeProbeOptions = null)
    {
        _locator = locator;
        _packageRootProvider = packageRootProvider;
        _hostPathProvider = hostPathProvider;
        _dataRootProvider = dataRootProvider;
        _runtimeProbeOptions = runtimeProbeOptions;
    }

    public Process? Start(
        string appId,
        string sessionId,
        string instanceKey,
        string? sourceComponentId,
        string? sourcePlacementId)
    {
        var hostPath = _locator.Resolve(_packageRootProvider(), _hostPathProvider());
        var startInfo = CreateStartInfo(hostPath, _runtimeProbeOptions);

        AddArgument(startInfo, "--app-id", appId);
        AddArgument(startInfo, "--session-id", sessionId);
        AddArgument(startInfo, "--instance-key", instanceKey);
        AddArgument(startInfo, "--launcher-pipe", LanMountainDesktop.Shared.IPC.IpcConstants.AirAppLifecyclePipeName);
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

        LanMountainDesktop.Launcher.Services.Logger.Info(
            $"Starting AirAppHost. AppId='{appId}'; InstanceKey='{instanceKey}'; HostPath='{hostPath}'; DataRoot='{dataRoot ?? string.Empty}'.");
        var process = Process.Start(startInfo);
        if (process is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                try
                {
                    LanMountainDesktop.Launcher.Services.Logger.Info(
                        $"AirAppHost exited. AppId='{appId}'; InstanceKey='{instanceKey}'; ProcessId={process.Id}; ExitCode={process.ExitCode}.");
                }
                catch (Exception ex)
                {
                    LanMountainDesktop.Launcher.Services.Logger.Warn($"Failed to log AirAppHost exit: {ex.Message}");
                }
            };
        }

        return process;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string hostPath,
        DotNetRuntimeProbeOptions? runtimeProbeOptions = null)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
        };

        if (OperatingSystem.IsWindows())
        {
            if (string.Equals(Path.GetExtension(hostPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (DotNetRuntimeProbe.IsFrameworkDependentWindowsApp(hostPath))
                {
                    var executableRuntime = DotNetRuntimeProbe.Probe(runtimeProbeOptions);
                    if (!executableRuntime.IsAvailable)
                    {
                        throw new InvalidOperationException(
                            "Unable to start AirAppHost because the architecture-matched .NET 10 runtime was not found. " +
                            executableRuntime.Message);
                    }
                }

                startInfo.FileName = hostPath;
                return startInfo;
            }

            var runtime = DotNetRuntimeProbe.Probe(runtimeProbeOptions);
            if (!runtime.IsAvailable || string.IsNullOrWhiteSpace(runtime.DotNetHostPath))
            {
                throw new InvalidOperationException(
                    "Unable to start AirAppHost because the architecture-matched .NET 10 runtime was not found. " +
                    runtime.Message);
            }

            startInfo.FileName = runtime.DotNetHostPath;
            startInfo.ArgumentList.Add(hostPath);
            return startInfo;
        }

        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(hostPath);
        return startInfo;
    }


    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
