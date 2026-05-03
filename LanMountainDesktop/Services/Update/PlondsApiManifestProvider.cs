using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class PlondsApiManifestProvider : IUpdateManifestProvider
{
    private const string ApiBasePath = "/api/plonds/v1";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public string ProviderName => "plonds-api";

    public PlondsApiManifestProvider(string baseUrl, HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/')),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress ??= new Uri(baseUrl.TrimEnd('/'));
            _ownsHttpClient = false;
        }

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-Updater/1.0");
        }
    }

    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct)
    {
        var pointer = await GetChannelPointerAsync(channel, platform, currentVersion, ct);
        if (pointer is null)
        {
            return null;
        }

        return await FetchDistributionManifestAsync(pointer.DistributionId, pointer.Version, channel, platform, ct);
    }

    public async Task<UpdateManifest?> GetByVersionAsync(
        string version,
        string channel,
        string platform,
        CancellationToken ct)
    {
        var distributionId = $"{channel}-{platform}-{version}";
        return await FetchDistributionManifestAsync(distributionId, version, channel, platform, ct);
    }

    public Task<IReadOnlyList<UpdateManifest>> GetIncrementalChainAsync(
        string channel,
        string platform,
        Version fromVersion,
        Version toVersion,
        CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<UpdateManifest>>([]);
    }

    private async Task<PlondsChannelPointerDto?> GetChannelPointerAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct)
    {
        var url = $"{ApiBasePath}/channels/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(platform)}/latest?currentVersion={Uri.EscapeDataString(currentVersion.ToString())}";

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            AppLogger.Warn("Update", $"PLONDS API latest endpoint returned HTTP {(int)response.StatusCode}: {Truncate(errorBody, 256)}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PlondsChannelPointerDto>(json, PlondsJsonOptions);
    }

    private async Task<UpdateManifest?> FetchDistributionManifestAsync(
        string distributionId,
        string targetVersion,
        string channel,
        string platform,
        CancellationToken ct)
    {
        var url = $"{ApiBasePath}/distributions/{Uri.EscapeDataString(distributionId)}";

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            AppLogger.Warn("Update", $"PLONDS API distribution endpoint returned HTTP {(int)response.StatusCode}: {Truncate(errorBody, 256)}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var dto = JsonSerializer.Deserialize<PlondsDistributionDto>(json, PlondsJsonOptions);
        if (dto is null)
        {
            return null;
        }

        return MapDistribution(dto, channel, platform);
    }

    private static UpdateManifest MapDistribution(PlondsDistributionDto dto, string channel, string platform)
    {
        var files = new List<UpdateFileEntry>();
        if (dto.Components is not null)
        {
            foreach (var component in dto.Components)
            {
                if (component.Files is null)
                {
                    continue;
                }

                foreach (var f in component.Files)
                {
                    files.Add(new UpdateFileEntry(
                        Path: f.Path ?? string.Empty,
                        Action: f.Op ?? "add",
                        Sha256: f.ContentHash ?? string.Empty,
                        Size: f.Size,
                        Mode: f.Mode ?? "file-object",
                        ObjectKey: f.ObjectKey,
                        ObjectUrl: null,
                        ArchiveSha256: null,
                        Metadata: null));
                }
            }
        }

        var mirrors = dto.InstallerMirrors?.Select(m => new UpdateMirrorAsset(
            Platform: m.Platform ?? platform,
            Url: m.Url,
            Name: m.FileName,
            Sha256: m.Sha256,
            Size: m.Size)).ToArray();

        var fileMapSignatureUrl = dto.Signatures?.FirstOrDefault()?.Signature;

        return new UpdateManifest(
            DistributionId: dto.DistributionId ?? string.Empty,
            FromVersion: dto.SourceVersion ?? string.Empty,
            ToVersion: dto.Version ?? string.Empty,
            Platform: platform,
            Channel: channel,
            PublishedAt: dto.PublishedAt,
            Kind: UpdatePayloadKind.DeltaPlonds,
            FileMapUrl: dto.FileMapUrl,
            FileMapSignatureUrl: fileMapSignatureUrl,
            FileMapSha256: null,
            Files: files,
            InstallerMirrors: mirrors,
            Metadata: dto.Metadata as IReadOnlyDictionary<string, string> ?? new Dictionary<string, string>());
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static readonly JsonSerializerOptions PlondsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record PlondsChannelPointerDto(
        string? Channel,
        string? Platform,
        string? DistributionId,
        string? Version,
        DateTimeOffset PublishedAt);

    private sealed record PlondsDistributionDto(
        string? DistributionId,
        string? Version,
        string? SourceVersion,
        string? Channel,
        string? Platform,
        DateTimeOffset PublishedAt,
        string? FileMapUrl,
        List<PlondsComponentDto>? Components,
        List<PlondsMirrorDto>? InstallerMirrors,
        List<PlondsSignatureDto>? Signatures,
        Dictionary<string, string>? Metadata);

    private sealed record PlondsComponentDto(
        string? Id,
        string? Root,
        string? Mode,
        List<PlondsFileDto>? Files);

    private sealed record PlondsFileDto(
        string? Path,
        string? Op,
        string? ContentHash,
        long Size,
        string? Mode,
        string? ObjectKey);

    private sealed record PlondsMirrorDto(
        string? Platform,
        string? Url,
        string? FileName,
        string? Sha256,
        long Size);

    private sealed record PlondsSignatureDto(
        string? Algorithm,
        string? KeyId,
        string? Signature);
}
