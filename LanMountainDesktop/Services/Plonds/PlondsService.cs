namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsService(
    PlondsSourceRegistry sourceRegistry,
    PlondsManifestClient manifestClient,
    PlondsDownloadPlanner downloadPlanner,
    PlondsSourceStore? sourceStore = null) : IPlondsService
{
    public async Task<PlondsLatestResult> FindLatestAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        var selectedCandidates = await DiscoverHighestVersionCandidatesAsync(cancellationToken).ConfigureAwait(false);
        if (selectedCandidates.Count == 0)
        {
            return PlondsLatestResult.Failed(currentVersion, "No usable PLONDS manifest was found.");
        }

        var selected = selectedCandidates[0];
        if (!PlondsManifestSelector.TryParseVersion(selected.Manifest.CurrentVersion, out var latestVersion))
        {
            return PlondsLatestResult.Failed(currentVersion, $"Invalid PLONDS version: {selected.Manifest.CurrentVersion}");
        }

        return latestVersion.CompareTo(currentVersion) > 0
            ? PlondsLatestResult.Available(currentVersion, latestVersion, selectedCandidates)
            : PlondsLatestResult.UpToDate(currentVersion, latestVersion);
    }

    public Task<PlondsPrepareResult> FindAndPrepareLatestAsync(CancellationToken cancellationToken)
    {
        return FindAndPrepareLatestAsync(new Version(0, 0, 0), forceFullPackage: false, cancellationToken);
    }

    public Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        return FindAndPrepareLatestAsync(currentVersion, forceFullPackage: false, cancellationToken);
    }

    public async Task<PlondsPrepareResult> FindAndPrepareLatestAsync(
        Version currentVersion,
        bool forceFullPackage,
        CancellationToken cancellationToken)
    {
        var latest = await FindLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        if (!latest.Success)
        {
            return PlondsPrepareResult.FailedForUi(latest.ErrorMessage ?? "未找到可用的智慧更新清单（S3 优先，GitHub 回退）。请检查网络或清单发布状态。");
        }

        if (!latest.IsUpdateAvailable && !forceFullPackage)
        {
            return PlondsPrepareResult.FailedForUi("当前已是最新版本。");
        }

        var candidates = latest.Candidates;
        if (candidates.Count == 0)
        {
            // force reinstall may still want to re-download current version package
            candidates = await DiscoverHighestVersionCandidatesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (candidates.Count == 0)
        {
            return PlondsPrepareResult.FailedForUi("未找到可用的智慧更新源（S3 / GitHub）。");
        }

        var errors = new List<string>();
        // Prefer S3, then other high-priority sources, then GitHub PLONDS mirrors.
        foreach (var selected in candidates.OrderByDescending(c => c.Source.Priority)
                     .ThenBy(c => string.Equals(c.Source.Kind, "s3", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(c => string.Equals(c.Source.Kind, "github", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
        {
            var result = await downloadPlanner
                .PrepareAsync(selected, cancellationToken, forceFullPackage)
                .ConfigureAwait(false);
            if (result.Success)
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add($"{selected.Source.Id}: {result.ErrorMessage}");
            }
        }

        return PlondsPrepareResult.FailedForUi(string.Join(Environment.NewLine, errors));
    }

    private async Task<IReadOnlyList<PlondsManifestCandidate>> DiscoverHighestVersionCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<PlondsManifestCandidate>();
        var sources = sourceRegistry.Sources.ToArray();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PlondsClientManifest? manifest;
            try
            {
                manifest = await manifestClient.GetManifestAsync(source, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PLONDS.Source", $"Failed to read PLONDS manifest from source '{source.Id}'.", ex);
                continue;
            }

            if (manifest is null)
            {
                continue;
            }

            var manifestSources = manifest.Sources ?? [];
            sourceRegistry.AddRange(manifestSources);
            if (manifestSources.Count > 0 && sourceStore is not null)
            {
                await sourceStore.SaveAsync(sourceRegistry.Sources, cancellationToken).ConfigureAwait(false);
            }

            candidates.Add(new PlondsManifestCandidate(source, manifest));
        }

        return PlondsManifestSelector.SelectHighestVersionCandidates(candidates);
    }
}
