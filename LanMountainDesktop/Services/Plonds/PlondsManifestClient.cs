using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsManifestClient(HttpClient httpClient)
{
    public async Task<PlondsClientManifest?> GetManifestAsync(PlondsSourceDescriptor source, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(source.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<PlondsClientManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
