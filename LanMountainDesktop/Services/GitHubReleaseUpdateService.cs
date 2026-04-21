using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed record GitHubReleaseAsset(
    string Name,
    string BrowserDownloadUrl,
    long SizeBytes,
    string? Sha256 = null);

public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    bool IsPrerelease,
    bool IsDraft,
    DateTimeOffset PublishedAt,
    IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed record UpdateCheckResult(
    bool Success,
    bool IsUpdateAvailable,
    string CurrentVersionText,
    string LatestVersionText,
    GitHubReleaseInfo? Release,
    GitHubReleaseAsset? PreferredAsset,
    string? ErrorMessage,
    bool ForceMode = false,
    PlondsUpdatePayload? PlondsPayload = null);

public sealed record PlondsUpdatePayload(
    string DistributionId,
    string ChannelId,
    string SubChannel,
    string? FileMapJson,
    string? FileMapSignature,
    string? FileMapJsonUrl,
    string? FileMapSignatureUrl,
    string? UpdateArchiveUrl = null,
    string? UpdateArchiveSha256 = null,
    long? UpdateArchiveSizeBytes = null);

public sealed record UpdateDownloadResult(
    bool Success,
    string? FilePath,
    string? ErrorMessage,
    bool HashVerified = false,
    string? ExpectedHash = null,
    string? ActualHash = null);

public sealed class GitHubReleaseUpdateService : IDisposable
{
    private const string GithubApiVersion = "2022-11-28";

    private readonly string _owner;
    private readonly string _repo;
    private readonly HttpClient _httpClient;
    private readonly ResumableDownloadService _downloadService;
    private readonly bool _ownsHttpClient;

    public GitHubReleaseUpdateService(
        string owner,
        string repo,
        HttpClient? httpClient = null)
    {
        _owner = owner?.Trim() ?? string.Empty;
        _repo = repo?.Trim() ?? string.Empty;

        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }

        _downloadService = new ResumableDownloadService(_httpClient);

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-Updater/1.0");
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GithubApiVersion);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var normalizedCurrentVersionText = FormatVersionText(normalizedCurrentVersion);

        if (string.IsNullOrWhiteSpace(_owner) || string.IsNullOrWhiteSpace(_repo))
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: "-",
                Release: null,
                PreferredAsset: null,
                ErrorMessage: "Repository information is not configured.");
        }

        try
        {
            var release = includePrerelease
                ? await GetLatestReleaseIncludingPrereleaseAsync(cancellationToken)
                : await GetLatestStableReleaseAsync(cancellationToken);

            if (release is null)
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: "-",
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "No release data was returned from GitHub.");
            }

            var hasParsedTagVersion = TryParseVersion(release.TagName, out var parsedTagVersion);
            var latestVersionText = hasParsedTagVersion && parsedTagVersion is not null
                ? FormatVersionText(parsedTagVersion)
                : release.TagName;

            var isUpdateAvailable = parsedTagVersion is not null && parsedTagVersion > currentVersion;
            var preferredAsset = isUpdateAvailable
                ? SelectPreferredInstallerAsset(release.Assets)
                : null;
            var plondsPayload = isUpdateAvailable
                ? TryResolvePlondsPayload(release)
                : null;

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: isUpdateAvailable,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: release,
                PreferredAsset: preferredAsset,
                ErrorMessage: null,
                PlondsPayload: plondsPayload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: "-",
                Release: null,
                PreferredAsset: null,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<UpdateCheckResult> ForceCheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var normalizedCurrentVersionText = FormatVersionText(normalizedCurrentVersion);

        if (string.IsNullOrWhiteSpace(_owner) || string.IsNullOrWhiteSpace(_repo))
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: "-",
                Release: null,
                PreferredAsset: null,
                ErrorMessage: "Repository information is not configured.",
                ForceMode: true);
        }

        try
        {
            var release = includePrerelease
                ? await GetLatestReleaseIncludingPrereleaseAsync(cancellationToken)
                : await GetLatestStableReleaseAsync(cancellationToken);

            if (release is null)
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: "-",
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "No release data was returned from GitHub.",
                    ForceMode: true);
            }

            var hasParsedTagVersion = TryParseVersion(release.TagName, out var parsedTagVersion);
            var latestVersionText = hasParsedTagVersion && parsedTagVersion is not null
                ? FormatVersionText(parsedTagVersion)
                : release.TagName;

            var preferredAsset = SelectPreferredInstallerAsset(release.Assets);
            var plondsPayload = TryResolvePlondsPayload(release);

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: true,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: release,
                PreferredAsset: preferredAsset,
                ErrorMessage: null,
                ForceMode: true,
                PlondsPayload: plondsPayload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: "-",
                Release: null,
                PreferredAsset: null,
                ErrorMessage: ex.Message,
                ForceMode: true);
        }
    }

    public async Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (asset is null)
        {
            return new UpdateDownloadResult(false, null, "Asset is null.");
        }

        if (string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return new UpdateDownloadResult(false, null, "Asset download url is empty.");
        }

        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            return new UpdateDownloadResult(false, null, "Destination file path is empty.");
        }

        var progressAdapter = progress is null
            ? null
            : new Progress<DownloadProgressInfo>(info => progress.Report(info.Progress));
        var effectiveSource = ApplyDownloadSource(asset.BrowserDownloadUrl, downloadSource);

        var result = await _downloadService.DownloadAsync(
            effectiveSource,
            destinationFilePath,
            new DownloadOptions(
                ExpectedSizeBytes: asset.SizeBytes > 0 ? asset.SizeBytes : null,
                MaxParallelSegments: UpdateSettingsValues.NormalizeDownloadThreads(maxParallelSegments)),
            progressAdapter,
            cancellationToken);

        if (!result.Success)
        {
            return new UpdateDownloadResult(false, null, result.ErrorMessage);
        }

        var filePath = result.FilePath ?? destinationFilePath;
        var (hashVerified, actualHash) = await VerifyFileHashAsync(filePath, asset.Sha256, cancellationToken);

        if (!string.IsNullOrEmpty(asset.Sha256) && !hashVerified)
        {
            return new UpdateDownloadResult(
                false,
                filePath,
                $"Hash verification failed. Expected: {asset.Sha256}, Actual: {actualHash}",
                false,
                asset.Sha256,
                actualHash);
        }

        return new UpdateDownloadResult(true, filePath, null, hashVerified, asset.Sha256, actualHash);
    }

    public async Task<UpdateDownloadResult> RedownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationFilePath))
        {
            try
            {
                File.Delete(destinationFilePath);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Update", $"Failed to delete existing file for redownload: {destinationFilePath}", ex);
            }
        }

        var partFile = destinationFilePath + ".part";
        if (File.Exists(partFile))
        {
            try
            {
                File.Delete(partFile);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Update", $"Failed to delete part file for redownload: {partFile}", ex);
            }
        }

        var packageFile = destinationFilePath + ".download";
        if (File.Exists(packageFile))
        {
            try
            {
                File.Delete(packageFile);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Update", $"Failed to delete package file for redownload: {packageFile}", ex);
            }
        }

        return await DownloadAssetAsync(asset, destinationFilePath, downloadSource, maxParallelSegments, progress, cancellationToken);
    }

    public static async Task<(bool Success, string? Hash)> VerifyFileHashAsync(
        string filePath,
        string? expectedHash,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return (false, null);
        }

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            var computedHash = await ComputeFileSha256Async(filePath, cancellationToken);
            return (true, computedHash);
        }

        var actualHash = await ComputeFileSha256Async(filePath, cancellationToken);
        var verified = string.Equals(
            expectedHash?.Trim().ToLowerInvariant(),
            actualHash?.Trim().ToLowerInvariant(),
            StringComparison.OrdinalIgnoreCase);

        return (verified, actualHash);
    }

    public static async Task<string?> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Update", $"Failed to compute SHA256 for file: {filePath}", ex);
            return null;
        }
    }

    public async Task<GitHubReleaseInfo?> GetReleaseByTagAsync(
        string tagName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var url =
            $"https://api.github.com/repos/{_owner}/{_repo}/releases/tags/{Uri.EscapeDataString(tagName.Trim())}";
        var responseText = await GetResponseTextAsync(url, cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        return ParseRelease(document.RootElement);
    }

    private async Task<GitHubReleaseInfo?> GetLatestStableReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
        var responseText = await GetResponseTextAsync(url, cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        return ParseRelease(document.RootElement);
    }

    private async Task<GitHubReleaseInfo?> GetLatestReleaseIncludingPrereleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases?per_page=20";
        var responseText = await GetResponseTextAsync(url, cancellationToken);

        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var release = ParseRelease(item);
            if (release is null || release.IsDraft)
            {
                continue;
            }

            return release;
        }

        return null;
    }

    private async Task<string> GetResponseTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub API request failed with HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
        }

        return responseText;
    }

    private static GitHubReleaseInfo? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tagName = element.TryGetProperty("tag_name", out var tagNode)
            ? tagNode.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var name = element.TryGetProperty("name", out var nameNode)
            ? nameNode.GetString()?.Trim() ?? string.Empty
            : string.Empty;

        var isPrerelease = element.TryGetProperty("prerelease", out var prereleaseNode) &&
                           prereleaseNode.ValueKind == JsonValueKind.True;

        var isDraft = element.TryGetProperty("draft", out var draftNode) &&
                      draftNode.ValueKind == JsonValueKind.True;

        var publishedAt = DateTimeOffset.MinValue;
        if (element.TryGetProperty("published_at", out var publishedAtNode) &&
            publishedAtNode.ValueKind == JsonValueKind.String)
        {
            var publishedAtText = publishedAtNode.GetString();
            if (!string.IsNullOrWhiteSpace(publishedAtText) &&
                DateTimeOffset.TryParse(
                    publishedAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsedPublishedAt))
            {
                publishedAt = parsedPublishedAt;
            }
        }

        var assets = new List<GitHubReleaseAsset>();
        if (element.TryGetProperty("assets", out var assetsNode) && assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetNode in assetsNode.EnumerateArray())
            {
                if (assetNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var assetName = assetNode.TryGetProperty("name", out var assetNameNode)
                    ? assetNameNode.GetString()?.Trim()
                    : null;
                var browserDownloadUrl = assetNode.TryGetProperty("browser_download_url", out var urlNode)
                    ? urlNode.GetString()?.Trim()
                    : null;
                var sizeBytes = assetNode.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var size)
                    ? size
                    : 0L;

                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(browserDownloadUrl))
                {
                    continue;
                }

                assets.Add(new GitHubReleaseAsset(assetName, browserDownloadUrl, sizeBytes, null));
            }
        }

        var sha256Map = BuildSha256MapFromAssets(assets, element);

        if (sha256Map.Count > 0)
        {
            assets = assets.Select(a =>
                sha256Map.TryGetValue(a.Name, out var hash)
                    ? a with { Sha256 = hash }
                    : a).ToList();
        }

        return new GitHubReleaseInfo(tagName, name, isPrerelease, isDraft, publishedAt, assets);
    }

    private static Dictionary<string, string> BuildSha256MapFromAssets(List<GitHubReleaseAsset> assets, JsonElement releaseElement)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            if (asset.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.EndsWith(".sha256sum", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = asset.Name[..asset.Name.LastIndexOf('.')];
                var targetAsset = assets.FirstOrDefault(a =>
                    a.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                    a.Name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase));

                if (targetAsset is not null && !map.ContainsKey(targetAsset.Name))
                {
                    map[targetAsset.Name] = asset.BrowserDownloadUrl;
                }
            }
        }

        if (releaseElement.TryGetProperty("body", out var bodyNode) &&
            bodyNode.ValueKind == JsonValueKind.String)
        {
            var body = bodyNode.GetString() ?? string.Empty;
            ParseSha256FromBody(body, assets, map);
        }

        return map;
    }

    private static void ParseSha256FromBody(string body, List<GitHubReleaseAsset> assets, Dictionary<string, string> map)
    {
        var lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            var parts = trimmedLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var hash = parts[0];
                var fileName = parts[1];

                if (hash.Length == 64 && IsHexString(hash))
                {
                    foreach (var asset in assets)
                    {
                        if (asset.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) ||
                            fileName.Equals("*" + asset.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!map.ContainsKey(asset.Name))
                            {
                                map[asset.Name] = hash.ToLowerInvariant();
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    private static bool IsHexString(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static GitHubReleaseAsset? SelectPreferredInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var architectureToken = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows())
        {
            return assets
                .Select(asset => (Asset: asset, Score: ScoreWindowsInstallerAsset(asset.Name, architectureToken)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        if (OperatingSystem.IsLinux())
        {
            return assets
                .Select(asset => (Asset: asset, Score: ScoreLinuxInstallerAsset(asset.Name, architectureToken)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        if (OperatingSystem.IsMacOS())
        {
            return assets
                .Select(asset => (Asset: asset, Score: ScoreMacInstallerAsset(asset.Name, architectureToken)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        return null;
    }

    private static PlondsUpdatePayload? TryResolvePlondsPayload(GitHubReleaseInfo release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        var platformSuffix = GetPlatformAssetSuffix();
        var fileMapAsset = FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.json");
        var signatureAsset = FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.json.sig")
                             ?? FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.sig");
        var archiveAsset = FindAsset(release.Assets, $"update-{platformSuffix}.zip");
        if (fileMapAsset is null || signatureAsset is null || archiveAsset is null)
        {
            return null;
        }

        var distributionId = $"plonds-{release.TagName.Trim().TrimStart('v')}-{platformSuffix}";
        var channelId = release.IsPrerelease
            ? UpdateSettingsValues.ChannelPreview
            : UpdateSettingsValues.ChannelStable;

        return new PlondsUpdatePayload(
            DistributionId: distributionId,
            ChannelId: channelId,
            SubChannel: platformSuffix,
            FileMapJson: null,
            FileMapSignature: null,
            FileMapJsonUrl: fileMapAsset.BrowserDownloadUrl,
            FileMapSignatureUrl: signatureAsset.BrowserDownloadUrl,
            UpdateArchiveUrl: archiveAsset.BrowserDownloadUrl,
            UpdateArchiveSha256: archiveAsset.Sha256,
            UpdateArchiveSizeBytes: archiveAsset.SizeBytes > 0 ? archiveAsset.SizeBytes : null);
    }

    private static GitHubReleaseAsset? FindAsset(IReadOnlyList<GitHubReleaseAsset> assets, string assetName)
    {
        return assets.FirstOrDefault(asset => string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPlatformAssetSuffix()
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "unknown";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }

    private static int ScoreWindowsInstallerAsset(string assetName, string architectureToken)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return 0;
        }

        var score = 0;

        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }
        else if (assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            score += 160;
        }
        else
        {
            return 0;
        }

        if (assetName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("installer", StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (assetName.Contains(architectureToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (assetName.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("x86", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        if (assetName.Contains("portable", StringComparison.OrdinalIgnoreCase))
        {
            score -= 40;
        }

        return score;
    }

    private static int ScoreLinuxInstallerAsset(string assetName, string architectureToken)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return 0;
        }

        var score = 0;

        if (assetName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }
        else if (assetName.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }
        else if (assetName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            score += 160;
        }
        else
        {
            return 0;
        }

        if (assetName.Contains("linux", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (assetName.Contains(architectureToken, StringComparison.OrdinalIgnoreCase) ||
            (architectureToken == "x64" && assetName.Contains("amd64", StringComparison.OrdinalIgnoreCase)))
        {
            score += 40;
        }
        else if (assetName.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("amd64", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("x86", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        return score;
    }

    private static int ScoreMacInstallerAsset(string assetName, string architectureToken)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return 0;
        }

        var score = 0;

        if (assetName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }
        else if (assetName.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }
        else
        {
            return 0;
        }

        if (assetName.Contains("mac", StringComparison.OrdinalIgnoreCase) ||
            assetName.Contains("osx", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (assetName.Contains(architectureToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (assetName.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                 assetName.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        return score;
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var separatorIndex = normalized.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static Version NormalizeVersion(Version version)
    {
        var major = Math.Max(0, version.Major);
        var minor = Math.Max(0, version.Minor);
        var build = Math.Max(0, version.Build >= 0 ? version.Build : 0);
        var revision = Math.Max(0, version.Revision >= 0 ? version.Revision : 0);
        return revision > 0
            ? new Version(major, minor, build, revision)
            : new Version(major, minor, build);
    }

    private static string FormatVersionText(Version version)
    {
        return version.Revision > 0
            ? version.ToString(4)
            : version.ToString(3);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string ApplyDownloadSource(string browserDownloadUrl, string? downloadSource)
    {
        if (!string.Equals(
                UpdateSettingsValues.NormalizeDownloadSource(downloadSource),
                UpdateSettingsValues.DownloadSourceGhProxy,
                StringComparison.OrdinalIgnoreCase))
        {
            return browserDownloadUrl;
        }

        var normalizedBase = UpdateSettingsValues.DefaultGhProxyBaseUrl.TrimEnd('/') + "/";
        if (browserDownloadUrl.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
        {
            return browserDownloadUrl;
        }

        return normalizedBase + browserDownloadUrl;
    }
}
