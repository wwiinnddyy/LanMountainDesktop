using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

/// <summary>
/// Thin PLONDS client used by the host app.
/// The host keeps responsibility for checking and downloading updates; Launcher only applies staged payloads.
/// </summary>
public sealed class PlondsReleaseUpdateService : IDisposable
{
    private const string DefaultApiBasePath = "/api/plonds/v1";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public PlondsReleaseUpdateService(HttpClient? httpClient = null)
    {
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
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: false, cancellationToken);
    }

    public Task<UpdateCheckResult> ForceCheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: true, cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckForUpdatesCoreAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce,
        CancellationToken cancellationToken)
    {
        var normalizedCurrentVersion = NormalizeVersion(currentVersion);
        var normalizedCurrentVersionText = FormatVersionText(normalizedCurrentVersion);
        var endpoint = ResolveEndpoint();

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: "-",
                Release: null,
                PreferredAsset: null,
                ErrorMessage: "PLONDS endpoint is not configured.",
                ForceMode: isForce);
        }

        try
        {
            var apiBasePath = ResolveApiBasePath();
            var metadataUrl = BuildApiUrl(endpoint, apiBasePath, "metadata");
            var metadata = await GetJsonNodeAsync(metadataUrl, cancellationToken).ConfigureAwait(false);

            var channelId = ResolveChannelId(includePrerelease);
            var platform = ResolvePlatform();
            var latestUrl = BuildApiUrl(
                endpoint,
                apiBasePath,
                $"channels/{Uri.EscapeDataString(channelId)}/{Uri.EscapeDataString(platform)}/latest?currentVersion={Uri.EscapeDataString(normalizedCurrentVersionText)}");

            JsonElement latestNode;
            try
            {
                latestNode = await GetJsonNodeAsync(latestUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("HTTP 204", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult(
                    Success: true,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: normalizedCurrentVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: null,
                    ForceMode: isForce);
            }

            var latestVersionText = ReadString(latestNode, "version") ?? "-";
            if (!TryParseVersion(latestVersionText, out var latestVersion) || latestVersion is null)
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: latestVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "PLONDS latest distribution version is invalid.",
                    ForceMode: isForce);
            }

            var distributionId = ReadString(latestNode, "distributionId");
            if (string.IsNullOrWhiteSpace(distributionId))
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: latestVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "PLONDS latest distribution id is missing.",
                    ForceMode: isForce);
            }

            var hasUpdate = latestVersion > normalizedCurrentVersion;
            if (!isForce && !hasUpdate)
            {
                return new UpdateCheckResult(
                    Success: true,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: latestVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: null,
                    ForceMode: false);
            }

            var distributionUrl = BuildApiUrl(
                endpoint,
                apiBasePath,
                $"distributions/{Uri.EscapeDataString(distributionId)}");
            var distributionNode = await GetJsonNodeAsync(distributionUrl, cancellationToken).ConfigureAwait(false);

            var assets = ResolveInstallerAssets(distributionNode);
            var payload = ResolvePlondsPayload(distributionNode, distributionId, channelId, platform);
            if (assets.Count == 0 && !HasPlondsPayload(payload))
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: latestVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "PLONDS distribution response does not expose downloadable update assets.",
                    ForceMode: isForce);
            }

            var publishedAt = ParsePublishedAt(distributionNode) ?? DateTimeOffset.UtcNow;
            var release = new GitHubReleaseInfo(
                TagName: $"v{latestVersionText}",
                Name: $"PLONDS Distribution {latestVersionText}",
                IsPrerelease: includePrerelease,
                IsDraft: false,
                PublishedAt: publishedAt,
                Assets: assets);

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: true,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: release,
                PreferredAsset: SelectPreferredInstallerAsset(assets),
                ErrorMessage: null,
                ForceMode: isForce,
                PlondsPayload: payload);
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
                ErrorMessage: $"PLONDS request failed: {ex.Message}",
                ForceMode: isForce);
        }
    }

    private async Task<JsonElement> GetJsonNodeAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = ResolveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Truncate(body, 180)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("content", out var content))
        {
            return content.Clone();
        }

        return root.Clone();
    }

    private static IReadOnlyList<GitHubReleaseAsset> ResolveInstallerAssets(JsonElement distributionNode)
    {
        var assets = new List<GitHubReleaseAsset>();

        if (TryGetPropertyIgnoreCase(distributionNode, "installerMirrors", out var installersNode) &&
            installersNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var installerNode in installersNode.EnumerateArray())
            {
                if (installerNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(installerNode, "name");
                var url = ReadString(installerNode, "url") ?? ReadString(installerNode, "downloadUrl");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var size = ReadInt64(installerNode, "size") ?? 0L;
                var sha256 = ReadString(installerNode, "sha256");
                assets.Add(new GitHubReleaseAsset(name, url, size, sha256));
            }
        }

        if (assets.Count > 0)
        {
            return assets;
        }

        if (TryGetPropertyIgnoreCase(distributionNode, "assets", out var assetsNode) &&
            assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetNode in assetsNode.EnumerateArray())
            {
                if (assetNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(assetNode, "name");
                var url = ReadString(assetNode, "url")
                          ?? ReadString(assetNode, "downloadUrl")
                          ?? ReadString(assetNode, "browserDownloadUrl");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var size = ReadInt64(assetNode, "size") ?? 0L;
                var sha256 = ReadString(assetNode, "sha256");
                assets.Add(new GitHubReleaseAsset(name, url, size, sha256));
            }
        }

        return assets;
    }

    private static PlondsUpdatePayload ResolvePlondsPayload(
        JsonElement distributionNode,
        string distributionId,
        string channelId,
        string platform)
    {
        var fileMapJson = ReadString(distributionNode, "fileMapJson");
        var fileMapSignature = ReadString(distributionNode, "fileMapSignature");
        var fileMapJsonUrl = ReadString(distributionNode, "fileMapJsonUrl")
                             ?? ReadString(distributionNode, "fileMapUrl")
                             ?? ReadString(distributionNode, "manifestUrl");
        var fileMapSignatureUrl = ReadString(distributionNode, "fileMapSignatureUrl")
                                  ?? ReadString(distributionNode, "signatureUrl");

        return new PlondsUpdatePayload(
            DistributionId: distributionId,
            ChannelId: channelId,
            SubChannel: platform,
            FileMapJson: fileMapJson,
            FileMapSignature: fileMapSignature,
            FileMapJsonUrl: fileMapJsonUrl,
            FileMapSignatureUrl: fileMapSignatureUrl);
    }

    private static bool HasPlondsPayload(PlondsUpdatePayload payload)
    {
        return !string.IsNullOrWhiteSpace(payload.FileMapJson)
               || !string.IsNullOrWhiteSpace(payload.FileMapJsonUrl);
    }

    private static GitHubReleaseAsset? SelectPreferredInstallerAsset(IReadOnlyList<GitHubReleaseAsset> assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var archToken = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64"
            };

            return assets
                .Select(asset => (Asset: asset, Score: ScoreInstallerAsset(asset.Name, ".exe", ".msi", archToken)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        if (OperatingSystem.IsLinux())
        {
            return assets
                .Select(asset => (Asset: asset, Score: ScoreInstallerAsset(asset.Name, ".deb", ".rpm", "x64")))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        if (OperatingSystem.IsMacOS())
        {
            var archToken = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return assets
                .Select(asset => (Asset: asset, Score: ScoreInstallerAsset(asset.Name, ".dmg", ".pkg", archToken)))
                .OrderByDescending(x => x.Score)
                .FirstOrDefault(x => x.Score > 0)
                .Asset;
        }

        return null;
    }

    private static int ScoreInstallerAsset(string name, string ext1, string ext2, string archToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        var score = 0;
        if (name.EndsWith(ext1, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }
        else if (name.EndsWith(ext2, StringComparison.OrdinalIgnoreCase))
        {
            score += 160;
        }
        else
        {
            return 0;
        }

        if (name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("installer", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (name.Contains(archToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (name.Contains("portable", StringComparison.OrdinalIgnoreCase))
        {
            score -= 30;
        }

        return score;
    }

    private static string ResolveChannelId(bool includePrerelease)
    {
        return includePrerelease
            ? UpdateSettingsValues.ChannelPreview
            : UpdateSettingsValues.ChannelStable;
    }

    private static string ResolvePlatform()
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

    private static string? ResolveEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("LANMOUNTAIN_PLONDS_ENDPOINT")
                      ?? Environment.GetEnvironmentVariable("PLONDS_ENDPOINT");
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim().TrimEnd('/');
    }

    private static string? ResolveToken()
    {
        var token = Environment.GetEnvironmentVariable("LANMOUNTAIN_PLONDS_TOKEN")
                    ?? Environment.GetEnvironmentVariable("PLONDS_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static string ResolveApiBasePath()
    {
        var configured = Environment.GetEnvironmentVariable("LANMOUNTAIN_PLONDS_API_BASE_PATH")
                         ?? Environment.GetEnvironmentVariable("PLONDS_API_BASE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultApiBasePath;
        }

        var normalized = configured.Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
    }

    private static string BuildApiUrl(string endpoint, string apiBasePath, string relativePath)
    {
        return $"{endpoint.TrimEnd('/')}/{apiBasePath.Trim('/').TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    private static string? ReadString(JsonElement node, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : value.ToString();
    }

    private static long? ReadInt64(JsonElement node, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        if (value.TryGetInt64(out var number))
        {
            return number;
        }

        var text = value.ToString();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParsePublishedAt(JsonElement node)
    {
        var text = ReadString(node, "publishedAt");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
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
}
