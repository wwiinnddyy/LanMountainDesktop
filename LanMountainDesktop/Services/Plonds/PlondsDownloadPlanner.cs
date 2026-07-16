namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsDownloadPlanner(IPlondsPackageDownloader downloader)
{
    public async Task<PlondsPrepareResult> PrepareAsync(
        PlondsManifestCandidate candidate,
        CancellationToken cancellationToken,
        bool forceFullPackage = false)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // Clean install / force reinstall: prefer full PLONDS package from this source (S3 first, GitHub PLONDS second).
        // If all PLONDS package sources fail, UpdateSettingsService may still fall back to a GitHub installer EXE.
        if (forceFullPackage ||
            candidate.Manifest.RequiresCleanInstall ||
            candidate.Manifest.IsFullUpdate)
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
                    $"智慧更新全量包准备失败（将尝试其他源/GitHub 回退）：{fullError.Message}");
            }
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
                    $"智慧更新增量包失败且全量回退也失败。增量：{deltaError.Message}；全量：{fullError.Message}");
            }
        }
    }
}
