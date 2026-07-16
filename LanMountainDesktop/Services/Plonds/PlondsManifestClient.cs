using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsManifestClient(HttpClient httpClient)
{
    public async Task<PlondsClientManifest?> GetManifestAsync(PlondsSourceDescriptor source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.ManifestUrl);
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Warn(
                "PLONDS.Manifest",
                $"Manifest fetch failed from '{source.Id}': {(int)response.StatusCode} {response.ReasonPhrase} ({source.ManifestUrl})");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<PlondsClientManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.CurrentVersion))
        {
            AppLogger.Warn("PLONDS.Manifest", $"Manifest from '{source.Id}' is empty or missing CurrentVersion.");
            return null;
        }

        return manifest;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
