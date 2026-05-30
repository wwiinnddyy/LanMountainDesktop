using System.Security.Cryptography;
using System.Text.Json;
using Plonds.Shared;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsCommitDeltaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PlondsCommitDeltaBuildResult Build(PlondsCommitDeltaBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hashAlgorithm = PlondsDeltaBuilder.ValidateHashAlgorithmInternal(options.HashAlgorithm);

        var currentPayloadZip = Path.GetFullPath(options.CurrentPayloadZip);
        if (!File.Exists(currentPayloadZip))
        {
            throw new FileNotFoundException("Current payload zip not found.", currentPayloadZip);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var workRoot = Path.Combine(outputRoot, "work", options.Platform);
        var currentExtractRoot = Path.Combine(workRoot, "current");

        Directory.CreateDirectory(outputRoot);
        PayloadUtilities.ExtractZip(currentPayloadZip, currentExtractRoot);

        var changedSourceFiles = PlondsCommitAnalyzer.GetChangedSourceFiles(options.BaselineTag, options.CurrentTag);

        if (changedSourceFiles.Count == 0)
        {
            return FallbackToFileCompare(options, currentPayloadZip, outputRoot, workRoot, hashAlgorithm);
        }

        var mappedArtifacts = PlondsCommitAnalyzer.MapSourceFilesToArtifacts(changedSourceFiles);
        var currentManifest = PayloadUtilities.ScanDirectory(currentExtractRoot);

        var filesMap = new Dictionary<string, PlondsFileEntry>(StringComparer.OrdinalIgnoreCase);
        var changedFilesMap = new Dictionary<string, PlondsChangedFileEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in mappedArtifacts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentManifest.TryGetValue(artifact, out var fingerprint))
            {
                continue;
            }

            var hash = GetHash(fingerprint, hashAlgorithm);
            var action = PlondsConstants.ActionReplace;

            filesMap[artifact] = new PlondsFileEntry(action, hash, fingerprint.Size, hashAlgorithm);
            changedFilesMap[artifact] = new PlondsChangedFileEntry(artifact, hash, fingerprint.Size, hashAlgorithm);
        }

        var changedZipPath = CreateChangedZipFromArtifacts(currentExtractRoot, mappedArtifacts, outputRoot, options.Platform);

        var requiresCleanInstall = mappedArtifacts.Contains(options.LauncherRelativePath, StringComparer.OrdinalIgnoreCase);

        var changedZipMd5 = ComputeMd5Hex(changedZipPath);

        var manifest = new PlondsManifest(
            FormatVersion: PlondsConstants.FormatVersion,
            CurrentVersion: options.CurrentVersion,
            PreviousVersion: options.BaselineVersion ?? options.BaselineTag.TrimStart('v'),
            IsFullUpdate: false,
            RequiresCleanInstall: requiresCleanInstall,
            Channel: options.Channel,
            Platform: options.Platform,
            UpdatedAt: DateTimeOffset.UtcNow,
            CompareMethod: PlondsConstants.CompareMethodCommitAnalyze,
            HashAlgorithm: hashAlgorithm,
            FilesMap: filesMap,
            ChangedFilesMap: changedFilesMap,
            Checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = $"md5:{changedZipMd5}"
            });

        var manifestPath = Path.Combine(outputRoot, "PLONDS.json");
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, manifestJson);

        return new PlondsCommitDeltaBuildResult(
            Platform: options.Platform,
            ChangedZipPath: changedZipPath,
            ManifestPath: manifestPath,
            IsFullUpdate: false,
            RequiresCleanInstall: requiresCleanInstall,
            FellBackToFileCompare: false,
            CurrentVersion: options.CurrentVersion,
            BaselineVersion: options.BaselineVersion,
            ChangedSourceFiles: changedSourceFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            MappedArtifactFiles: mappedArtifacts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private PlondsCommitDeltaBuildResult FallbackToFileCompare(
        PlondsCommitDeltaBuildOptions options,
        string currentPayloadZip,
        string outputRoot,
        string workRoot,
        string hashAlgorithm)
    {
        var fallbackZip = string.IsNullOrWhiteSpace(options.FallbackBaselineZip)
            ? null
            : Path.GetFullPath(options.FallbackBaselineZip);

        if (string.IsNullOrWhiteSpace(fallbackZip) || !File.Exists(fallbackZip))
        {
            var currentExtractRoot = Path.Combine(workRoot, "current");
            PayloadUtilities.ExtractZip(currentPayloadZip, currentExtractRoot);
            var currentManifest = PayloadUtilities.ScanDirectory(currentExtractRoot);

            var filesMap = new Dictionary<string, PlondsFileEntry>(StringComparer.OrdinalIgnoreCase);
            var changedFilesMap = new Dictionary<string, PlondsChangedFileEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in currentManifest.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var fp = currentManifest[path];
                var hash = GetHash(fp, hashAlgorithm);
                filesMap[path] = new PlondsFileEntry(PlondsConstants.ActionAdd, hash, fp.Size, hashAlgorithm);
                changedFilesMap[path] = new PlondsChangedFileEntry(path, hash, fp.Size, hashAlgorithm);
            }

            var changedZipPath = CreateChangedZipFromArtifacts(currentExtractRoot, filesMap.Keys.ToHashSet(), outputRoot, options.Platform);
            var changedZipMd5 = ComputeMd5Hex(changedZipPath);

            var manifest = new PlondsManifest(
                FormatVersion: PlondsConstants.FormatVersion,
                CurrentVersion: options.CurrentVersion,
                PreviousVersion: "0.0.0",
                IsFullUpdate: true,
                RequiresCleanInstall: false,
                Channel: options.Channel,
                Platform: options.Platform,
                UpdatedAt: DateTimeOffset.UtcNow,
                CompareMethod: PlondsConstants.CompareMethodCommitAnalyze,
                HashAlgorithm: hashAlgorithm,
                FilesMap: filesMap,
                ChangedFilesMap: changedFilesMap,
                Checksums: new Dictionary<string, string>
                {
                    ["changed.zip"] = $"md5:{changedZipMd5}"
                });

            var manifestPath = Path.Combine(outputRoot, "PLONDS.json");
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(manifestPath, manifestJson);

            return new PlondsCommitDeltaBuildResult(
                Platform: options.Platform,
                ChangedZipPath: changedZipPath,
                ManifestPath: manifestPath,
                IsFullUpdate: true,
                RequiresCleanInstall: false,
                FellBackToFileCompare: true,
                CurrentVersion: options.CurrentVersion,
                BaselineVersion: options.BaselineVersion,
                ChangedSourceFiles: [],
                MappedArtifactFiles: []);
        }

        var deltaBuilder = new PlondsDeltaBuilder();
        var deltaResult = deltaBuilder.Build(new PlondsDeltaBuildOptions(
            Platform: options.Platform,
            CurrentVersion: options.CurrentVersion,
            CurrentPayloadZip: currentPayloadZip,
            OutputRoot: outputRoot,
            Channel: options.Channel,
            BaselineVersion: options.BaselineVersion,
            BaselinePayloadZip: fallbackZip,
            LauncherRelativePath: options.LauncherRelativePath,
            HashAlgorithm: hashAlgorithm));

        return new PlondsCommitDeltaBuildResult(
            Platform: deltaResult.Platform,
            ChangedZipPath: deltaResult.ChangedZipPath,
            ManifestPath: deltaResult.ManifestPath,
            IsFullUpdate: deltaResult.IsFullUpdate,
            RequiresCleanInstall: deltaResult.RequiresCleanInstall,
            FellBackToFileCompare: true,
            CurrentVersion: deltaResult.CurrentVersion,
            BaselineVersion: deltaResult.BaselineVersion,
            ChangedSourceFiles: [],
            MappedArtifactFiles: []);
    }

    private static string GetHash(PayloadUtilities.FileFingerprint fingerprint, string hashAlgorithm)
    {
        if (hashAlgorithm == PlondsConstants.HashAlgorithmMd5)
        {
            return ComputeMd5Hex(fingerprint.FullPath);
        }

        return fingerprint.Sha256;
    }

    private static string CreateChangedZipFromArtifacts(
        string currentExtractRoot,
        IReadOnlySet<string> artifacts,
        string outputRoot,
        string platform)
    {
        var changedZipPath = Path.Combine(outputRoot, "changed.zip");
        var stagingRoot = Path.Combine(outputRoot, "work", platform, "staging");
        PayloadUtilities.EnsureCleanDirectory(stagingRoot);

        foreach (var artifact in artifacts)
        {
            var sourcePath = Path.Combine(currentExtractRoot, artifact);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destPath = Path.Combine(stagingRoot, artifact);
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

    private static string ComputeMd5Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }
}
