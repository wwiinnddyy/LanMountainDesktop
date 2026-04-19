using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 更新检查服务 - 基于 GitHub Release API
/// </summary>
internal sealed class UpdateCheckService
{
    private const string GitHubApiBase = "https://api.github.com";
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly HttpClient _httpClient;

    public UpdateCheckService(string repoOwner, string repoName)
    {
        _repoOwner = repoOwner;
        _repoName = repoName;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LanMountainDesktop-Launcher");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <summary>
    /// 检查更新
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        string currentVersion,
        UpdateChannel channel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = await FetchReleasesAsync(cancellationToken);
            
            // 根据频道过滤版本
            var filteredReleases = channel == UpdateChannel.Stable
                ? releases.Where(r => !r.Prerelease).ToList()
                : releases;

            // 找到最新版本
            var latestRelease = filteredReleases
                .OrderByDescending(r => ParseVersion(r.TagName))
                .FirstOrDefault();

            if (latestRelease == null)
            {
                return new UpdateCheckResult
                {
                    HasUpdate = false,
                    CurrentVersion = currentVersion,
                    ErrorMessage = "No releases found"
                };
            }

            var latestVersion = ParseVersionString(latestRelease.TagName);
            var current = ParseVersion(currentVersion);
            var latest = ParseVersion(latestVersion);

            return new UpdateCheckResult
            {
                HasUpdate = latest > current,
                LatestVersion = latestVersion,
                CurrentVersion = currentVersion,
                Release = latestRelease
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = currentVersion,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 获取所有 Release
    /// </summary>
    private async Task<List<ReleaseInfo>> FetchReleasesAsync(CancellationToken cancellationToken)
    {
        var url = $"{GitHubApiBase}/repos/{_repoOwner}/{_repoName}/releases";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var releases = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListGitHubRelease);

        return releases?.Select(r => new ReleaseInfo
        {
            TagName = r.TagName ?? "",
            Name = r.Name ?? "",
            Prerelease = r.Prerelease,
            PublishedAt = r.PublishedAt,
            Body = r.Body,
            Assets = r.Assets?.Select(a => new ReleaseAsset
            {
                Name = a.Name ?? "",
                BrowserDownloadUrl = a.BrowserDownloadUrl ?? "",
                Size = a.Size
            }).ToList() ?? [],
            VelopackFeedUrl = r.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, "releases.win.json", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl,
            VelopackLegacyReleasesUrl = r.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, "RELEASES", StringComparison.OrdinalIgnoreCase))?.BrowserDownloadUrl
        }).ToList() ?? [];
    }

    /// <summary>
    /// 从 tag 解析版本号 (例如: v1.0.0 -> 1.0.0)
    /// </summary>
    private static string ParseVersionString(string tag)
    {
        return tag.TrimStart('v', 'V');
    }

    /// <summary>
    /// 解析版本号
    /// </summary>
    private static Version ParseVersion(string versionString)
    {
        var cleaned = ParseVersionString(versionString);
        return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0);
    }
}

// GitHub API 响应模型
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
