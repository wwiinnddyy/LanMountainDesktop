using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed record GitHubReleaseAsset(
    string Name,
    string BrowserDownloadUrl,
    long SizeBytes);

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
    string? ErrorMessage);

public sealed record UpdateDownloadResult(
    bool Success,
    string? FilePath,
    string? ErrorMessage);

public sealed class GitHubReleaseUpdateService : IDisposable
{
    private const string GithubApiVersion = "2022-11-28";

    private readonly string _owner;
    private readonly string _repo;
    private readonly HttpClient _httpClient;
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
        var normalizedCurrentVersionText = NormalizeVersion(currentVersion).ToString(3);

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
                ? parsedTagVersion.ToString(3)
                : release.TagName;

            var isUpdateAvailable = parsedTagVersion is not null && parsedTagVersion > currentVersion;
            var preferredAsset = isUpdateAvailable
                ? SelectPreferredInstallerAsset(release.Assets)
                : null;

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: isUpdateAvailable,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: release,
                PreferredAsset: preferredAsset,
                ErrorMessage: null);
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

    public async Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
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

        try
        {
            var directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var response = await _httpClient.GetAsync(
                asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateDownloadResult(
                    false,
                    null,
                    $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var contentLength = response.Content.Headers.ContentLength ??
                                (asset.SizeBytes > 0 ? asset.SizeBytes : -1);

            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destinationStream = File.Create(destinationFilePath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;

                if (contentLength > 0)
                {
                    progress?.Report(Math.Clamp(totalRead / (double)contentLength, 0d, 1d));
                }
            }

            progress?.Report(1d);

            return new UpdateDownloadResult(true, destinationFilePath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateDownloadResult(false, null, ex.Message);
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

                assets.Add(new GitHubReleaseAsset(assetName, browserDownloadUrl, sizeBytes));
            }
        }

        return new GitHubReleaseInfo(tagName, name, isPrerelease, isDraft, publishedAt, assets);
    }

    private static GitHubReleaseAsset? SelectPreferredInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        if (assets is null || assets.Count == 0 || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var architectureToken = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        var ranked = assets
            .Select(asset => (Asset: asset, Score: ScoreWindowsInstallerAsset(asset.Name, architectureToken)))
            .OrderByDescending(x => x.Score)
            .ToList();

        return ranked.FirstOrDefault(x => x.Score > 0).Asset;
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
        var build = Math.Max(0, version.Build);
        return new Version(major, minor, build);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
