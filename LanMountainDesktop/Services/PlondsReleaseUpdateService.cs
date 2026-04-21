using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
    private const int MaxTransientRetryAttempts = 3;

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
        var latestVersionText = "-";

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: null,
                PreferredAsset: null,
                ErrorMessage: "PLONDS endpoint is not configured.",
                ForceMode: isForce);
        }

        try
        {
            var apiBasePath = ResolveApiBasePath();
            var metadataUrl = BuildApiUrl(endpoint, apiBasePath, "metadata");
            var channelId = ResolveChannelId(includePrerelease);
            var platform = ResolvePlatform();
            var latestUrl = BuildApiUrl(
                endpoint,
                apiBasePath,
                $"channels/{Uri.EscapeDataString(channelId)}/{Uri.EscapeDataString(platform)}/latest?currentVersion={Uri.EscapeDataString(normalizedCurrentVersionText)}");

            _ = await GetJsonNodeWithRetryAsync(metadataUrl, PlondsCheckStage.Metadata, cancellationToken).ConfigureAwait(false);

            var latestDescriptor = await GetLatestDescriptorAsync(
                latestUrl,
                allowNoUpdateResponse: true,
                cancellationToken).ConfigureAwait(false);

            if (latestDescriptor is null)
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

            latestVersionText = latestDescriptor.VersionText;
            var hasUpdate = latestDescriptor.Version > normalizedCurrentVersion;
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

            var distribution = await ResolveDistributionAsync(
                endpoint,
                apiBasePath,
                latestUrl,
                latestDescriptor,
                channelId,
                platform,
                cancellationToken).ConfigureAwait(false);

            latestVersionText = distribution.Latest.VersionText;

            var publishedAt = ParsePublishedAt(distribution.DistributionNode) ?? DateTimeOffset.UtcNow;
            var release = new GitHubReleaseInfo(
                TagName: $"v{distribution.Latest.VersionText}",
                Name: $"PLONDS Distribution {distribution.Latest.VersionText}",
                IsPrerelease: includePrerelease,
                IsDraft: false,
                PublishedAt: publishedAt,
                Assets: distribution.Assets);

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: true,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: distribution.Latest.VersionText,
                Release: release,
                PreferredAsset: SelectPreferredInstallerAsset(distribution.Assets),
                ErrorMessage: null,
                ForceMode: isForce,
                PlondsPayload: distribution.Payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlondsRequestException ex)
        {
            AppLogger.Warn(
                "PLONDS",
                $"PLONDS {GetStageName(ex.Stage)} stage failed. {ex.Message}",
                ex);

            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: null,
                PreferredAsset: null,
                ErrorMessage: $"PLONDS {GetStageName(ex.Stage)} failed: {ex.Message}",
                ForceMode: isForce);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PLONDS", "PLONDS request failed with an unexpected error.", ex);

            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersionText: normalizedCurrentVersionText,
                LatestVersionText: latestVersionText,
                Release: null,
                PreferredAsset: null,
                ErrorMessage: $"PLONDS request failed: {ex.Message}",
                ForceMode: isForce);
        }
    }

    private async Task<LatestDescriptor?> GetLatestDescriptorAsync(
        string latestUrl,
        bool allowNoUpdateResponse,
        CancellationToken cancellationToken)
    {
        try
        {
            var latestNode = await GetJsonNodeWithRetryAsync(
                latestUrl,
                PlondsCheckStage.Latest,
                cancellationToken).ConfigureAwait(false);

            return ParseLatestDescriptor(latestNode);
        }
        catch (PlondsRequestException ex)
            when (allowNoUpdateResponse &&
                  ex.Stage == PlondsCheckStage.Latest &&
                  ex.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }
    }

    private async Task<DistributionDescriptor> ResolveDistributionAsync(
        string endpoint,
        string apiBasePath,
        string latestUrl,
        LatestDescriptor latest,
        string channelId,
        string platform,
        CancellationToken cancellationToken)
    {
        var currentLatest = latest;
        var hasRefreshedLatest = false;

        while (true)
        {
            var distributionUrl = BuildApiUrl(
                endpoint,
                apiBasePath,
                $"distributions/{Uri.EscapeDataString(currentLatest.DistributionId)}");

            try
            {
                var distributionNode = await GetJsonNodeWithRetryAsync(
                    distributionUrl,
                    PlondsCheckStage.Distribution,
                    cancellationToken).ConfigureAwait(false);

                if (TryCreateDistributionDescriptor(
                        distributionNode,
                        currentLatest,
                        channelId,
                        platform,
                        out var descriptor,
                        out var descriptorError))
                {
                    return descriptor;
                }

                if (hasRefreshedLatest || descriptorError is null || !IsRecoverableDistributionError(descriptorError))
                {
                    throw descriptorError ?? new PlondsRequestException(
                        PlondsCheckStage.PayloadParse,
                        "PLONDS distribution payload is incomplete.");
                }

                AppLogger.Warn(
                    "PLONDS",
                    $"PLONDS distribution '{currentLatest.DistributionId}' is incomplete. Refreshing latest pointer once before failing.");
            }
            catch (PlondsRequestException ex) when (!hasRefreshedLatest && IsRecoverableDistributionError(ex))
            {
                AppLogger.Warn(
                    "PLONDS",
                    $"PLONDS distribution fetch for '{currentLatest.DistributionId}' failed during {GetStageName(ex.Stage)}. Refreshing latest pointer once. Details: {ex.Message}");
            }

            hasRefreshedLatest = true;
            currentLatest = await GetLatestDescriptorAsync(
                latestUrl,
                allowNoUpdateResponse: false,
                cancellationToken).ConfigureAwait(false)
                ?? throw new PlondsRequestException(
                    PlondsCheckStage.Latest,
                    "PLONDS latest pointer disappeared while recovering the distribution payload.");
        }
    }

    private async Task<JsonElement> GetJsonNodeWithRetryAsync(
        string url,
        PlondsCheckStage stage,
        CancellationToken cancellationToken)
    {
        PlondsRequestException? lastError = null;

        for (var attempt = 1; attempt <= MaxTransientRetryAttempts; attempt++)
        {
            try
            {
                return await GetJsonNodeAsync(url, stage, cancellationToken).ConfigureAwait(false);
            }
            catch (PlondsRequestException ex) when (attempt < MaxTransientRetryAttempts && ex.IsTransient)
            {
                lastError = ex;
                AppLogger.Warn(
                    "PLONDS",
                    $"PLONDS {GetStageName(stage)} attempt {attempt}/{MaxTransientRetryAttempts} failed. Retrying shortly. Details: {ex.Message}");
                await Task.Delay(GetRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new PlondsRequestException(stage, "PLONDS request failed before a response was returned.");
    }

    private async Task<JsonElement> GetJsonNodeAsync(
        string url,
        PlondsCheckStage stage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = ResolveToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlondsRequestException(stage, "Request timed out.", isTransient: true, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PlondsRequestException(stage, $"Network error: {ex.Message}", isTransient: true, innerException: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                throw new PlondsRequestException(
                    stage,
                    "HTTP 204: no content.",
                    statusCode: response.StatusCode,
                    isTransient: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PlondsRequestException(
                    stage,
                    $"HTTP {(int)response.StatusCode}: {Truncate(body, 180)}",
                    statusCode: response.StatusCode,
                    isTransient: IsTransientStatusCode(response.StatusCode));
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("content", out var content))
                {
                    return content.Clone();
                }

                return root.Clone();
            }
            catch (JsonException ex)
            {
                throw new PlondsRequestException(
                    stage,
                    $"Invalid JSON response: {ex.Message}",
                    isTransient: IsLikelyIncompleteJson(body),
                    innerException: ex);
            }
        }
    }

    private static LatestDescriptor ParseLatestDescriptor(JsonElement latestNode)
    {
        var latestVersionText = ReadString(latestNode, "version") ?? "-";
        if (!TryParseVersion(latestVersionText, out var latestVersion) || latestVersion is null)
        {
            throw new PlondsRequestException(
                PlondsCheckStage.Latest,
                $"PLONDS latest distribution version is invalid: '{latestVersionText}'.");
        }

        var distributionId = ReadString(latestNode, "distributionId");
        if (string.IsNullOrWhiteSpace(distributionId))
        {
            throw new PlondsRequestException(
                PlondsCheckStage.Latest,
                "PLONDS latest distribution id is missing.");
        }

        return new LatestDescriptor(distributionId, latestVersionText, latestVersion);
    }

    private static bool TryCreateDistributionDescriptor(
        JsonElement distributionNode,
        LatestDescriptor latest,
        string channelId,
        string platform,
        out DistributionDescriptor descriptor,
        out PlondsRequestException? error)
    {
        descriptor = default!;
        error = null;

        var assets = ResolveInstallerAssets(distributionNode);
        var payload = ResolvePlondsPayload(
            distributionNode,
            latest.DistributionId,
            channelId,
            platform);

        if (assets.Count == 0 && !HasPlondsPayload(payload))
        {
            error = new PlondsRequestException(
                PlondsCheckStage.PayloadParse,
                "PLONDS distribution response does not expose downloadable update assets.");
            return false;
        }

        descriptor = new DistributionDescriptor(latest, distributionNode, assets, payload);
        return true;
    }

    private static bool IsRecoverableDistributionError(PlondsRequestException error)
    {
        if (error.Stage == PlondsCheckStage.PayloadParse)
        {
            return true;
        }

        return error.Stage == PlondsCheckStage.Distribution &&
               (error.StatusCode == HttpStatusCode.NotFound ||
                error.StatusCode == HttpStatusCode.RequestTimeout ||
                error.StatusCode == HttpStatusCode.TooManyRequests ||
                error.StatusCode is >= HttpStatusCode.InternalServerError);
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

                var name = ReadString(installerNode, "name")
                           ?? ReadString(installerNode, "fileName");
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

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests ||
               statusCode >= HttpStatusCode.InternalServerError;
    }

    private static bool IsLikelyIncompleteJson(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return true;
        }

        var trimmed = body.TrimEnd();
        if (trimmed.Length == 0)
        {
            return true;
        }

        var last = trimmed[^1];
        return last != '}' && last != ']';
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(350),
            2 => TimeSpan.FromMilliseconds(900),
            _ => TimeSpan.FromMilliseconds(1500)
        };
    }

    private static string GetStageName(PlondsCheckStage stage)
    {
        return stage switch
        {
            PlondsCheckStage.Metadata => "metadata",
            PlondsCheckStage.Latest => "latest",
            PlondsCheckStage.Distribution => "distribution",
            PlondsCheckStage.PayloadParse => "payload-parse",
            _ => "unknown"
        };
    }

    private enum PlondsCheckStage
    {
        Metadata,
        Latest,
        Distribution,
        PayloadParse
    }

    private sealed record LatestDescriptor(
        string DistributionId,
        string VersionText,
        Version Version);

    private sealed record DistributionDescriptor(
        LatestDescriptor Latest,
        JsonElement DistributionNode,
        IReadOnlyList<GitHubReleaseAsset> Assets,
        PlondsUpdatePayload Payload);

    private sealed class PlondsRequestException : Exception
    {
        public PlondsRequestException(
            PlondsCheckStage stage,
            string message,
            HttpStatusCode? statusCode = null,
            bool isTransient = false,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Stage = stage;
            StatusCode = statusCode;
            IsTransient = isTransient;
        }

        public PlondsCheckStage Stage { get; }

        public HttpStatusCode? StatusCode { get; }

        public bool IsTransient { get; }
    }
}
