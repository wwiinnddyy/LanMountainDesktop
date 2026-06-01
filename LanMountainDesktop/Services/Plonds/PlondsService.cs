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
        return FindAndPrepareLatestAsync(new Version(0, 0, 0), cancellationToken);
    }

    public async Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        var latest = await FindLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        if (!latest.Success)
        {
            return PlondsPrepareResult.FailedForUi(latest.ErrorMessage ?? "No usable PLONDS manifest was found.");
        }

        if (!latest.IsUpdateAvailable)
        {
            return PlondsPrepareResult.FailedForUi("No newer PLONDS version was found.");
        }

        var errors = new List<string>();
        foreach (var selected in latest.Candidates)
        {
            var result = await downloadPlanner.PrepareAsync(selected, cancellationToken).ConfigureAwait(false);
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
