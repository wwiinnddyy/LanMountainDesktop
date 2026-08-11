using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class GithubReleaseManifestProvider : IUpdateManifestProvider, IDisposable
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

        return UpdateManifestMapper.FromGitHubRelease(result.Release, channel, platform);
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

        return UpdateManifestMapper.FromGitHubRelease(release, channel, platform);
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

    public void Dispose()
    {
        if (_ownsService)
        {
            _githubService.Dispose();
        }
    }
}
