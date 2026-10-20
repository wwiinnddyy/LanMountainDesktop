using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.Data;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsMaintenanceGateway
{
    public InstallResult Schedule(PlondsPreparedPackage? package, string launcherRoot, string? rollbackSnapshot = null)
    {
        try
        {
            var executable = package?.FilesDirectory is { } files
                ? Path.Combine(files, DeploymentMaintenance.ExecutableName)
                : Path.Combine(launcherRoot, DeploymentMaintenance.ExecutableName);
            if (!File.Exists(executable))
                return new InstallResult(false, "This package does not include its maintenance installer.", false, "maintenance_missing");
            var directory = package is not null
                ? Path.GetDirectoryName(Path.GetFullPath(package.ManifestPath))!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UserDataRoot.FolderName,
                    "Updates", "maintenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var helper = Path.Combine(directory, DeploymentMaintenance.ExecutableName);
            if (!string.Equals(Path.GetFullPath(executable), Path.GetFullPath(helper), StringComparison.OrdinalIgnoreCase))
                File.Copy(executable, helper, overwrite: true);
            using var process = Process.GetCurrentProcess();
            var request = new DeploymentMaintenanceRequest(launcherRoot, package?.ManifestPath, package?.FilesZipPath,
                package?.FilesZipPath is { } archive ? HashArchive(archive) : null, package?.Version.ToString(),
                Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks,
                Path.Combine(directory, DeploymentMaintenance.ReceiptFileName), rollbackSnapshot);
            var path = Path.Combine(directory, DeploymentMaintenance.RequestFileName);
            if (File.Exists(request.ReceiptPath)) File.Delete(request.ReceiptPath);
            File.WriteAllText(path, JsonSerializer.Serialize(request, DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceRequest));
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = true, WorkingDirectory = directory,
                Verb = OperatingSystem.IsWindows() && !CanWriteRoot(launcherRoot) ? "runas" : string.Empty,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(path);
            using var helperProcess = Process.Start(start);
            return helperProcess is null
                ? new InstallResult(false, "The maintenance installer could not be started.", false)
                : new InstallResult(true, null, false, RequiresExit: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new InstallResult(false, ex.Message, true);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, ex.Message, false);
        }
    }

    private static string HashArchive(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool CanWriteRoot(string root)
    {
        var test = Path.Combine(root, ".write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(test)) { }
            File.Delete(test);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
