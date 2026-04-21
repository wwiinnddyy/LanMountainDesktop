using Plonds.Core.Security;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsDeltaBuilder
{
    private readonly RsaFileSigner _signer = new();

    public PlondsDeltaBuildResult Build(PlondsDeltaBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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
        var objectsRoot = Path.Combine(workRoot, "objects");
        var releaseAssetsRoot = Path.Combine(outputRoot, "release-assets");
        var summaryRoot = Path.Combine(outputRoot, "platform-summaries");

        Directory.CreateDirectory(releaseAssetsRoot);
        Directory.CreateDirectory(summaryRoot);
        PayloadUtilities.ExtractZip(currentPayloadZip, currentExtractRoot);

        var useFullPayload = options.IsFullPayload || string.IsNullOrWhiteSpace(baselinePayloadZip);
        if (useFullPayload)
        {
            PayloadUtilities.EnsureCleanDirectory(baselineExtractRoot);
        }
        else
        {
            PayloadUtilities.ExtractZip(baselinePayloadZip!, baselineExtractRoot);
        }

        PayloadUtilities.EnsureCleanDirectory(objectsRoot);

        var previousManifest = useFullPayload
            ? new Dictionary<string, PayloadUtilities.FileFingerprint>(StringComparer.OrdinalIgnoreCase)
            : PayloadUtilities.ScanDirectory(baselineExtractRoot);
        var currentManifest = PayloadUtilities.ScanDirectory(currentExtractRoot);
        var fileEntries = BuildFileEntries(previousManifest, currentManifest, objectsRoot);

        var updateAssetName = $"update-{options.Platform}.zip";
        var fileMapAssetName = $"plonds-filemap-{options.Platform}.json";
        var fileMapSignatureAssetName = fileMapAssetName + ".sig";
        var distributionId = $"plonds-{options.CurrentVersion}-{options.Platform}";
        var updateArchivePath = Path.Combine(releaseAssetsRoot, updateAssetName);
        var fileMapPath = Path.Combine(releaseAssetsRoot, fileMapAssetName);
        var fileMapSignaturePath = Path.Combine(releaseAssetsRoot, fileMapSignatureAssetName);

        PayloadUtilities.CreatePayloadZip(objectsRoot, updateArchivePath);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["protocol"] = "PLONDS",
            ["channel"] = options.Channel,
            ["releaseTag"] = options.CurrentTag,
            ["baselineTag"] = options.BaselineTag ?? string.Empty,
            ["baselineVersion"] = options.BaselineVersion ?? "0.0.0",
            ["targetVersion"] = options.CurrentVersion,
            ["isFullPayload"] = useFullPayload ? "true" : "false"
        };

        var component = new ComponentDocument(
            Name: "app",
            Version: options.CurrentVersion,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component"] = "app",
                ["mode"] = "file-object"
            },
            Files: fileEntries);

        var fileMap = new FileMapDocument(
            FormatVersion: "1.0",
            DistributionId: distributionId,
            FromVersion: options.BaselineVersion ?? "0.0.0",
            ToVersion: options.CurrentVersion,
            Version: options.CurrentVersion,
            Platform: options.Platform,
            Arch: PayloadUtilities.ResolveArch(options.Platform),
            Channel: options.Channel,
            GeneratedAt: DateTimeOffset.UtcNow,
            Metadata: metadata,
            Components: [component],
            Files: fileEntries);

        PayloadUtilities.WriteJson(fileMapPath, fileMap);
        _signer.SignFile(fileMapPath, options.PrivateKeyPath, fileMapSignaturePath);

        var summary = new PlondsReleasePlatformEntry(
            Platform: options.Platform,
            DistributionId: distributionId,
            BaselineTag: options.BaselineTag,
            BaselineVersion: options.BaselineVersion ?? "0.0.0",
            TargetVersion: options.CurrentVersion,
            IsFullPayload: useFullPayload,
            FilesZipAsset: $"files-{options.Platform}.zip",
            UpdateZipAsset: updateAssetName,
            FileMapAsset: fileMapAssetName,
            FileMapSignatureAsset: fileMapSignatureAssetName,
            Sha256: PayloadUtilities.ComputeSha256(updateArchivePath));

        var summaryPath = Path.Combine(summaryRoot, $"platform-summary-{options.Platform}.json");
        PayloadUtilities.WriteJson(summaryPath, summary);

        return new PlondsDeltaBuildResult(
            options.Platform,
            distributionId,
            updateArchivePath,
            fileMapPath,
            fileMapSignaturePath,
            summaryPath,
            useFullPayload,
            options.BaselineTag,
            options.BaselineVersion,
            options.CurrentVersion);
    }

    private static List<FileEntryDocument> BuildFileEntries(
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> previousManifest,
        IReadOnlyDictionary<string, PayloadUtilities.FileFingerprint> currentManifest,
        string objectsRoot)
    {
        var result = new List<FileEntryDocument>();

        foreach (var path in currentManifest.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
        {
            var current = currentManifest[path];
            if (previousManifest.TryGetValue(path, out var previous) &&
                string.Equals(current.Sha256, previous.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new FileEntryDocument(
                    Path: path,
                    Action: "reuse",
                    Sha256: current.Sha256,
                    Size: current.Size,
                    ObjectPath: null,
                    ObjectKey: null,
                    Metadata: null));
                continue;
            }

            var action = previousManifest.ContainsKey(path) ? "replace" : "add";
            var objectPath = PayloadUtilities.CopyObject(current.FullPath, objectsRoot, current.Sha256);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = "file-object"
            };
            if (!string.IsNullOrWhiteSpace(current.UnixFileMode))
            {
                metadata["unixFileMode"] = current.UnixFileMode!;
            }

            result.Add(new FileEntryDocument(
                Path: path,
                Action: action,
                Sha256: current.Sha256,
                Size: current.Size,
                ObjectPath: objectPath,
                ObjectKey: objectPath,
                Metadata: metadata));
        }

        foreach (var path in previousManifest.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (currentManifest.ContainsKey(path))
            {
                continue;
            }

            result.Add(new FileEntryDocument(
                Path: path,
                Action: "delete",
                Sha256: string.Empty,
                Size: 0,
                ObjectPath: null,
                ObjectKey: null,
                Metadata: null));
        }

        return result;
    }

    private sealed record FileMapDocument(
        string FormatVersion,
        string DistributionId,
        string FromVersion,
        string ToVersion,
        string Version,
        string Platform,
        string Arch,
        string Channel,
        DateTimeOffset GeneratedAt,
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyList<ComponentDocument> Components,
        IReadOnlyList<FileEntryDocument> Files);

    private sealed record ComponentDocument(
        string Name,
        string Version,
        IReadOnlyDictionary<string, string>? Metadata,
        IReadOnlyList<FileEntryDocument> Files);

    private sealed record FileEntryDocument(
        string Path,
        string Action,
        string Sha256,
        long Size,
        string? ObjectPath,
        string? ObjectKey,
        IReadOnlyDictionary<string, string>? Metadata);
}
