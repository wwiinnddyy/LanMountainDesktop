namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsDownloadPlanner(IPlondsPackageDownloader downloader)
{
    public async Task<PlondsPrepareResult> PrepareAsync(
        PlondsManifestCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Manifest.RequiresCleanInstall)
        {
            return PlondsPrepareResult.FailedForUi(
                "PLONDS manifest requires a clean install. Use the Host Update installer flow instead.");
        }

        try
        {
            var deltaPackage = await downloader
                .PrepareDeltaAsync(candidate.Manifest, candidate.Source, cancellationToken)
                .ConfigureAwait(false);

            return PlondsPrepareResult.Prepared(deltaPackage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception deltaError)
        {
            try
            {
                var fullPackage = await downloader
                    .PrepareFullAsync(candidate.Manifest, candidate.Source, cancellationToken)
                    .ConfigureAwait(false);

                return PlondsPrepareResult.Prepared(fullPackage);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception fullError)
            {
                return PlondsPrepareResult.FailedForUi(
                    $"PLONDS delta package failed and full package fallback also failed. Delta: {deltaError.Message}; Full: {fullError.Message}");
            }
        }
    }
}
