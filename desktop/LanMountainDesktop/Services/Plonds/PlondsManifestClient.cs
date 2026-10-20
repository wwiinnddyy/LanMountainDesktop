using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsManifestClient(
    HttpClient httpClient, string? platform = null, string channel = UpdateSettingsValues.ChannelStable)
{
    public async Task<PlondsClientManifest?> GetManifestAsync(PlondsSourceDescriptor source, CancellationToken cancellationToken)
    {
        var json = await ReadJsonAsync(source.ManifestUrl, cancellationToken).ConfigureAwait(false);
        if (json is null && source.Id == "s3" && Uri.TryCreate(source.ManifestUrl, UriKind.Absolute, out var legacy))
        {
            var marker = legacy.AbsolutePath.IndexOf("/plonds/", StringComparison.Ordinal);
            if (marker >= 0)
            {
                var builder = new UriBuilder(legacy) { Path = legacy.AbsolutePath[..(marker + "/plonds/".Length)] + PlondsWireFormat.CommitDeltaManifestFileName };
                if (builder.Uri.AbsoluteUri != source.ManifestUrl)
                    json = await ReadJsonAsync(builder.Uri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            }
        }
        if (json is null)
        {
            return null;
        }
        if (source.Kind == "github-api")
        {
            var selectedPlatform = platform ?? PlatformIdentifiers.CurrentPlatformId;
            foreach (var assetUrl in LanMountainDesktop.Shared.Contracts.Deployment.ReleaseManifestLocator.SelectAssetUrls(
                json, selectedPlatform, channel == UpdateSettingsValues.ChannelPreview))
            {
                var manifestJson = await ReadJsonAsync(assetUrl, cancellationToken).ConfigureAwait(false);
                if (manifestJson is null) continue;
                var releaseManifest = JsonSerializer.Deserialize<PlondsClientManifest>(manifestJson, JsonOptions);
                if (releaseManifest is not null &&
                    PlondsManifestSelector.SelectHighestVersionCandidates([new(source, releaseManifest)], selectedPlatform, channel).Count > 0)
                    return releaseManifest;
            }
            return null;
        }

        var manifest = JsonSerializer.Deserialize<PlondsClientManifest>(json, JsonOptions);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.CurrentVersion))
        {
            AppLogger.Warn("PLONDS.Manifest", $"Manifest from '{source.Id}' is empty or missing CurrentVersion.");
            return null;
        }

        return manifest;
    }

    private async Task<string?> ReadJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"Update manifest request timed out: {url}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
