using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsSourceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _sourceFilePath;

    public PlondsSourceStore(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            throw new ArgumentException("PLONDS source cache path is required.", nameof(sourceFilePath));
        }

        _sourceFilePath = Path.GetFullPath(sourceFilePath);
    }

    public async Task<IReadOnlyList<PlondsSourceDescriptor>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_sourceFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_sourceFilePath);
        var document = await JsonSerializer.DeserializeAsync<PlondsSourceStoreDocument>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return document?.Sources ?? [];
    }

    public async Task SaveAsync(IEnumerable<PlondsSourceDescriptor> sources, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_sourceFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var normalized = new PlondsSourceRegistry(sources).Sources.ToArray();
        var document = new PlondsSourceStoreDocument(normalized);
        await using var stream = File.Create(_sourceFilePath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PlondsSourceStoreDocument(IReadOnlyList<PlondsSourceDescriptor> Sources);
}
