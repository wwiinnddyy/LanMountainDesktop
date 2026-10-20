using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Plonds;

internal static class PlondsClientServiceFactory
{
    private const string S3ManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_S3_MANIFEST_URL";
    private const string GitHubManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_GITHUB_MANIFEST_URL";

    public static IPlondsService CreateDefault(HttpClient? httpClient = null, string channel = UpdateSettingsValues.ChannelStable)
    {
        var client = httpClient ?? PlondsHttpClientFactory.Create();
        var dataRoot = Path.Combine(AppDataPathProvider.GetDataRoot(), "PLONDS");
        var sourceStore = new PlondsSourceStore(Path.Combine(dataRoot, "sources.json"));
        var registry = new PlondsSourceRegistry(CreateBuiltInSources(channel));
        foreach (var source in sourceStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            if (source.Id is not ("s3" or "github")) registry.Add(source);
        }

        var packageStore = new PlondsPackageStore(Path.Combine(dataRoot, "packages"));
        return new PlondsService(
            registry,
            new PlondsManifestClient(client, PlatformIdentifiers.CurrentPlatformId, channel),
            new PlondsDownloadPlanner(new PlondsHttpPackageDownloader(client, packageStore, new PlondsVerifier(), new ResumableDownloadService(client))),
            sourceStore, channel: channel, lifetime: httpClient is null ? client : null);
    }

    /// <summary>
    /// Built-in sources. S3/CDN is the primary 智慧更新 endpoint; GitHub is fallback only.
    /// </summary>
    internal static IReadOnlyList<PlondsSourceDescriptor> CreateBuiltInSources(string channel = UpdateSettingsValues.ChannelStable)
    {
        var baseUrl = PlondsManifestValues.ResolveManifestUrl(
            UpdateSettingsValues.PlondsStaticBaseUrlEnvironmentVariable,
            UpdateSettingsValues.DefaultPlondsStaticBaseUrl).TrimEnd('/');

        var s3Manifest = PlondsManifestValues.ResolveManifestUrl(
            S3ManifestUrlEnvironmentVariable,
            $"{baseUrl}/plonds/{PlatformIdentifiers.CurrentPlatformId}/{UpdateSettingsValues.NormalizeChannel(channel)}/PLONDS.json");

        var githubManifest = PlondsManifestValues.ResolveManifestUrl(
            GitHubManifestUrlEnvironmentVariable,
            "https://api.github.com/repos/wwiinnddyy/LanMountainDesktop/releases?per_page=50");

        return
        [
            new(
                Id: "s3",
                Kind: "s3",
                ManifestUrl: s3Manifest,
                Priority: 100),
            new(
                Id: "github",
                Kind: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubManifestUrlEnvironmentVariable)) ? "github-api" : "github",
                ManifestUrl: githubManifest,
                Priority: 50)
        ];
    }
}
