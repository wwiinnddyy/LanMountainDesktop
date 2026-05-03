using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class GithubReleaseManifestProvider : IUpdateManifestProvider
{
    private readonly GitHubReleaseUpdateService _githubService;
    private readonly bool _ownsService;

    public string ProviderName => "github-release";

    public GithubReleaseManifestProvider(string owner, string repo, GitHubReleaseUpdateService? githubService = null)
    {
        if (githubService is null)
        {
            _githubService = new GitHubReleaseUpdateService(owner, repo);
            _ownsService = true;
        }
        else
        {
            _githubService = githubService;
            _ownsService = false;
        }
    }

    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct)
    {
        var includePrerelease = string.Equals(channel, UpdateSettingsValues.ChannelPreview, StringComparison.OrdinalIgnoreCase);

        var result = await _githubService.CheckForUpdatesAsync(currentVersion, includePrerelease, ct);
        if (!result.Success || !result.IsUpdateAvailable || result.Release is null)
        {
            return null;
        }

        return UpdateManifestMapper.FromGitHubRelease(result.Release, result.PlondsPayload, channel, platform);
    }

    public async Task<UpdateManifest?> GetByVersionAsync(
        string version,
        string channel,
        string platform,
        CancellationToken ct)
    {
        var tag = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}";
        var release = await _githubService.GetReleaseByTagAsync(tag, ct);
        if (release is null)
        {
            return null;
        }

        var plondsPayload = TryResolvePlondsPayload(release);
        return UpdateManifestMapper.FromGitHubRelease(release, plondsPayload, channel, platform);
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

    private static PlondsUpdatePayload? TryResolvePlondsPayload(GitHubReleaseInfo release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        var platformSuffix = GetPlatformAssetSuffix();
        var fileMapAsset = FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.json");
        var signatureAsset = FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.json.sig")
                             ?? FindAsset(release.Assets, $"plonds-filemap-{platformSuffix}.sig");
        var archiveAsset = FindAsset(release.Assets, $"update-{platformSuffix}.zip");

        if (fileMapAsset is null || signatureAsset is null || archiveAsset is null)
        {
            return null;
        }

        var distributionId = $"plonds-{release.TagName.Trim().TrimStart('v')}-{platformSuffix}";
        var channelId = release.IsPrerelease
            ? UpdateSettingsValues.ChannelPreview
            : UpdateSettingsValues.ChannelStable;

        return new PlondsUpdatePayload(
            DistributionId: distributionId,
            ChannelId: channelId,
            SubChannel: platformSuffix,
            FileMapJson: null,
            FileMapSignature: null,
            FileMapJsonUrl: fileMapAsset.BrowserDownloadUrl,
            FileMapSignatureUrl: signatureAsset.BrowserDownloadUrl,
            UpdateArchiveUrl: archiveAsset.BrowserDownloadUrl,
            UpdateArchiveSha256: archiveAsset.Sha256,
            UpdateArchiveSizeBytes: archiveAsset.SizeBytes > 0 ? archiveAsset.SizeBytes : null);
    }

    private static GitHubReleaseAsset? FindAsset(IReadOnlyList<GitHubReleaseAsset> assets, string assetName)
    {
        return assets.FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPlatformAssetSuffix()
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "unknown";

        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }
}
