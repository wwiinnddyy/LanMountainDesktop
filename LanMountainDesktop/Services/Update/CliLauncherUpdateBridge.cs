using System.Diagnostics;
using System.IO;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class CliLauncherUpdateBridge : ILauncherUpdateBridge
{
    public Task<LaunchResult> LaunchInstallerAsync(InstallRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                return Task.FromResult(new LaunchResult(false, "Launcher executable not found.", null));
            }

            var resolvedLauncherRoot = Path.GetDirectoryName(launcherPath)!;

            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                Arguments = $"apply-update --app-root \"{resolvedLauncherRoot}\" --launch-source {request.LaunchSource ?? "apply-update"}",
                UseShellExecute = false,
                WorkingDirectory = resolvedLauncherRoot
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(new LaunchResult(false, "Failed to start Launcher process.", null));
            }

            return Task.FromResult(new LaunchResult(true, null, process.Id));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new LaunchResult(false, ex.Message, null));
        }
    }

    public IObservable<InstallProgressReport> ProgressStream => ObservableHelper<InstallProgressReport>.Empty;

    public Task<bool> SupportsIpcAsync() => Task.FromResult(false);
}
