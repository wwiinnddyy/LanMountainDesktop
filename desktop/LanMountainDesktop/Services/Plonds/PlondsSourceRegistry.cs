namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsSourceRegistry
{
    private readonly List<PlondsSourceDescriptor> _sources = [];

    public PlondsSourceRegistry(IEnumerable<PlondsSourceDescriptor> initialSources)
    {
        AddRange(initialSources);
    }

    public IReadOnlyList<PlondsSourceDescriptor> Sources => _sources;

    public void AddRange(IEnumerable<PlondsSourceDescriptor>? sources)
    {
        if (sources is null)
        {
            return;
        }

        foreach (var source in sources)
        {
            Add(source);
        }
    }

    public void Add(PlondsSourceDescriptor source)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.ManifestUrl))
        {
            return;
        }

        var normalized = source with
        {
            Id = source.Id.Trim(),
            Kind = string.IsNullOrWhiteSpace(source.Kind) ? "http" : source.Kind.Trim(),
            ManifestUrl = source.ManifestUrl.Trim()
        };

        var existingIndex = _sources.FindIndex(item =>
            string.Equals(item.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            _sources[existingIndex] = normalized;
            return;
        }

        if (_sources.Any(item => string.Equals(item.ManifestUrl, normalized.ManifestUrl, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _sources.Add(normalized);
    }
}
