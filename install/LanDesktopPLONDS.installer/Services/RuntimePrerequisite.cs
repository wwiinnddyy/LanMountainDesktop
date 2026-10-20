using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

internal static class RuntimePrerequisite
{
    internal static async Task EnsureAsync(PreparedFilesPackage package, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        var app = FilesPackageInstaller.ResolveFullPackageAppDirectory(package.ExtractDirectory, package.Version);
        var config = Path.Combine(app, DeploymentLayout.HostExecutableBaseName + ".runtimeconfig.json");
        if (!File.Exists(config)) return;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false));
        var options = document.RootElement.GetProperty("runtimeOptions");
        if (!options.TryGetProperty("framework", out var framework)) return;
        var name = framework.GetProperty("name").GetString()!;
        var version = Version.Parse(framework.GetProperty("version").GetString()!.Split('-')[0]);
        var architecture = package.Manifest.Platform.EndsWith("-x86", StringComparison.Ordinal) ? "x86" : "x64";
        if (IsInstalled(name, version, architecture)) return;

        var directory = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var installer = Path.Combine(directory, "runtime.exe");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var url = $"https://aka.ms/dotnet/{version.Major}.0/windowsdesktop-runtime-win-{architecture}.exe";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(installer);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
        var signature = AuthenticodeVerifier.VerifyFile(installer);
        if (signature.Status != AuthenticodeStatus.Signed || signature.SignerSubject?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidDataException("The .NET runtime installer is not signed by Microsoft.");
        using var process = Process.Start(new ProcessStartInfo(installer)
        {
            Arguments = "/install /quiet /norestart", UseShellExecute = true,
            Verb = InstallerElevation.IsRunningElevated() ? string.Empty : "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("The .NET runtime installer could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode is not (0 or 3010) || !IsInstalled(name, version, architecture))
            throw new InvalidOperationException($"The required .NET runtime could not be installed (exit {process.ExitCode}).");
    }

    private static bool IsInstalled(string framework, Version required, string architecture)
    {
        var programFiles = architecture == "x86"
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            : Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var roots = new[] { Path.Combine(programFiles, "dotnet"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dotnet") };
        return roots.Select(root => Path.Combine(root, "shared", framework))
            .Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories)
            .Any(path => Version.TryParse(Path.GetFileName(path), out var version) &&
                version.Major == required.Major && version >= required);
    }
}
