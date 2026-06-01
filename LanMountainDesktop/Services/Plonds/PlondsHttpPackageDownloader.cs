namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsHttpPackageDownloader(
    HttpClient httpClient,
    PlondsPackageStore packageStore,
    PlondsVerifier verifier) : IPlondsPackageDownloader
{
    public Task<PlondsPreparedPackage> PrepareDeltaAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        if (manifest.IsFullUpdate || manifest.RequiresCleanInstall)
        {
            throw new InvalidOperationException("PLONDS manifest requires a full package.");
        }

        return PrepareAsync(manifest, source, PlondsPackageMode.Delta, cancellationToken);
    }

    public Task<PlondsPreparedPackage> PrepareFullAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        return PrepareAsync(manifest, source, PlondsPackageMode.Full, cancellationToken);
    }

    private async Task<PlondsPreparedPackage> PrepareAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        PlondsPackageMode mode,
        CancellationToken cancellationToken)
    {
        var urls = PlondsDownloadUrlResolver.Resolve(manifest, source, mode);
        if (urls.Count == 0)
        {
            throw new InvalidOperationException($"PLONDS manifest does not provide a {mode} package URL.");
        }

        Exception? lastError = null;
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var staging = await packageStore.CreateStagingAsync(manifest, source, mode, cancellationToken).ConfigureAwait(false);
            try
            {
                await DownloadToFileAsync(url, staging.PackageZipPath, cancellationToken).ConfigureAwait(false);
                await verifier.VerifyFileAsync(
                    staging.PackageZipPath,
                    manifest.Checksums,
                    GetChecksumKeys(mode, url),
                    cancellationToken).ConfigureAwait(false);

                packageStore.ExtractPackage(staging.PackageZipPath, staging.ExtractDirectory);
                return staging.ToPreparedPackage();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"Failed to prepare PLONDS {mode} package.", lastError);
    }

    private async Task DownloadToFileAsync(Uri url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"PLONDS package download failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partialPath = $"{destinationPath}.partial";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partialPath))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static IReadOnlyList<string> GetChecksumKeys(PlondsPackageMode mode, Uri url)
    {
        var urlFileName = Path.GetFileName(url.LocalPath);
        var keys = mode is PlondsPackageMode.Delta
            ? new[] { "changed.zip", urlFileName }
            : new[] { "Files.zip", "files.zip", "files-windows-x64.zip", urlFileName };

        return keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
