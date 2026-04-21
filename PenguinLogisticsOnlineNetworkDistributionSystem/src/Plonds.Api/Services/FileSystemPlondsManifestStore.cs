using System.Text.Json;
using Plonds.Api.Configuration;
using Plonds.Shared;
using Plonds.Shared.Models;

namespace Plonds.Api.Services;

public sealed class FileSystemPlondsManifestStore : IPlondsManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly PlondsApiOptions _options;
    private readonly string _storageRootFullPath;
    private readonly string _metaRootFullPath;

    public FileSystemPlondsManifestStore(PlondsApiOptions options)
    {
        _options = options;
        _storageRootFullPath = ResolveRootPath(options.StorageRoot);
        _metaRootFullPath = Path.Combine(_storageRootFullPath, options.MetaRoot);
    }

    public Task<PlondsMetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var channelsRoot = Path.Combine(_metaRootFullPath, "channels");
        var latest = new List<PlondsChannelPointer>();
        if (Directory.Exists(channelsRoot))
        {
            foreach (var latestPath in Directory.EnumerateFiles(channelsRoot, "latest.json", SearchOption.AllDirectories))
            {
                var pointer = ReadLatestPointer(latestPath);
                if (pointer is not null)
                {
                    latest.Add(pointer);
                }
            }
        }

        var catalog = new PlondsMetadataCatalog(
            ProtocolName: PlondsConstants.ProtocolName,
            ProtocolVersion: PlondsConstants.ProtocolVersion,
            StorageRoot: _storageRootFullPath,
            MetaRoot: _metaRootFullPath,
            Latest: latest.OrderBy(x => x.Channel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Platform, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Metadata: new Dictionary<string, string>
            {
                ["apiBasePath"] = PlondsConstants.DefaultApiBasePath
            });

        return Task.FromResult(catalog);
    }

    public Task<PlondsChannelPointer?> GetLatestAsync(string channel, string platform, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ReadLatestPointer(GetLatestPath(channel, platform)));
    }

    public Task<PlondsDistributionInfo?> GetDistributionAsync(string distributionId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var path = GetDistributionPath(distributionId);
        if (!File.Exists(path))
        {
            return Task.FromResult<PlondsDistributionInfo?>(null);
        }

        var json = File.ReadAllText(path);
        var distribution = JsonSerializer.Deserialize<PlondsDistributionInfo>(json, JsonOptions);
        return Task.FromResult(distribution);
    }

    private PlondsChannelPointer? ReadLatestPointer(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        var pointer = JsonSerializer.Deserialize<PlondsChannelPointer>(json, JsonOptions);
        return pointer;
    }

    private string GetLatestPath(string channel, string platform)
    {
        return Path.Combine(_metaRootFullPath, "channels", channel, platform, "latest.json");
    }

    private string GetDistributionPath(string distributionId)
    {
        return Path.Combine(_metaRootFullPath, "distributions", $"{distributionId}.json");
    }

    private static string ResolveRootPath(string root)
    {
        if (Path.IsPathRooted(root))
        {
            return Path.GetFullPath(root);
        }

        var candidates = new List<string>();

        AddCandidateChain(candidates, Directory.GetCurrentDirectory(), root);
        AddCandidateChain(candidates, AppContext.BaseDirectory, root);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
    }

    private static void AddCandidateChain(ICollection<string> candidates, string? startDirectory, string relativeRoot)
    {
        var current = string.IsNullOrWhiteSpace(startDirectory)
            ? null
            : Path.GetFullPath(startDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(current, relativeRoot)));
            current = Directory.GetParent(current)?.FullName;
        }
    }
}
