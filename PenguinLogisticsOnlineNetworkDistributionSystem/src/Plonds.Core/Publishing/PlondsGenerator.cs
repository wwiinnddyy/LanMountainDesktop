using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plonds.Core.Publishing;

public sealed class PlondsGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PlatformPublishResult Generate(PlondsGenerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var currentDirectory = Path.GetFullPath(options.CurrentDirectory);
        if (!Directory.Exists(currentDirectory))
        {
            throw new DirectoryNotFoundException($"Current directory not found: {currentDirectory}");
        }

        var previousDirectory = string.IsNullOrWhiteSpace(options.PreviousDirectory)
            ? null
            : Path.GetFullPath(options.PreviousDirectory);

        var distributionId = string.IsNullOrWhiteSpace(options.DistributionId)
            ? $"plonds-{options.CurrentVersion}-{options.Platform}"
            : options.DistributionId.Trim();

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var repoRoot = Path.Combine(outputRoot, "repo", "sha256");
        var manifestsRoot = Path.Combine(outputRoot, "manifests", distributionId);
        var metaDistributionRoot = Path.Combine(outputRoot, "meta", "distributions");
        var metaChannelRoot = Path.Combine(outputRoot, "meta", "channels", options.Channel, options.Platform);
        var installerMirrorRoot = Path.Combine(outputRoot, "installers", options.Platform, options.CurrentVersion);

        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(manifestsRoot);
        Directory.CreateDirectory(metaDistributionRoot);
        Directory.CreateDirectory(metaChannelRoot);

        var previousManifest = options.IsFullPayloadRelease
            ? new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase)
            : ScanDirectory(previousDirectory);
        var currentManifest = ScanDirectory(currentDirectory);
        var fileEntries = BuildFileEntries(previousManifest, currentManifest, repoRoot, options.RepoBaseUrl);
        var installerMirrors = BuildInstallerMirrors(options.Platform, installerMirrorRoot, options.InstallerDirectory, options.InstallerBaseUrl);
        var publishedAt = DateTimeOffset.UtcNow;
        var generatedAt = DateTimeOffset.UtcNow;
        var baselineVersion = string.IsNullOrWhiteSpace(options.BaselineVersion)
            ? options.PreviousVersion
            : options.BaselineVersion;
        var arch = ResolveArch(options.Platform);

        var fileMap = new FileMapDocument(
            FormatVersion: "2.0",
            DistributionId: distributionId,
            FromVersion: options.PreviousVersion,
            ToVersion: options.CurrentVersion,
            Version: options.CurrentVersion,
            Platform: options.Platform,
            Arch: arch,
            Channel: options.Channel,
            PublishedAt: publishedAt,
            GeneratedAt: generatedAt,
            BaselineVersion: baselineVersion,
            Capabilities: ["file-object", "compressed-object"],
            Components:
            [
                new ComponentDocument(
                    Id: "app",
                    Root: "/",
                    Mode: "file-object",
                    Files: fileEntries,
                    Metadata: new Dictionary<string, string> { ["component"] = "app" })
            ],
            Metadata: new Dictionary<string, string>
            {
                ["protocol"] = "PLONDS",
                ["mode"] = "file-object",
                ["baselineVersion"] = baselineVersion,
                ["incrementalStrategy"] = options.IncrementalStrategy,
                ["isFullPayloadRelease"] = options.IsFullPayloadRelease ? "true" : "false",
                ["sourceCommit"] = options.SourceCommit ?? string.Empty,
                ["baselineRef"] = options.BaselineRef ?? string.Empty,
                ["commitRangeStart"] = options.CommitRangeStart ?? string.Empty,
                ["commitRangeEnd"] = options.CommitRangeEnd ?? string.Empty
            });

        var distribution = new DistributionDocument(
            DistributionId: distributionId,
            Version: options.CurrentVersion,
            Channel: options.Channel,
            Platform: options.Platform,
            Arch: arch,
            PublishedAt: publishedAt,
            FileMapUrl: options.FileMapUrl,
            FileMapSignatureUrl: options.FileMapSignatureUrl,
            Components: fileMap.Components,
            InstallerMirrors: installerMirrors,
            Capabilities: ["file-object", "compressed-object"],
            Metadata: new Dictionary<string, string>
            {
                ["protocol"] = "PLONDS",
                ["baselineVersion"] = baselineVersion,
                ["incrementalStrategy"] = options.IncrementalStrategy,
                ["isFullPayloadRelease"] = options.IsFullPayloadRelease ? "true" : "false",
                ["sourceCommit"] = options.SourceCommit ?? string.Empty,
                ["baselineRef"] = options.BaselineRef ?? string.Empty,
                ["commitRangeStart"] = options.CommitRangeStart ?? string.Empty,
                ["commitRangeEnd"] = options.CommitRangeEnd ?? string.Empty
            });

        var latest = new LatestPointerDocument(
            DistributionId: distributionId,
            Version: options.CurrentVersion,
            Channel: options.Channel,
            Platform: options.Platform,
            PublishedAt: publishedAt);

        var fileMapPath = Path.Combine(manifestsRoot, "plonds-filemap.json");
        var distributionPath = Path.Combine(metaDistributionRoot, distributionId + ".json");
        var latestPath = Path.Combine(metaChannelRoot, "latest.json");

        WriteJson(fileMapPath, fileMap);
        WriteJson(distributionPath, distribution);
        WriteJson(latestPath, latest);

        return new PlatformPublishResult(
            options.Platform,
            distributionId,
            currentDirectory,
            previousDirectory,
            options.PreviousVersion,
            fileMapPath,
            fileMapPath + ".sig",
            distributionPath,
            latestPath,
            installerMirrors.Select(x => x.FileName ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
    }

    public static void WriteBundle(string fileMapPath, string signatureBase64)
    {
        var fileMapJson = File.ReadAllText(fileMapPath);
        WriteBundle(fileMapPath, fileMapJson, signatureBase64);
    }

    private static Dictionary<string, FileFingerprint> ScanDirectory(string? root)
    {
        var manifest = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return manifest;
        }

        var resolvedRoot = Path.GetFullPath(root);
        foreach (var filePath in Directory.EnumerateFiles(resolvedRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(resolvedRoot, filePath).Replace('\\', '/');
            if (ShouldIgnore(relativePath))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            manifest[relativePath] = new FileFingerprint(relativePath, filePath, ComputeSha256(filePath), fileInfo.Length);
        }

        return manifest;
    }

    private static List<FileEntryDocument> BuildFileEntries(
        Dictionary<string, FileFingerprint> previousManifest,
        Dictionary<string, FileFingerprint> currentManifest,
        string repoRoot,
        string? repoBaseUrl)
    {
        var entries = new List<FileEntryDocument>();

        foreach (var path in currentManifest.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var current = currentManifest[path];
            if (previousManifest.TryGetValue(path, out var previous) &&
                string.Equals(current.Sha256, previous.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new FileEntryDocument(
                    Path: path,
                    Action: "reuse",
                    Sha256: current.Sha256,
                    Size: current.Size,
                    Mode: "file-object",
                    ObjectKey: null,
                    ObjectUrl: null,
                    ArchiveSha256: null,
                    Metadata: new Dictionary<string, string> { ["reuseVerified"] = "true" }));
                continue;
            }

            var action = previousManifest.ContainsKey(path) ? "replace" : "add";
            var (objectKey, archiveSha256, mode) = CopyContentObjectWithCompression(
                current.FullPath, repoRoot, current.Sha256, current.Size);
            var objectUrl = string.IsNullOrWhiteSpace(repoBaseUrl)
                ? null
                : $"{repoBaseUrl.TrimEnd('/')}/{objectKey}";

            entries.Add(new FileEntryDocument(
                Path: path,
                Action: action,
                Sha256: current.Sha256,
                Size: current.Size,
                Mode: mode,
                ObjectKey: objectKey,
                ObjectUrl: objectUrl,
                ArchiveSha256: string.IsNullOrEmpty(archiveSha256) ? null : archiveSha256,
                Metadata: new Dictionary<string, string> { ["mode"] = mode }));
        }

        foreach (var path in previousManifest.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentManifest.ContainsKey(path))
            {
                entries.Add(new FileEntryDocument(
                    Path: path,
                    Action: "delete",
                    Sha256: string.Empty,
                    Size: 0,
                    Mode: "file-object",
                    ObjectKey: null,
                    ObjectUrl: null,
                    ArchiveSha256: null,
                    Metadata: null));
            }
        }

        return entries;
    }

    private static List<InstallerMirrorDocument> BuildInstallerMirrors(
        string platform,
        string installerMirrorRoot,
        string? installerSourceDirectory,
        string? installerBaseUrl)
    {
        var result = new List<InstallerMirrorDocument>();
        if (string.IsNullOrWhiteSpace(installerSourceDirectory) || !Directory.Exists(installerSourceDirectory))
        {
            return result;
        }

        Directory.CreateDirectory(installerMirrorRoot);
        foreach (var sourceFile in Directory.EnumerateFiles(installerSourceDirectory))
        {
            var fileName = Path.GetFileName(sourceFile);
            var destinationPath = Path.Combine(installerMirrorRoot, fileName);
            File.Copy(sourceFile, destinationPath, overwrite: true);

            var url = string.IsNullOrWhiteSpace(installerBaseUrl)
                ? null
                : $"{installerBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}";
            result.Add(new InstallerMirrorDocument(
                Platform: platform,
                Arch: ResolveArch(platform),
                Url: url,
                Name: fileName,
                FileName: fileName,
                Sha256: ComputeSha256(destinationPath),
                Size: new FileInfo(destinationPath).Length));
        }

        return result;
    }

    private static string ResolveArch(string platform)
    {
        if (platform.EndsWith("-x86", StringComparison.OrdinalIgnoreCase))
        {
            return "x86";
        }

        if (platform.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase))
        {
            return "arm64";
        }

        return "x64";
    }

    private static bool ShouldIgnore(string relativePath)
    {
        var normalized = relativePath.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.Equals(".current", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".partial", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".destroy", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".current/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".partial/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".destroy/", StringComparison.OrdinalIgnoreCase);
    }

    private static string CopyContentObject(string sourcePath, string repoRoot, string sha256)
    {
        var prefix = sha256[..Math.Min(2, sha256.Length)];
        var relativeKey = $"{prefix}/{sha256}";
        var destinationPath = Path.Combine(repoRoot, prefix, sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (!File.Exists(destinationPath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return relativeKey.Replace('\\', '/');
    }

    private static (string ObjectKey, string ArchiveSha256, string Mode) CopyContentObjectWithCompression(
        string sourcePath, string repoRoot, string sha256, long fileSize)
    {
        if (fileSize > 65536)
        {
            var compressedBytes = CompressGzip(sourcePath);
            var archiveSha256 = ComputeSha256FromBytes(compressedBytes);
            var archiveKey = CopyBytesToObjectStore(compressedBytes, repoRoot, archiveSha256);
            return (archiveKey, archiveSha256, "compressed-object");
        }

        var key = CopyContentObject(sourcePath, repoRoot, sha256);
        return (key, string.Empty, "file-object");
    }

    private static byte[] CompressGzip(string filePath)
    {
        using var input = File.OpenRead(filePath);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            input.CopyTo(gzip);
        }
        return output.ToArray();
    }

    private static string ComputeSha256FromBytes(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string CopyBytesToObjectStore(byte[] data, string repoRoot, string sha256)
    {
        var prefix = sha256[..Math.Min(2, sha256.Length)];
        var relativeKey = $"{prefix}/{sha256}";
        var destinationPath = Path.Combine(repoRoot, prefix, sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (!File.Exists(destinationPath))
        {
            File.WriteAllBytes(destinationPath, data);
        }
        return relativeKey.Replace('\\', '/');
    }

    private static void WriteBundle(string fileMapPath, string fileMapJson, string signatureBase64)
    {
        var bundle = new BundleDocument(fileMapJson, signatureBase64);
        WriteJson(fileMapPath + ".bundle.json", bundle);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private sealed record FileFingerprint(string RelativePath, string FullPath, string Sha256, long Size);

    private sealed record FileMapDocument(
        string FormatVersion,
        string DistributionId,
        string FromVersion,
        string ToVersion,
        string Version,
        string Platform,
        string Arch,
        string Channel,
        DateTimeOffset PublishedAt,
        DateTimeOffset GeneratedAt,
        string? BaselineVersion,
        IReadOnlyList<string> Capabilities,
        IReadOnlyList<ComponentDocument> Components,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record DistributionDocument(
        string DistributionId,
        string Version,
        string Channel,
        string Platform,
        string Arch,
        DateTimeOffset PublishedAt,
        string? FileMapUrl,
        string? FileMapSignatureUrl,
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

    private sealed record ComponentDocument(
        string Id,
        string Root,
        string Mode,
        IReadOnlyList<FileEntryDocument> Files,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record FileEntryDocument(
        string Path,
        string Action,
        string Sha256,
        long Size,
        string Mode,
        string? ObjectKey,
        string? ObjectUrl,
        string? ArchiveSha256,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record InstallerMirrorDocument(
        string Platform,
        string Arch,
        string? Url,
        string? Name,
        string? FileName,
        string? Sha256,
        long Size);

    private sealed record BundleDocument(string Manifest, string Signature);
}
