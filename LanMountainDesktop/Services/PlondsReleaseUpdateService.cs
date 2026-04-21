using System;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

/// <summary>
/// Release-backed PLONDS checker.
/// It only succeeds when the latest GitHub Release already exposes platform PLONDS assets.
/// If those assets are not ready yet, callers can fall back to the normal GitHub installer flow.
/// </summary>
public sealed class PlondsReleaseUpdateService : IDisposable
{
    private readonly GitHubReleaseUpdateService _githubReleaseUpdateService = new("wwiinnddyy", "LanMountainDesktop");

    public Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: false, cancellationToken);
    }

    public Task<UpdateCheckResult> ForceCheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: true, cancellationToken);
    }

    public void Dispose()
    {
        _githubReleaseUpdateService.Dispose();
    }

    private async Task<UpdateCheckResult> CheckForUpdatesCoreAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce,
        CancellationToken cancellationToken)
    {
        var releaseResult = isForce
            ? await _githubReleaseUpdateService.ForceCheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken)
            : await _githubReleaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);

        if (!releaseResult.Success)
        {
            return releaseResult;
        }

        if (!isForce && !releaseResult.IsUpdateAvailable)
        {
            return releaseResult with { ForceMode = false };
        }

        if (releaseResult.PlondsPayload is not null)
        {
            return releaseResult with { ForceMode = isForce };
        }

        var latestVersion = string.IsNullOrWhiteSpace(releaseResult.LatestVersionText)
            ? "-"
            : releaseResult.LatestVersionText;
        var message = releaseResult.Release is null
            ? "GitHub Release data is unavailable for PLONDS."
            : $"Release {latestVersion} does not expose platform PLONDS assets yet.";

        return new UpdateCheckResult(
            Success: false,
            IsUpdateAvailable: releaseResult.IsUpdateAvailable,
            CurrentVersionText: releaseResult.CurrentVersionText,
            LatestVersionText: latestVersion,
            Release: releaseResult.Release,
            PreferredAsset: releaseResult.PreferredAsset,
            ErrorMessage: message,
            ForceMode: isForce,
            PlondsPayload: null);
    }
}
