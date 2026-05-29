using System.Globalization;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class AppDeploymentLocator(string launcherRoot)
{
    public string LauncherRoot { get; } = launcherRoot;

    public string? FindCurrentDeploymentDirectory()
    {
        if (!Directory.Exists(LauncherRoot))
        {
            return null;
        }

        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var candidates = Directory.GetDirectories(LauncherRoot, "app-*", SearchOption.TopDirectoryOnly);

        return candidates
            .Where(path => !File.Exists(Path.Combine(path, ".destroy")))
            .Where(path => !File.Exists(Path.Combine(path, ".partial")))
            .Where(path => File.Exists(Path.Combine(path, executable)))
            .Select(path => new
            {
                Path = path,
                Version = ParseVersionFromDirectory(path),
                HasCurrent = File.Exists(Path.Combine(path, ".current"))
            })
            .OrderBy(x => x.HasCurrent ? 0 : 1)
            .ThenByDescending(x => x.Version)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    public string GetCurrentVersion()
    {
        var deployment = FindCurrentDeploymentDirectory();
        return string.IsNullOrWhiteSpace(deployment) ? "0.0.0" : ParseVersionTextFromDirectory(deployment) ?? "0.0.0";
    }

    public string BuildNextDeploymentDirectory(string targetVersion)
    {
        var sanitized = string.IsNullOrWhiteSpace(targetVersion) ? "0.0.0" : targetVersion.Trim();
        var index = 0;
        while (true)
        {
            var candidate = Path.Combine(LauncherRoot, $"app-{sanitized}-{index.ToString(CultureInfo.InvariantCulture)}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    public void CleanupOldDeployments(int minVersionsToKeep = 3)
    {
        try
        {
            if (!Directory.Exists(LauncherRoot))
            {
                return;
            }

            var candidates = Directory.GetDirectories(LauncherRoot, "app-*", SearchOption.TopDirectoryOnly);
            var validDeployments = candidates
                .Where(path => !File.Exists(Path.Combine(path, ".partial")))
                .Select(path => new
                {
                    Path = path,
                    Version = ParseVersionFromDirectory(path),
                    IsDestroyed = File.Exists(Path.Combine(path, ".destroy")),
                    IsCurrent = File.Exists(Path.Combine(path, ".current"))
                })
                .OrderByDescending(item => item.Version)
                .ToList();

            var versionsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentVersion = validDeployments.FirstOrDefault(d => d.IsCurrent);
            if (currentVersion is not null)
            {
                versionsToKeep.Add(currentVersion.Path);
            }

            foreach (var ver in validDeployments.Where(d => !d.IsDestroyed).Take(minVersionsToKeep))
            {
                versionsToKeep.Add(ver.Path);
            }

            var snapshotsDir = UpdatePaths.GetSnapshotsDirectory(LauncherRoot);
            if (Directory.Exists(snapshotsDir))
            {
                var snapshotFiles = Directory
                    .GetFiles(snapshotsDir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetCreationTimeUtc)
                    .Take(Math.Max(1, minVersionsToKeep));

                foreach (var snapshotFile in snapshotFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(snapshotFile);
                        var snapshot = JsonSerializer.Deserialize(json, UpdateApplyJsonContext.Default.ApplySnapshotMetadata);
                        if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.SourceDirectory) && Directory.Exists(snapshot.SourceDirectory))
                        {
                            versionsToKeep.Add(snapshot.SourceDirectory);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            foreach (var deployment in validDeployments)
            {
                if (versionsToKeep.Contains(deployment.Path))
                {
                    if (deployment.IsDestroyed)
                    {
                        try { File.Delete(Path.Combine(deployment.Path, ".destroy")); } catch { }
                    }

                    continue;
                }

                if (!deployment.IsDestroyed)
                {
                    try { File.WriteAllText(Path.Combine(deployment.Path, ".destroy"), string.Empty); } catch { }
                }

                try { Directory.Delete(deployment.Path, true); } catch { }
            }
        }
        catch
        {
        }
    }

    public static Version ParseVersionFromDirectory(string path)
    {
        var text = ParseVersionTextFromDirectory(path);
        return Version.TryParse(text, out var version) ? version : new Version(0, 0, 0);
    }

    private static string? ParseVersionTextFromDirectory(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var segments = fileName.Split('-');
        return segments.Length < 2 ? null : segments[1];
    }
}
