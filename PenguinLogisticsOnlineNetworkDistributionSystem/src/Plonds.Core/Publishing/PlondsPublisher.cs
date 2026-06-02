using System.IO.Compression;
using System.Security.Cryptography;
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
        var filesZipPath = Path.GetFullPath(Require(options.FilesZipPath, nameof(options.FilesZipPath)));
        var workDir = Path.GetFullPath(Require(options.WorkDir, nameof(options.WorkDir)));
        var version = releaseTag.TrimStart('v', 'V');
        var prefix = NormalizePrefix(options.S3KeyPrefix);
        var versionPrefix = $"{prefix}/{version}";
        var changedFolderName = $"{version}-changed";
        var filesFolderName = $"{version}-Files";
        var changedExtractRoot = Path.Combine(workDir, changedFolderName);
        var filesExtractRoot = Path.Combine(workDir, filesFolderName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("PLONDS manifest not found.", manifestPath);
        }

        if (!File.Exists(changedZipPath))
        {
            throw new FileNotFoundException("PLONDS changed.zip not found.", changedZipPath);
        }

        if (!File.Exists(filesZipPath))
        {
            throw new FileNotFoundException("PLONDS files zip not found.", filesZipPath);
        }

        var manifest = LoadManifest(manifestPath);
        PayloadUtilities.EnsureCleanDirectory(changedExtractRoot);
        ZipFile.ExtractToDirectory(changedZipPath, changedExtractRoot, overwriteFiles: true);
        PayloadUtilities.EnsureCleanDirectory(filesExtractRoot);
        ZipFile.ExtractToDirectory(filesZipPath, filesExtractRoot, overwriteFiles: true);

        var manifestKey = $"{versionPrefix}/PLONDS.json";
        var latestManifestKey = $"{prefix}/PLONDS.json";
        var changedZipKey = $"{versionPrefix}/changed.zip";
        var changedFolderKey = $"{versionPrefix}/{changedFolderName}";
        var filesZipKey = $"{versionPrefix}/Files.zip";
        var filesFolderKey = $"{versionPrefix}/{filesFolderName}";

        using var s3 = new PlondsS3Client(options.S3);

        await UploadArtifactAsync(s3, changedZipPath, changedZipKey, "application/zip", cancellationToken).ConfigureAwait(false);
        await UploadArtifactAsync(s3, filesZipPath, filesZipKey, "application/zip", cancellationToken).ConfigureAwait(false);

        var directoryConcurrency = Math.Max(1, options.DirectoryUploadConcurrency);
        var changedFileCount = await UploadDirectoryAsync(s3, changedExtractRoot, changedFolderKey, directoryConcurrency, cancellationToken).ConfigureAwait(false);
        var filesFileCount = await UploadDirectoryAsync(s3, filesExtractRoot, filesFolderKey, directoryConcurrency, cancellationToken).ConfigureAwait(false);

        var updatedChecksums = new Dictionary<string, string>(manifest.Checksums, StringComparer.OrdinalIgnoreCase)
        {
            ["changed.zip"] = NormalizeChecksum(manifest.Checksums, "changed.zip", changedZipPath),
            ["Files.zip"] = $"md5:{ComputeMd5Hex(filesZipPath)}"
        };

        var updatedManifest = manifest with
        {
            Checksums = updatedChecksums,
            Downloads = new PlondsDownloadInfo(
                ReleaseTag: releaseTag,
                GitHub: new PlondsGitHubDownloadInfo(
                    ReleaseUrl: $"https://github.com/{repository}/releases/tag/{releaseTag}",
                    ManifestUrl: $"https://github.com/{repository}/releases/download/{releaseTag}/PLONDS.json",
                    ChangedZipUrl: $"https://github.com/{repository}/releases/download/{releaseTag}/changed.zip",
                    FilesZipUrl: $"https://github.com/{repository}/releases/download/{releaseTag}/{Path.GetFileName(filesZipPath)}"),
                S3: new PlondsS3DownloadInfo(
                    Bucket: options.S3.Bucket,
                    Prefix: versionPrefix,
                    ManifestKey: manifestKey,
                    ManifestUrl: s3.BuildPublicUrl(manifestKey),
                    ChangedZipKey: changedZipKey,
                    ChangedZipUrl: s3.BuildPublicUrl(changedZipKey),
                    ChangedFolderKey: changedFolderKey,
                    ChangedFolderUrl: s3.BuildPublicUrl(changedFolderKey),
                    FilesZipKey: filesZipKey,
                    FilesZipUrl: s3.BuildPublicUrl(filesZipKey),
                    FilesFolderKey: filesFolderKey,
                    FilesFolderUrl: s3.BuildPublicUrl(filesFolderKey)))
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(updatedManifest, JsonOptions), new UTF8Encoding(false));
        await s3.UploadFileAsync(new PlondsS3ObjectUpload(manifestPath, manifestKey, "application/json"), cancellationToken).ConfigureAwait(false);
        await s3.UploadFileAsync(new PlondsS3ObjectUpload(manifestPath, latestManifestKey, "application/json"), cancellationToken).ConfigureAwait(false);

        await s3.EnsureObjectExistsAsync(manifestKey, cancellationToken).ConfigureAwait(false);
        await s3.EnsureObjectExistsAsync(latestManifestKey, cancellationToken).ConfigureAwait(false);
        await s3.EnsureObjectExistsAsync(changedZipKey, cancellationToken).ConfigureAwait(false);
        await s3.EnsureObjectExistsAsync(filesZipKey, cancellationToken).ConfigureAwait(false);

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
            FilesZipKey: filesZipKey,
            FilesZipUrl: s3.BuildPublicUrl(filesZipKey),
            FilesFolderKey: filesFolderKey,
            FilesFolderUrl: s3.BuildPublicUrl(filesFolderKey),
            ChangedFileCount: changedFileCount,
            FilesFileCount: filesFileCount);
    }

    private static async Task<int> UploadDirectoryAsync(
        PlondsS3Client s3,
        string sourceDirectory,
        string destinationKeyPrefix,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                var relativePath = PayloadUtilities.NormalizeRelativePath(Path.GetRelativePath(sourceDirectory, filePath));
                return new DirectoryUploadPlan(
                    SourcePath: filePath,
                    ObjectKey: $"{destinationKeyPrefix}/{relativePath}",
                    ContentType: ResolveContentType(filePath));
            })
            .OrderBy(x => x.ObjectKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"No files found under {sourceDirectory}; skipping S3 directory upload to {destinationKeyPrefix}.");
            return 0;
        }

        Console.WriteLine($"Uploading S3 directory {destinationKeyPrefix}: {files.Length} files with concurrency {concurrency}.");

        var processed = 0;
        var uploaded = 0;
        var skipped = 0;
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellationToken
            },
            async (file, token) =>
            {
                var didUpload = await s3.UploadFileIfChangedAsync(
                    new PlondsS3ObjectUpload(file.SourcePath, file.ObjectKey, file.ContentType),
                    token).ConfigureAwait(false);

                if (didUpload)
                {
                    Interlocked.Increment(ref uploaded);
                }
                else
                {
                    Interlocked.Increment(ref skipped);
                }

                var current = Interlocked.Increment(ref processed);
                if (current == files.Length || current % 10 == 0)
                {
                    Console.WriteLine($"S3 directory progress {destinationKeyPrefix}: {current}/{files.Length} processed ({uploaded} uploaded, {skipped} skipped).");
                }
            }).ConfigureAwait(false);

        Console.WriteLine($"Finished S3 directory {destinationKeyPrefix}: {files.Length} files processed ({uploaded} uploaded, {skipped} skipped).");
        return files.Length;
    }

    private static async Task UploadArtifactAsync(
        PlondsS3Client s3,
        string sourcePath,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var didUpload = await s3.UploadFileIfChangedAsync(
            new PlondsS3ObjectUpload(sourcePath, objectKey, contentType),
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine(didUpload
            ? $"Published S3 artifact {objectKey}."
            : $"S3 artifact {objectKey} already exists with matching size.");
    }

    private static PlondsManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<PlondsManifest>(json, JsonOptions)
               ?? throw new InvalidOperationException("PLONDS manifest is empty or invalid.");
    }

    private static string NormalizeChecksum(
        IReadOnlyDictionary<string, string> checksums,
        string key,
        string filePath)
    {
        return checksums.TryGetValue(key, out var checksum) && !string.IsNullOrWhiteSpace(checksum)
            ? checksum
            : $"md5:{ComputeMd5Hex(filePath)}";
    }

    private static string ComputeMd5Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
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

    private sealed record DirectoryUploadPlan(
        string SourcePath,
        string ObjectKey,
        string ContentType);
}
