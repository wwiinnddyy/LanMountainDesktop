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
/// Best-effort PDC client that maps PDC responses to the existing update result model.
/// This keeps launcher update contracts stable while allowing a gradual migration.
/// </summary>
public sealed class PdcReleaseUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public PdcReleaseUpdateService(HttpClient? httpClient = null)
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
                ErrorMessage: "PDC endpoint is not configured.",
                ForceMode: isForce);
        }

        try
        {
            var metadataUrl = BuildUri(endpoint, "api/v1/public/distributions/metadata");
            var metadata = await GetContentNodeAsync(metadataUrl, cancellationToken).ConfigureAwait(false);

            var channelId = ResolveChannelId(metadata, includePrerelease);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                channelId = includePrerelease ? "preview" : "stable";
            }

            var latestUrl = BuildUri(
                endpoint,
                $"api/v1/public/distributions/latest/{Uri.EscapeDataString(channelId)}?appVersion={Uri.EscapeDataString(normalizedCurrentVersionText)}");
            var latestNode = await GetContentNodeAsync(latestUrl, cancellationToken).ConfigureAwait(false);

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
                    ErrorMessage: "PDC latest distribution version is invalid.",
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
                    ErrorMessage: "PDC latest distribution id is missing.",
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

            var subChannel = ResolveSubChannel();
            var distributionUrl = BuildUri(
                endpoint,
                $"api/v1/public/distributions/{Uri.EscapeDataString(distributionId)}/{Uri.EscapeDataString(subChannel)}");
            var distributionNode = await GetContentNodeAsync(distributionUrl, cancellationToken).ConfigureAwait(false);

            var assets = ResolveAssets(distributionNode);
            var pdcPayload = ResolvePdcPayload(distributionNode, distributionId, channelId, subChannel);
            if (assets.Count == 0 && !HasPdcPayload(pdcPayload))
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersionText: normalizedCurrentVersionText,
                    LatestVersionText: latestVersionText,
                    Release: null,
                    PreferredAsset: null,
                    ErrorMessage: "PDC distribution response does not expose downloadable update assets.",
                    ForceMode: isForce);
            }

            var release = new GitHubReleaseInfo(
                TagName: $"v{latestVersionText}",
                Name: $"PDC Distribution {latestVersionText}",
                IsPrerelease: includePrerelease,
                IsDraft: false,
                PublishedAt: DateTimeOffset.UtcNow,
                Assets: assets);
            var preferredAsset = SelectPreferredInstallerAsset(assets);

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: true,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: release,
                PreferredAsset: preferredAsset,
                ErrorMessage: null,
                ForceMode: isForce,
                PdcPayload: pdcPayload);
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
                ErrorMessage: $"PDC request failed: {ex.Message}",
                ForceMode: isForce);
        }
    }

    private async Task<JsonElement> GetContentNodeAsync(string url, CancellationToken cancellationToken)
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

    private static IReadOnlyList<GitHubReleaseAsset> ResolveAssets(JsonElement distributionNode)
    {
        var assets = new List<GitHubReleaseAsset>();
        if (distributionNode.ValueKind != JsonValueKind.Object)
        {
            return assets;
        }

        if (distributionNode.TryGetProperty("assets", out var assetsNode) &&
            assetsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var assetNode in assetsNode.EnumerateArray())
            {
                if (assetNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(assetNode, "name");
                var url = ReadString(assetNode, "url") ??
                          ReadString(assetNode, "downloadUrl") ??
                          ReadString(assetNode, "browserDownloadUrl");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var size = ReadInt64(assetNode, "size") ?? 0L;
                var sha256 = ReadString(assetNode, "sha256");
                assets.Add(new GitHubReleaseAsset(name, url, size, sha256));
            }
        }

        if (assets.Count > 0)
        {
            return assets;
        }

        // Field-level fallback for service-side URL projection.
        var manifestUrl = ReadString(distributionNode, "manifestUrl")
                          ?? ReadString(distributionNode, "fileMapUrl");
        var signatureUrl = ReadString(distributionNode, "signatureUrl")
                           ?? ReadString(distributionNode, "fileMapSignatureUrl");
        var archiveUrl = ReadString(distributionNode, "archiveUrl")
                         ?? ReadString(distributionNode, "updateArchiveUrl")
                         ?? ReadString(distributionNode, "payloadUrl");

        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            assets.Add(new GitHubReleaseAsset("files.json", manifestUrl, 0, null));
        }

        if (!string.IsNullOrWhiteSpace(signatureUrl))
        {
            assets.Add(new GitHubReleaseAsset("files.json.sig", signatureUrl, 0, null));
        }

        if (!string.IsNullOrWhiteSpace(archiveUrl))
        {
            assets.Add(new GitHubReleaseAsset("update.zip", archiveUrl, 0, null));
        }

        return assets;
    }

    private static PdcUpdatePayload ResolvePdcPayload(
        JsonElement distributionNode,
        string distributionId,
        string channelId,
        string subChannel)
    {
        var fileMapJson = ReadString(distributionNode, "fileMapJson");
        var fileMapSignature = ReadString(distributionNode, "fileMapSignature");
        var fileMapJsonUrl = ReadString(distributionNode, "fileMapJsonUrl")
                             ?? ReadString(distributionNode, "fileMapUrl")
                             ?? ReadString(distributionNode, "manifestUrl");
        var fileMapSignatureUrl = ReadString(distributionNode, "fileMapSignatureUrl")
                                  ?? ReadString(distributionNode, "signatureUrl");
        return new PdcUpdatePayload(
            DistributionId: distributionId,
            ChannelId: channelId,
            SubChannel: subChannel,
            FileMapJson: fileMapJson,
            FileMapSignature: fileMapSignature,
            FileMapJsonUrl: fileMapJsonUrl,
            FileMapSignatureUrl: fileMapSignatureUrl);
    }

    private static bool HasPdcPayload(PdcUpdatePayload payload)
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

    private static string ResolveChannelId(JsonElement metadataNode, bool includePrerelease)
    {
        if (metadataNode.ValueKind != JsonValueKind.Object ||
            !metadataNode.TryGetProperty("channels", out var channelsNode))
        {
            return includePrerelease ? "preview" : "stable";
        }

        var defaultChannelId = ReadString(metadataNode, "defaultChannelId") ?? string.Empty;
        if (channelsNode.ValueKind != JsonValueKind.Object)
        {
            return defaultChannelId;
        }

        string? matchedPreview = null;
        string? matchedStable = null;

        foreach (var channel in channelsNode.EnumerateObject())
        {
            var name = ReadString(channel.Value, "name") ?? channel.Name;
            if (string.IsNullOrWhiteSpace(matchedPreview) &&
                (name.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("dev", StringComparison.OrdinalIgnoreCase)))
            {
                matchedPreview = channel.Name;
            }

            if (string.IsNullOrWhiteSpace(matchedStable) &&
                (name.Contains("stable", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("release", StringComparison.OrdinalIgnoreCase)))
            {
                matchedStable = channel.Name;
            }
        }

        if (includePrerelease)
        {
            return matchedPreview ?? defaultChannelId ?? "preview";
        }

        return matchedStable ?? defaultChannelId ?? "stable";
    }

    private static string ResolveSubChannel()
    {
        var configured = Environment.GetEnvironmentVariable("LANMOUNTAIN_PDC_SUBCHANNEL")
                         ?? Environment.GetEnvironmentVariable("PDC_SUBCHANNEL");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

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

        return $"{os}_{arch}_release_folderClassic";
    }

    private static string? ResolveEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("LANMOUNTAIN_PDC_ENDPOINT")
                      ?? Environment.GetEnvironmentVariable("PDC_ENDPOINT");
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim().TrimEnd('/');
    }

    private static string? ResolveToken()
    {
        var token = Environment.GetEnvironmentVariable("LANMOUNTAIN_PDC_TOKEN")
                    ?? Environment.GetEnvironmentVariable("PDC_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static string BuildUri(string endpoint, string relativePath)
    {
        return $"{endpoint.TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    private static string? ReadString(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static long? ReadInt64(JsonElement node, string propertyName)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(propertyName, out var value))
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
