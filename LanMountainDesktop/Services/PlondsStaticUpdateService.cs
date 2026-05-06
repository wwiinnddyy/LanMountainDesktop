using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services;

internal sealed class PlondsStaticUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;

    public PlondsStaticUpdateService(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl ?? ResolveConfiguredBaseUrl());
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
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

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static string ResolveCurrentPlatform()
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

    private async Task<UpdateCheckResult> CheckForUpdatesCoreAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce,
        CancellationToken cancellationToken)
    {
        var currentVersionText = FormatVersion(currentVersion);
        var channel = includePrerelease ? UpdateSettingsValues.ChannelPreview : UpdateSettingsValues.ChannelStable;
        var platform = ResolveCurrentPlatform();

        try
        {
            var latestUrl = BuildUrl($"meta/channels/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(platform)}/latest.json");
            var latest = await GetJsonAsync<LatestPointerDto>(latestUrl, cancellationToken);
            if (latest is null || string.IsNullOrWhiteSpace(latest.DistributionId))
            {
                return Failed(currentVersionText, isForce, $"PLONDS static latest manifest is unavailable at {latestUrl}.");
            }

            var distributionUrl = BuildUrl($"meta/distributions/{Uri.EscapeDataString(latest.DistributionId)}.json");
            var distribution = await GetJsonAsync<DistributionDto>(distributionUrl, cancellationToken);
            if (distribution is null)
            {
                return Failed(currentVersionText, isForce, $"PLONDS static distribution manifest is unavailable at {distributionUrl}.");
            }

            var latestVersionText = FirstNonEmpty(distribution.Version, latest.Version) ?? "-";
            var isNewer = TryParseVersion(latestVersionText, out var latestVersion) && latestVersion > currentVersion;
            var isUpdateAvailable = isForce || isNewer;
            var payload = isUpdateAvailable
                ? CreatePayload(distribution, latest, channel, platform)
                : null;

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: isUpdateAvailable,
                CurrentVersionText: currentVersionText,
                LatestVersionText: latestVersionText,
                Release: null,
                PreferredAsset: null,
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
            return Failed(currentVersionText, isForce, ex.Message);
        }
    }

    private PlondsUpdatePayload CreatePayload(
        DistributionDto distribution,
        LatestPointerDto latest,
        string channel,
        string platform)
    {
        var distributionId = FirstNonEmpty(distribution.DistributionId, latest.DistributionId) ?? string.Empty;
        var fileMapUrl = FirstNonEmpty(distribution.FileMapUrl, BuildUrl($"manifests/{Uri.EscapeDataString(distributionId)}/plonds-filemap.json"));
        var signatureUrl = FirstNonEmpty(distribution.FileMapSignatureUrl, fileMapUrl + ".sig");

        return new PlondsUpdatePayload(
            DistributionId: distributionId,
            ChannelId: FirstNonEmpty(distribution.Channel, latest.Channel, channel) ?? channel,
            SubChannel: FirstNonEmpty(distribution.Platform, latest.Platform, platform) ?? platform,
            FileMapJson: null,
            FileMapSignature: null,
            FileMapJsonUrl: fileMapUrl,
            FileMapSignatureUrl: signatureUrl);
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} from {url}: {Truncate(body, 256)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static UpdateCheckResult Failed(string currentVersionText, bool isForce, string message)
    {
        return new UpdateCheckResult(
            Success: false,
            IsUpdateAvailable: false,
            CurrentVersionText: currentVersionText,
            LatestVersionText: "-",
            Release: null,
            PreferredAsset: null,
            ErrorMessage: message,
            ForceMode: isForce);
    }

    private string BuildUrl(string relativePath)
    {
        return $"{_baseUrl}/{relativePath.TrimStart('/')}";
    }

    private static string ResolveConfiguredBaseUrl()
    {
        var environmentValue = Environment.GetEnvironmentVariable(UpdateSettingsValues.PlondsStaticBaseUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(environmentValue)
            ? UpdateSettingsValues.DefaultPlondsStaticBaseUrl
            : environmentValue;
    }

    private static string NormalizeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UpdateSettingsValues.DefaultPlondsStaticBaseUrl;
        }

        return value.Trim().TrimEnd('/');
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Version.TryParse(value.Trim().TrimStart('v', 'V'), out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string FormatVersion(Version version)
    {
        if (version.Revision >= 0)
        {
            return version.ToString();
        }

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record LatestPointerDto(
        string? DistributionId,
        string? Version,
        string? Channel,
        string? Platform,
        DateTimeOffset PublishedAt);

    private sealed record DistributionDto(
        string? DistributionId,
        string? Version,
        string? SourceVersion,
        string? Channel,
        string? Platform,
        DateTimeOffset PublishedAt,
        string? FileMapUrl,
        string? FileMapSignatureUrl);
}
