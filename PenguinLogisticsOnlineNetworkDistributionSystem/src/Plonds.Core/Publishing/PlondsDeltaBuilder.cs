using System.Security.Cryptography;
using System.Text.Json;
using Plonds.Shared;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsDeltaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PlondsDeltaBuildResult Build(PlondsDeltaBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hashAlgorithm = ValidateHashAlgorithm(options.HashAlgorithm);

        var currentPayloadZip = Path.GetFullPath(options.CurrentPayloadZip);
        if (!File.Exists(currentPayloadZip))
        {
            throw new FileNotFoundException("Current payload zip not found.", currentPayloadZip);
        }

        var baselinePayloadZip = string.IsNullOrWhiteSpace(options.BaselinePayloadZip)
            ? null
            : Path.GetFullPath(options.BaselinePayloadZip);
        if (!string.IsNullOrWhiteSpace(baselinePayloadZip) && !File.Exists(baselinePayloadZip))
        {
            throw new FileNotFoundException("Baseline payload zip not found.", baselinePayloadZip);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var workRoot = Path.Combine(outputRoot, "work", options.Platform);
        var currentExtractRoot = Path.Combine(workRoot, "current");
        var baselineExtractRoot = Path.Combine(workRoot, "baseline");

        Directory.CreateDirectory(outputRoot);
        PayloadUtilities.ExtractZip(currentPayloadZip, currentExtractRoot);

        var isFullUpdate = string.IsNullOrWhiteSpace(baselinePayloadZip);
        if (!isFullUpdate)
        {
            PayloadUtilities.ExtractZip(baselinePayloadZip!, baselineExtractRoot);
        }

        var currentAppRoot = ResolvePayloadAppRoot(currentExtractRoot, options.CurrentVersion);
        var baselineAppRoot = isFullUpdate
            ? null
            : ResolvePayloadAppRoot(baselineExtractRoot, options.BaselineVersion);

        var previousManifest = isFullUpdate
            ? new Dictionary<string, PayloadUtilities.FileFingerprint>(StringComparer.OrdinalIgnoreCase)
            : PayloadUtilities.ScanDirectory(baselineAppRoot);
        var currentManifest = PayloadUtilities.ScanDirectory(currentAppRoot);

        var filesMap = BuildFilesMap(previousManifest, currentManifest, hashAlgorithm);
        var changedFilesMap = BuildChangedFilesMap(filesMap, hashAlgorithm);

        var changedZipPath = CreateChangedZip(currentAppRoot, filesMap, outputRoot, options.Platform);

        var previousRootManifest = isFullUpdate
            ? new Dictionary<string, PayloadUtilities.FileFingerprint>(StringComparer.OrdinalIgnoreCase)
            : PayloadUtilities.ScanDirectory(baselineExtractRoot);
        var currentRootManifest = PayloadUtilities.ScanDirectory(currentExtractRoot);
        var launcherChanged = DetectLauncherChange(previousRootManifest, currentRootManifest, options.LauncherRelativePath);
        var requiresCleanInstall = launcherChanged && !isFullUpdate;

        var changedZipMd5 = ComputeMd5Hex(changedZipPath);

        var manifest = new PlondsManifest(
            FormatVersion: PlondsConstants.FormatVersion,
            CurrentVersion: options.CurrentVersion,
            PreviousVersion: options.BaselineVersion ?? "0.0.0",
            IsFullUpdate: isFullUpdate,
            RequiresCleanInstall: requiresCleanInstall,
            Channel: options.Channel,
            Platform: options.Platform,
            UpdatedAt: DateTimeOffset.UtcNow,
            FilesMap: filesMap,
            ChangedFilesMap: changedFilesMap,
            Checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = $"md5:{changedZipMd5}"
            });

        var manifestPath = Path.Combine(outputRoot, "PLONDS.json");
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, manifestJson);

        return new PlondsDeltaBuildResult(
            Platform: options.Platform,
            ChangedZipPath: changedZipPath,
            ManifestPath: manifestPath,
            IsFullUpdate: isFullUpdate,
            RequiresCleanInstall: requiresCleanInstall,
            CurrentVersion: options.CurrentVersion,
            BaselineVersion: options.BaselineVersion);
    }

    private static string ValidateHashAlgorithm(string algorithm)
    {
        var normalized = algorithm.Trim().ToLowerInvariant();
        if (normalized is not (PlondsConstants.HashAlgorithmSha256 or PlondsConstants.HashAlgorithmMd5))
        {
            throw new ArgumentException($"Unsupported hash algorithm: {algorithm}. Supported: sha256, md5");
        }

        return normalized;
    }

    private static Dictionary<string, PlondsFileEntry> BuildFilesMap(
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> previousManifest,
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> currentManifest,
        string hashAlgorithm)
    {
        var filesMap = new Dictionary<string, PlondsFileEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in currentManifest.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var current = currentManifest[path];
            var currentHash = ComputeHash(current.FullPath, hashAlgorithm);

            if (previousManifest.TryGetValue(path, out var previous))
            {
                var previousHash = ComputeHash(previous.FullPath, hashAlgorithm);
                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    filesMap[path] = new PlondsFileEntry(PlondsConstants.ActionReuse, currentHash, current.Size, hashAlgorithm);
                    continue;
                }

                filesMap[path] = new PlondsFileEntry(PlondsConstants.ActionReplace, currentHash, current.Size, hashAlgorithm);
                continue;
            }

            filesMap[path] = new PlondsFileEntry(PlondsConstants.ActionAdd, currentHash, current.Size, hashAlgorithm);
        }

        foreach (var path in previousManifest.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentManifest.ContainsKey(path))
            {
                filesMap[path] = new PlondsFileEntry(PlondsConstants.ActionDelete, string.Empty, 0, hashAlgorithm);
            }
        }

        return filesMap;
    }

    private static Dictionary<string, PlondsChangedFileEntry> BuildChangedFilesMap(
        IReadOnlyDictionary<string, PlondsFileEntry> filesMap,
        string hashAlgorithm)
    {
        var changedFilesMap = new Dictionary<string, PlondsChangedFileEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, entry) in filesMap)
        {
            if (entry.Action is PlondsConstants.ActionAdd or PlondsConstants.ActionReplace)
            {
                changedFilesMap[path] = new PlondsChangedFileEntry(path, entry.Hash, entry.Size, hashAlgorithm);
            }
        }

        return changedFilesMap;
    }

    private static string CreateChangedZip(
        string currentExtractRoot,
        IReadOnlyDictionary<string, PlondsFileEntry> filesMap,
        string outputRoot,
        string platform)
    {
        var changedZipPath = Path.Combine(outputRoot, "changed.zip");
        var stagingRoot = Path.Combine(outputRoot, "work", platform, "staging");
        PayloadUtilities.EnsureCleanDirectory(stagingRoot);

        foreach (var (path, entry) in filesMap)
        {
            if (entry.Action is not (PlondsConstants.ActionAdd or PlondsConstants.ActionReplace))
            {
                continue;
            }

            var sourcePath = Path.Combine(currentExtractRoot, path);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destPath = Path.Combine(stagingRoot, path);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destPath, overwrite: true);
        }

        PayloadUtilities.CreatePayloadZip(stagingRoot, changedZipPath);
        return changedZipPath;
    }

    private static bool DetectLauncherChange(
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> previousManifest,
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> currentManifest,
        string launcherRelativePath)
    {
        var normalizedPath = launcherRelativePath.Replace('\\', '/');

        if (!currentManifest.TryGetValue(normalizedPath, out var current))
        {
            return false;
        }

        if (!previousManifest.TryGetValue(normalizedPath, out var previous))
        {
            return true;
        }

        return !string.Equals(current.Sha256, previous.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolvePayloadAppRoot(string extractRoot, string? version)
    {
        var resolvedRoot = Path.GetFullPath(extractRoot);
        if (File.Exists(Path.Combine(resolvedRoot, "LanMountainDesktop.exe")))
        {
            return resolvedRoot;
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            var versionedAppRoot = Path.Combine(resolvedRoot, $"app-{version.Trim().TrimStart('v', 'V')}");
            if (Directory.Exists(versionedAppRoot) &&
                File.Exists(Path.Combine(versionedAppRoot, "LanMountainDesktop.exe")))
            {
                return versionedAppRoot;
            }
        }

        var appRoots = Directory.Exists(resolvedRoot)
            ? Directory.GetDirectories(resolvedRoot, "app-*", SearchOption.TopDirectoryOnly)
                .Where(path => File.Exists(Path.Combine(path, "LanMountainDesktop.exe")))
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return appRoots.FirstOrDefault() ?? resolvedRoot;
    }

    internal static string ComputeHash(string filePath, string hashAlgorithm)
    {
        using var stream = File.OpenRead(filePath);
        var hash = hashAlgorithm == PlondsConstants.HashAlgorithmMd5
            ? MD5.HashData(stream)
            : SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeMd5Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }
}
