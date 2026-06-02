using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class InstallerPlondsClient(HttpClient httpClient, string stagingRoot)
{
    private const string S3ManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_S3_MANIFEST_URL";
    private const string GitHubManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_GITHUB_MANIFEST_URL";
    private const string DefaultS3ManifestUrl = "https://cn-nb1.rains3.com/lmdesktop/lanmountain/update/plonds/PLONDS.json";
    private const string DefaultGitHubManifestUrl = "https://github.com/wwiinnddyy/LanMountainDesktop/releases/latest/download/PLONDS.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<InstallerPlondsSource> CreateBuiltInSources()
    {
        return
        [
            new("s3", "s3", ResolveManifestUrl(S3ManifestUrlEnvironmentVariable, DefaultS3ManifestUrl), 100),
            new("github", "github", ResolveManifestUrl(GitHubManifestUrlEnvironmentVariable, DefaultGitHubManifestUrl), 50)
        ];
    }

    public async Task<InstallerPlondsCandidate> FindLatestAsync(CancellationToken cancellationToken)
    {
        var sources = CreateBuiltInSources().ToList();
        var candidates = new List<InstallerPlondsCandidate>();

        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[index];
            InstallerPlondsManifest? manifest;
            try
            {
                manifest = await GetManifestAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (manifest is null)
            {
                continue;
            }

            AddManifestSources(sources, manifest.Sources);
            var filesUrl = InstallerPlondsUrlResolver.ResolveFilesZipUrls(manifest, source).FirstOrDefault();
            if (filesUrl is null)
            {
                continue;
            }

            candidates.Add(new InstallerPlondsCandidate(source, manifest, filesUrl));
        }

        return candidates
                   .Where(candidate => TryParseVersion(candidate.Manifest.CurrentVersion, out _))
                   .OrderByDescending(candidate => ParseVersion(candidate.Manifest.CurrentVersion))
                   .ThenByDescending(candidate => candidate.Source.Priority)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException("No usable PLONDS full package source was found.");
    }

    public async Task<PreparedFilesPackage> DownloadAndPrepareFullPackageAsync(
        InstallerPlondsCandidate candidate,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        var version = ParseVersion(candidate.Manifest.CurrentVersion).ToString();
        var packageRoot = Path.Combine(stagingRoot, SanitizePathSegment(version), SanitizePathSegment(candidate.Source.Id), "full");
        var urls = new[] { candidate.FilesZipUrl }
            .Concat(InstallerPlondsUrlResolver.ResolveFilesZipUrls(candidate.Manifest, candidate.Source))
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Exception? lastError = null;

        foreach (var filesZipUrl in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, recursive: true);
            }

            Directory.CreateDirectory(packageRoot);
            var zipPath = Path.Combine(packageRoot, "Files.zip");
            var extractDirectory = Path.Combine(packageRoot, "Files");
            Directory.CreateDirectory(extractDirectory);
            var attempt = candidate with { FilesZipUrl = filesZipUrl };

            try
            {
                await DownloadToFileAsync(attempt, zipPath, progress, cancellationToken).ConfigureAwait(false);
                await VerifyPackageAsync(zipPath, attempt.Manifest, filesZipUrl, cancellationToken).ConfigureAwait(false);
                ExtractZip(zipPath, extractDirectory);

                progress?.Report(new InstallerDeployProgress(
                    "Files package prepared",
                    version,
                    1,
                    0.10,
                    "Files.zip",
                    new FileInfo(zipPath).Length,
                    new FileInfo(zipPath).Length));

                return new PreparedFilesPackage(version, candidate.Source.Id, zipPath, extractDirectory, candidate.Manifest);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Failed to download and prepare the PLONDS Files package.", lastError);
    }

    public static long EstimateInstallBytes(InstallerPlondsManifest manifest)
    {
        var filesBytes = manifest.FilesMap?.Values.Sum(file => Math.Max(0, file.Size)) ?? 0;
        var packageBytes = FindChecksumSizeHint(manifest.Checksums);
        return Math.Max(filesBytes, packageBytes);
    }

    private async Task<InstallerPlondsManifest?> GetManifestAsync(
        InstallerPlondsSource source,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(source.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, InstallerJsonContext.Default.InstallerPlondsManifest, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DownloadToFileAsync(
        InstallerPlondsCandidate candidate,
        string destinationPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(candidate.FilesZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var partialPath = $"{destinationPath}.partial";
        long downloaded = 0;
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partialPath))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    var fraction = totalBytes is > 0 ? Math.Clamp((double)downloaded / totalBytes.Value, 0, 1) : 0;
                    progress?.Report(new InstallerDeployProgress(
                        "Downloading Files.zip",
                        candidate.Manifest.CurrentVersion,
                        fraction,
                        0,
                        "Files.zip",
                        downloaded,
                        totalBytes));
                }
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static async Task VerifyPackageAsync(
        string zipPath,
        InstallerPlondsManifest manifest,
        Uri filesZipUrl,
        CancellationToken cancellationToken)
    {
        var checksum = FindChecksum(manifest.Checksums, GetChecksumKeys(filesZipUrl));
        if (checksum is null)
        {
            throw new InvalidDataException("PLONDS manifest does not declare a checksum for Files.zip.");
        }

        var (algorithm, expectedHash) = ParseChecksum(checksum);
        var actualHash = await ComputeHashAsync(zipPath, algorithm, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"PLONDS Files.zip checksum mismatch. Expected {algorithm}:{expectedHash}, actual {algorithm}:{actualHash}.");
        }
    }

    private static void ExtractZip(string zipPath, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var normalizedName = InstallerPathGuard.NormalizeRelativePath(entry.FullName);
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName));
            InstallerPathGuard.EnsureChildPath(destinationDirectory, destinationPath);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void AddManifestSources(List<InstallerPlondsSource> sources, IEnumerable<InstallerPlondsSource>? manifestSources)
    {
        if (manifestSources is null)
        {
            return;
        }

        foreach (var source in manifestSources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.ManifestUrl))
            {
                continue;
            }

            if (sources.Any(existing => string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(existing.ManifestUrl, source.ManifestUrl, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            sources.Add(source with
            {
                Id = source.Id.Trim(),
                Kind = string.IsNullOrWhiteSpace(source.Kind) ? "http" : source.Kind.Trim(),
                ManifestUrl = source.ManifestUrl.Trim()
            });
        }
    }

    private static IReadOnlyList<string> GetChecksumKeys(Uri url)
    {
        var urlFileName = Path.GetFileName(url.LocalPath);
        return new[] { "Files.zip", "files.zip", "files-windows-x64.zip", urlFileName }
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindChecksum(IReadOnlyDictionary<string, string>? checksums, IEnumerable<string> keys)
    {
        if (checksums is null || checksums.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (checksums.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var match = checksums.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static (string Algorithm, string Hash) ParseChecksum(string checksum)
    {
        var normalized = checksum.Trim();
        var separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var algorithm = normalized[..separatorIndex].Trim().ToLowerInvariant();
            var hash = NormalizeHash(normalized[(separatorIndex + 1)..]);
            if (algorithm is "md5" or "sha256" && hash.Length > 0)
            {
                return (algorithm, hash);
            }
        }

        var inferred = NormalizeHash(normalized);
        return inferred.Length switch
        {
            32 => ("md5", inferred),
            64 => ("sha256", inferred),
            _ => throw new InvalidDataException($"Unsupported PLONDS checksum format: {checksum}")
        };
    }

    private static async Task<string> ComputeHashAsync(string filePath, string algorithm, CancellationToken cancellationToken)
    {
        using HashAlgorithm hasher = algorithm switch
        {
            "md5" => MD5.Create(),
            "sha256" => SHA256.Create(),
            _ => throw new InvalidDataException($"Unsupported PLONDS checksum algorithm: {algorithm}")
        };
        await using var stream = File.OpenRead(filePath);
        var hash = await hasher.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long FindChecksumSizeHint(IReadOnlyDictionary<string, string>? checksums)
    {
        _ = checksums;
        return 0;
    }

    private static Version ParseVersion(string version)
    {
        var normalized = version.Trim().TrimStart('v', 'V');
        return Version.Parse(normalized);
    }

    private static bool TryParseVersion(string version, out Version parsed)
    {
        return Version.TryParse(version.Trim().TrimStart('v', 'V'), out parsed!);
    }

    private static string NormalizeHash(string value)
    {
        return value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string ResolveManifestUrl(string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
