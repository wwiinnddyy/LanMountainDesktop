using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<PlondsPublishResult> PublishAsync(PlondsPublishOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var releaseTag = Require(options.ReleaseTag, nameof(options.ReleaseTag));
        var repository = Require(options.Repository, nameof(options.Repository));
        var manifestPath = Path.GetFullPath(Require(options.ManifestPath, nameof(options.ManifestPath)));
        var changedZipPath = Path.GetFullPath(Require(options.ChangedZipPath, nameof(options.ChangedZipPath)));
        var workDir = Path.GetFullPath(Require(options.WorkDir, nameof(options.WorkDir)));
        var version = releaseTag.TrimStart('v', 'V');
        var prefix = NormalizePrefix(options.S3KeyPrefix);
        var versionPrefix = $"{prefix}/{version}";
        var changedFolderName = $"{version}-changed";
        var changedExtractRoot = Path.Combine(workDir, changedFolderName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("PLONDS manifest not found.", manifestPath);
        }

        if (!File.Exists(changedZipPath))
        {
            throw new FileNotFoundException("PLONDS changed.zip not found.", changedZipPath);
        }

        var manifest = LoadManifest(manifestPath);
        PayloadUtilities.EnsureCleanDirectory(changedExtractRoot);
        ZipFile.ExtractToDirectory(changedZipPath, changedExtractRoot, overwriteFiles: true);

        var manifestKey = $"{versionPrefix}/PLONDS.json";
        var changedZipKey = $"{versionPrefix}/changed.zip";
        var changedFolderKey = $"{versionPrefix}/{changedFolderName}";

        using var s3 = new PlondsS3Client(options.S3);

        var changedFileCount = 0;
        foreach (var filePath in Directory.EnumerateFiles(changedExtractRoot, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = PayloadUtilities.NormalizeRelativePath(Path.GetRelativePath(changedExtractRoot, filePath));
            var objectKey = $"{changedFolderKey}/{relativePath}";
            await s3.UploadFileAsync(new PlondsS3ObjectUpload(filePath, objectKey, ResolveContentType(filePath)), cancellationToken).ConfigureAwait(false);
            changedFileCount++;
        }

        await s3.UploadFileAsync(new PlondsS3ObjectUpload(changedZipPath, changedZipKey, "application/zip"), cancellationToken).ConfigureAwait(false);

        var updatedManifest = manifest with
        {
            Downloads = new PlondsDownloadInfo(
                ReleaseTag: releaseTag,
                GitHub: new PlondsGitHubDownloadInfo(
                    ReleaseUrl: $"https://github.com/{repository}/releases/tag/{releaseTag}",
                    ManifestUrl: $"https://github.com/{repository}/releases/download/{releaseTag}/PLONDS.json",
                    ChangedZipUrl: $"https://github.com/{repository}/releases/download/{releaseTag}/changed.zip"),
                S3: new PlondsS3DownloadInfo(
                    Bucket: options.S3.Bucket,
                    Prefix: versionPrefix,
                    ManifestKey: manifestKey,
                    ManifestUrl: s3.BuildPublicUrl(manifestKey),
                    ChangedZipKey: changedZipKey,
                    ChangedZipUrl: s3.BuildPublicUrl(changedZipKey),
                    ChangedFolderKey: changedFolderKey,
                    ChangedFolderUrl: s3.BuildPublicUrl(changedFolderKey)))
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(updatedManifest, JsonOptions), new UTF8Encoding(false));
        await s3.UploadFileAsync(new PlondsS3ObjectUpload(manifestPath, manifestKey, "application/json"), cancellationToken).ConfigureAwait(false);

        await s3.EnsureObjectExistsAsync(manifestKey, cancellationToken).ConfigureAwait(false);
        await s3.EnsureObjectExistsAsync(changedZipKey, cancellationToken).ConfigureAwait(false);

        return new PlondsPublishResult(
            ReleaseTag: releaseTag,
            Version: version,
            VersionPrefix: versionPrefix,
            ManifestKey: manifestKey,
            ManifestUrl: s3.BuildPublicUrl(manifestKey),
            ChangedZipKey: changedZipKey,
            ChangedZipUrl: s3.BuildPublicUrl(changedZipKey),
            ChangedFolderKey: changedFolderKey,
            ChangedFolderUrl: s3.BuildPublicUrl(changedFolderKey),
            ChangedFileCount: changedFileCount);
    }

    private static PlondsManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<PlondsManifest>(json, JsonOptions)
               ?? throw new InvalidOperationException("PLONDS manifest is empty or invalid.");
    }

    private static string NormalizePrefix(string value)
    {
        var normalized = Require(value, nameof(value)).Replace('\\', '/').Trim('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid S3 key prefix: {value}", nameof(value));
        }

        return normalized;
    }

    private static string ResolveContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".zip" => "application/zip",
            ".dll" => "application/octet-stream",
            ".exe" => "application/octet-stream",
            ".pdb" => "application/octet-stream",
            ".deps" => "application/json",
            ".runtimeconfig" => "application/json",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private static string Require(string value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Trim();
    }
}
