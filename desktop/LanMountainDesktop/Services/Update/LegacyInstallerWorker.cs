using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanMountainDesktop.Services.Update;

internal static class LegacyInstallerWorker
{
    internal static ProcessStartInfo CreateStartInfo(string installer, string root, string version, string sha256, string receipt)
    {
        using var current = Process.GetCurrentProcess();
        var job = new { Installer = installer, Root = root, Version = version, Sha256 = sha256, Receipt = receipt,
            ParentId = Environment.ProcessId, ParentStartTicks = current.StartTime.ToUniversalTime().Ticks };
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(job)));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $job = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encoded}}')) | ConvertFrom-Json
            function Save-Receipt($status, $errorText) {
                $data = @{Status=$status; Version=$job.Version; Error=$errorText} | ConvertTo-Json -Compress
                [IO.File]::WriteAllText($job.Receipt, $data)
            }
            try {
                Save-Receipt 'waiting_for_exit' $null
                try {
                    $parent = Get-Process -Id $job.ParentId -ErrorAction Stop
                    if ($parent.StartTime.ToUniversalTime().Ticks -eq $job.ParentStartTicks) { $parent.WaitForExit() }
                } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
                if ((Get-FileHash -LiteralPath $job.Installer -Algorithm SHA256).Hash -ne $job.Sha256) {
                    throw 'Installer checksum mismatch.'
                }
                Save-Receipt 'installing' $null
                $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /DIR="' + $job.Root + '"'
                $installed = Start-Process -FilePath $job.Installer -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
                $installed.WaitForExit()
                if ($installed.ExitCode -ne 0) { throw ('Installer failed: ' + $installed.ExitCode) }
                $deployment = Get-ChildItem -LiteralPath $job.Root -Directory |
                    Where-Object { $_.Name.Split('-')[1] -eq $job.Version -and
                        (Test-Path -LiteralPath (Join-Path $_.FullName '{{DeploymentLayout.GetHostExecutableName()}}')) } |
                    Select-Object -First 1
                if (-not $deployment) { throw 'The requested deployment was not installed.' }
                Save-Receipt 'completed' $null
                Start-Process -FilePath (Join-Path $job.Root '{{DeploymentLayout.GetLauncherExecutableName()}}') -WorkingDirectory $job.Root -WindowStyle Hidden
            } catch { Save-Receipt 'failed' $_.Exception.Message; exit 1 }
            """;
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return info;
    }
}
