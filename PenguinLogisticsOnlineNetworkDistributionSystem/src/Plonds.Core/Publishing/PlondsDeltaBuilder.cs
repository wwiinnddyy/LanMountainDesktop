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
        var updateBaseUrl = string.IsNullOrWhiteSpace(options.UpdateBaseUrl)
            ? null
            : options.UpdateBaseUrl.TrimEnd('/');
        var repoBaseUrl = string.IsNullOrWhiteSpace(updateBaseUrl)
            ? null
            : $"{updateBaseUrl}/repo/sha256";
        var fileEntries = BuildFileEntries(previousManifest, currentManifest, objectsRoot, repoBaseUrl);

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

        var generatedAt = DateTimeOffset.UtcNow;
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
            GeneratedAt: generatedAt,
            Metadata: metadata,
            Components: [component],
            Files: fileEntries);

        PayloadUtilities.WriteJson(fileMapPath, fileMap);
        _signer.SignFile(fileMapPath, options.PrivateKeyPath, fileMapSignaturePath);

        if (!string.IsNullOrWhiteSpace(options.StaticOutputRoot) && !string.IsNullOrWhiteSpace(updateBaseUrl))
        {
            WriteStaticLayout(
                options,
                component,
                objectsRoot,
                distributionId,
                fileMapPath,
                fileMapSignaturePath,
                Path.GetFullPath(options.StaticOutputRoot),
                updateBaseUrl,
                generatedAt);
        }

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
        string objectsRoot,
        string? repoBaseUrl)
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
                    ObjectUrl: null,
                    Metadata: null));
                continue;
            }

            var action = previousManifest.ContainsKey(path) ? "replace" : "add";
            var objectPath = PayloadUtilities.CopyObject(current.FullPath, objectsRoot, current.Sha256);
            var objectUrl = string.IsNullOrWhiteSpace(repoBaseUrl)
                ? null
                : $"{repoBaseUrl.TrimEnd('/')}/{objectPath}";
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
                ObjectUrl: objectUrl,
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
                ObjectUrl: null,
                Metadata: null));
        }

        return result;
    }

    private static void WriteStaticLayout(
        PlondsDeltaBuildOptions options,
        ComponentDocument component,
        string objectsRoot,
        string distributionId,
        string fileMapPath,
        string fileMapSignaturePath,
        string staticOutputRoot,
        string updateBaseUrl,
        DateTimeOffset generatedAt)
    {
        var repoRoot = Path.Combine(staticOutputRoot, "repo", "sha256");
        var manifestRoot = Path.Combine(staticOutputRoot, "manifests", distributionId);
        var distributionRoot = Path.Combine(staticOutputRoot, "meta", "distributions");
        var channelRoot = Path.Combine(staticOutputRoot, "meta", "channels", options.Channel, options.Platform);

        CopyDirectory(objectsRoot, repoRoot);
        Directory.CreateDirectory(manifestRoot);
        File.Copy(fileMapPath, Path.Combine(manifestRoot, "plonds-filemap.json"), overwrite: true);
        File.Copy(fileMapSignaturePath, Path.Combine(manifestRoot, "plonds-filemap.json.sig"), overwrite: true);

        var fileMapUrl = $"{updateBaseUrl}/manifests/{Uri.EscapeDataString(distributionId)}/plonds-filemap.json";
        var distribution = new DistributionDocument(
            DistributionId: distributionId,
            Version: options.CurrentVersion,
            SourceVersion: options.BaselineVersion ?? "0.0.0",
            Channel: options.Channel,
            Platform: options.Platform,
            Arch: PayloadUtilities.ResolveArch(options.Platform),
            PublishedAt: generatedAt,
            FileMapUrl: fileMapUrl,
            FileMapSignatureUrl: fileMapUrl + ".sig",
            Components: [component],
            InstallerMirrors: [],
            Capabilities: ["file-object"],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["protocol"] = "PLONDS",
                ["releaseTag"] = options.CurrentTag,
                ["baselineTag"] = options.BaselineTag ?? string.Empty,
                ["baselineVersion"] = options.BaselineVersion ?? "0.0.0",
                ["targetVersion"] = options.CurrentVersion,
                ["isFullPayload"] = options.IsFullPayload ? "true" : "false"
            });

        var latest = new LatestPointerDocument(
            DistributionId: distributionId,
            Version: options.CurrentVersion,
            Channel: options.Channel,
            Platform: options.Platform,
            PublishedAt: generatedAt);

        PayloadUtilities.WriteJson(Path.Combine(distributionRoot, distributionId + ".json"), distribution);
        PayloadUtilities.WriteJson(Path.Combine(channelRoot, "latest.json"), latest);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destinationPath = Path.Combine(destinationDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
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
        string? ObjectUrl,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record DistributionDocument(
        string DistributionId,
        string Version,
        string SourceVersion,
        string Channel,
        string Platform,
        string Arch,
        DateTimeOffset PublishedAt,
        string FileMapUrl,
        string FileMapSignatureUrl,
        IReadOnlyList<ComponentDocument> Components,
        IReadOnlyList<InstallerMirrorDocument> InstallerMirrors,
        IReadOnlyList<string> Capabilities,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record LatestPointerDocument(
        string DistributionId,
        string Version,
        string Channel,
        string Platform,
        DateTimeOffset PublishedAt);

    private sealed record InstallerMirrorDocument(
        string Platform,
        string? Url,
        string? FileName,
        string? Sha256,
        long Size);
}
