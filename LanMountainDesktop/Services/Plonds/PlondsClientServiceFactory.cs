namespace LanMountainDesktop.Services.Plonds;

internal static class PlondsClientServiceFactory
{
    private const string S3ManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_S3_MANIFEST_URL";
    private const string GitHubManifestUrlEnvironmentVariable = "LANMOUNTAIN_PLONDS_GITHUB_MANIFEST_URL";
    private const string DefaultS3ManifestUrl = "https://cn-nb1.rains3.com/lmdesktop/plonds/PLONDS.json";
    private const string DefaultGitHubManifestUrl = "https://github.com/wwiinnddyy/LanMountainDesktop/releases/latest/download/PLONDS.json";

    public static IPlondsService CreateDefault(HttpClient? httpClient = null)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var dataRoot = Path.Combine(AppDataPathProvider.GetDataRoot(), "PLONDS");
        var sourceStore = new PlondsSourceStore(Path.Combine(dataRoot, "sources.json"));
        var registry = new PlondsSourceRegistry(CreateBuiltInSources());
        foreach (var source in sourceStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            registry.Add(source);
        }

        var packageStore = new PlondsPackageStore(Path.Combine(dataRoot, "packages"));
        return new PlondsService(
            registry,
            new PlondsManifestClient(client),
            new PlondsDownloadPlanner(new PlondsHttpPackageDownloader(client, packageStore, new PlondsVerifier())),
            sourceStore);
    }

    internal static IReadOnlyList<PlondsSourceDescriptor> CreateBuiltInSources()
    {
        return
        [
            new(
                Id: "s3",
                Kind: "s3",
                ManifestUrl: ResolveManifestUrl(S3ManifestUrlEnvironmentVariable, DefaultS3ManifestUrl),
                Priority: 100),
            new(
                Id: "github",
                Kind: "github",
                ManifestUrl: ResolveManifestUrl(GitHubManifestUrlEnvironmentVariable, DefaultGitHubManifestUrl),
                Priority: 50)
        ];
    }

    private static string ResolveManifestUrl(string environmentVariable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
