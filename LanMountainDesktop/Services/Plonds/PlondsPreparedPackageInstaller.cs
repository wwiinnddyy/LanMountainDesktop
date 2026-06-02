using System.Security.Cryptography;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsPreparedPackageInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<PlondsInstallResult> InstallAsync(
        PlondsPreparedPackage package,
        string launcherRoot,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (package.Mode is PlondsPackageMode.Full)
            {
                return InstallFullPackage(package, launcherRoot, progress, cancellationToken);
            }

            var manifest = await LoadManifestAsync(package.ManifestPath, cancellationToken).ConfigureAwait(false);
            return InstallDeltaPackage(package, manifest, launcherRoot, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PLONDS.Install", $"Prepared PLONDS package install failed: {ex.Message}");
            return new PlondsInstallResult(false, ex.Message, "plonds_install_failed");
        }
    }

    private static PlondsInstallResult InstallFullPackage(
        PlondsPreparedPackage package,
        string launcherRoot,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.FilesDirectory) || !Directory.Exists(package.FilesDirectory))
        {
            return new PlondsInstallResult(false, "PLONDS full package directory is missing.", "staging_incomplete");
        }

        var sourceAppDirectory = ResolveFullPackageAppDirectory(package.FilesDirectory, package.Version);
        var currentDeployment = FindCurrentDeploymentDirectory(launcherRoot);
        var targetDeployment = BuildNextDeploymentDirectory(launcherRoot, package.Version.ToString());

        progress?.Report(new InstallProgressReport(InstallStage.CreateTarget, "Creating target deployment...", 15, null, 0, 0));
        PrepareTargetDirectory(targetDeployment);
        CopyDirectory(sourceAppDirectory, targetDeployment, cancellationToken, skipMarkers: true);

        progress?.Report(new InstallProgressReport(InstallStage.ActivateDeployment, "Activating deployment...", 85, null, 0, 0));
        ActivateDeployment(currentDeployment, targetDeployment);
        progress?.Report(new InstallProgressReport(InstallStage.Completed, $"Updated to {package.Version}.", 100, null, 0, 0));
        return new PlondsInstallResult(true, null);
    }

    private static PlondsInstallResult InstallDeltaPackage(
        PlondsPreparedPackage package,
        PlondsClientManifest manifest,
        string launcherRoot,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.ChangedDirectory) || !Directory.Exists(package.ChangedDirectory))
        {
            return new PlondsInstallResult(false, "PLONDS changed package directory is missing.", "staging_incomplete");
        }

        var currentDeployment = FindCurrentDeploymentDirectory(launcherRoot);
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            return new PlondsInstallResult(false, "No current deployment was found for PLONDS delta install.", "current_missing");
        }

        var targetDeployment = BuildNextDeploymentDirectory(launcherRoot, package.Version.ToString());
        var fileEntries = manifest.FilesMap ?? new Dictionary<string, PlondsClientFileEntry>();

        progress?.Report(new InstallProgressReport(InstallStage.CreateTarget, "Creating target deployment...", 15, null, 0, fileEntries.Count));
        PrepareTargetDirectory(targetDeployment);
        CopyDirectory(currentDeployment, targetDeployment, cancellationToken, skipMarkers: true);

        var applied = 0;
        foreach (var (relativePath, entry) in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyDeltaEntry(relativePath, entry, manifest, package.ChangedDirectory, targetDeployment);
            applied++;
            progress?.Report(new InstallProgressReport(
                InstallStage.ApplyFiles,
                "Applying PLONDS files...",
                20 + (applied * 45 / Math.Max(1, fileEntries.Count)),
                relativePath,
                applied,
                fileEntries.Count));
        }

        VerifyFiles(fileEntries, targetDeployment, progress, cancellationToken);

        progress?.Report(new InstallProgressReport(InstallStage.ActivateDeployment, "Activating deployment...", 85, null, fileEntries.Count, fileEntries.Count));
        ActivateDeployment(currentDeployment, targetDeployment);
        progress?.Report(new InstallProgressReport(InstallStage.Completed, $"Updated to {package.Version}.", 100, null, fileEntries.Count, fileEntries.Count));
        return new PlondsInstallResult(true, null);
    }

    private static async Task<PlondsClientManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            throw new FileNotFoundException("PLONDS manifest is missing.", manifestPath);
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<PlondsClientManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException("PLONDS manifest is empty or invalid.");
    }

    private static void ApplyDeltaEntry(
        string relativePath,
        PlondsClientFileEntry entry,
        PlondsClientManifest manifest,
        string changedDirectory,
        string targetDeployment)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var targetPath = Path.GetFullPath(Path.Combine(targetDeployment, normalizedPath));
        EnsureChildPath(targetDeployment, targetPath);

        var action = string.IsNullOrWhiteSpace(entry.Action) ? "replace" : entry.Action.Trim();
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            return;
        }

        if (string.Equals(action, "reuse", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var archivePath = manifest.ChangedFilesMap is not null &&
                          manifest.ChangedFilesMap.TryGetValue(relativePath, out var changedEntry) &&
                          !string.IsNullOrWhiteSpace(changedEntry.ArchivePath)
            ? changedEntry.ArchivePath
            : normalizedPath;

        var sourcePath = Path.GetFullPath(Path.Combine(changedDirectory, NormalizeRelativePath(archivePath)));
        EnsureChildPath(changedDirectory, sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"PLONDS changed file is missing: {archivePath}", sourcePath);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void VerifyFiles(
        IReadOnlyDictionary<string, PlondsClientFileEntry> fileEntries,
        string targetDeployment,
        IProgress<InstallProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var verified = 0;
        foreach (var (relativePath, entry) in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(entry.Action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                verified++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Hash))
            {
                verified++;
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(targetDeployment, NormalizeRelativePath(relativePath)));
            EnsureChildPath(targetDeployment, targetPath);
            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException($"Expected PLONDS target file was not created: {relativePath}", targetPath);
            }

            var actual = ComputeHash(targetPath, entry.HashAlgorithm);
            if (!string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"PLONDS target hash mismatch for {relativePath}. Expected {entry.Hash}, actual {actual}.");
            }

            verified++;
            progress?.Report(new InstallProgressReport(
                InstallStage.VerifyHashes,
                "Verifying PLONDS files...",
                65 + (verified * 15 / Math.Max(1, fileEntries.Count)),
                relativePath,
                verified,
                fileEntries.Count));
        }
    }

    private static void PrepareTargetDirectory(string targetDeployment)
    {
        if (Directory.Exists(targetDeployment))
        {
            Directory.Delete(targetDeployment, recursive: true);
        }

        Directory.CreateDirectory(targetDeployment);
        File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken,
        bool skipMarkers = false)
    {
        var resolvedSource = Path.GetFullPath(sourceDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(resolvedSource, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativePath(Path.GetRelativePath(resolvedSource, sourcePath));
            if (skipMarkers && IsDeploymentMarker(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
            EnsureChildPath(targetDirectory, targetPath);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent))
            {
                Directory.CreateDirectory(targetParent);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static string? FindCurrentDeploymentDirectory(string launcherRoot)
    {
        if (!Directory.Exists(launcherRoot))
        {
            return null;
        }

        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        return Directory.GetDirectories(launcherRoot, "app-*", SearchOption.TopDirectoryOnly)
            .Where(path => !File.Exists(Path.Combine(path, ".destroy")))
            .Where(path => !File.Exists(Path.Combine(path, ".partial")))
            .Where(path => File.Exists(Path.Combine(path, executable)) || File.Exists(Path.Combine(path, ".current")))
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

    private static string ResolveFullPackageAppDirectory(string filesDirectory, Version version)
    {
        var resolvedRoot = Path.GetFullPath(filesDirectory);
        if (File.Exists(Path.Combine(resolvedRoot, "LanMountainDesktop.exe")))
        {
            return resolvedRoot;
        }

        var exactAppDirectory = Path.Combine(resolvedRoot, $"app-{version}");
        if (Directory.Exists(exactAppDirectory) &&
            File.Exists(Path.Combine(exactAppDirectory, "LanMountainDesktop.exe")))
        {
            return exactAppDirectory;
        }

        var appDirectory = Directory.GetDirectories(resolvedRoot, "app-*", SearchOption.TopDirectoryOnly)
            .Where(path => File.Exists(Path.Combine(path, "LanMountainDesktop.exe")))
            .Select(path => new
            {
                Path = path,
                Version = ParseVersionFromDirectory(path)
            })
            .OrderByDescending(item => item.Version)
            .Select(item => item.Path)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            return appDirectory;
        }

        throw new DirectoryNotFoundException("PLONDS full package does not contain an app deployment directory.");
    }

    private static string BuildNextDeploymentDirectory(string launcherRoot, string targetVersion)
    {
        Directory.CreateDirectory(launcherRoot);
        var sanitized = SanitizePathSegment(targetVersion);
        var index = 0;
        while (true)
        {
            var candidate = Path.Combine(launcherRoot, $"app-{sanitized}-{index}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static void ActivateDeployment(string? currentDeployment, string targetDeployment)
    {
        File.WriteAllText(Path.Combine(targetDeployment, ".current"), string.Empty);
        TryDeleteFile(Path.Combine(targetDeployment, ".partial"));
        TryDeleteFile(Path.Combine(targetDeployment, ".destroy"));

        if (!string.IsNullOrWhiteSpace(currentDeployment) && Directory.Exists(currentDeployment))
        {
            TryDeleteFile(Path.Combine(currentDeployment, ".current"));
            File.WriteAllText(Path.Combine(currentDeployment, ".destroy"), string.Empty);
        }
    }

    private static string ComputeHash(string filePath, string algorithm)
    {
        using var stream = File.OpenRead(filePath);
        var normalized = string.IsNullOrWhiteSpace(algorithm) ? "sha256" : algorithm.Trim().ToLowerInvariant();
        var hash = normalized == "md5"
            ? MD5.HashData(stream)
            : SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Version ParseVersionFromDirectory(string path)
    {
        var fileName = Path.GetFileName(path);
        var segments = fileName.Split('-');
        return segments.Length >= 2 && Version.TryParse(segments[1], out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static void EnsureChildPath(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedChild = Path.GetFullPath(child);
        if (!resolvedChild.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !resolvedChild.StartsWith(resolvedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resolvedParent, resolvedChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"PLONDS path escapes its root: {child}");
        }
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    private static bool IsDeploymentMarker(string relativePath)
    {
        return relativePath is ".current" or ".partial" or ".destroy";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "0.0.0" : sanitized;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
