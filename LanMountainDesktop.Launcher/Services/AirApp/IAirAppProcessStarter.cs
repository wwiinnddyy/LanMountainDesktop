using System.Diagnostics;

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

    public AirAppProcessStarter(
        AirAppHostLocator locator,
        Func<string?> packageRootProvider,
        Func<string?> hostPathProvider)
    {
        _locator = locator;
        _packageRootProvider = packageRootProvider;
        _hostPathProvider = hostPathProvider;
    }

    public Process? Start(
        string appId,
        string sessionId,
        string instanceKey,
        string? sourceComponentId,
        string? sourcePlacementId)
    {
        var hostPath = _locator.Resolve(_packageRootProvider(), _hostPathProvider());
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
        };

        if (OperatingSystem.IsWindows() &&
            string.Equals(Path.GetExtension(hostPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = hostPath;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(hostPath);
        }

        AddArgument(startInfo, "--app-id", appId);
        AddArgument(startInfo, "--session-id", sessionId);
        AddArgument(startInfo, "--instance-key", instanceKey);
        AddArgument(startInfo, "--launcher-pipe", LanMountainDesktop.Shared.IPC.IpcConstants.AirAppLifecyclePipeName);

        if (!string.IsNullOrWhiteSpace(sourceComponentId))
        {
            AddArgument(startInfo, "--source-component-id", sourceComponentId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sourcePlacementId))
        {
            AddArgument(startInfo, "--source-placement-id", sourcePlacementId.Trim());
        }

        return Process.Start(startInfo);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
