namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsHttpPackageDownloader(
    HttpClient httpClient,
    PlondsPackageStore packageStore,
    PlondsVerifier verifier,
    ResumableDownloadService? resumableDownloader = null) : IPlondsPackageDownloader
{
    private int _downloadThreads = UpdateSettingsValues.DefaultDownloadThreads;
    private IProgress<DownloadProgressInfo>? _progress;

    public void ConfigureTransfer(int downloadThreads, IProgress<DownloadProgressInfo>? progress)
    {
        _downloadThreads = UpdateSettingsValues.NormalizeDownloadThreads(downloadThreads);
        _progress = progress;
    }

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
        foreach (var url in ExpandUrlCandidates(urls, mode))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var staging = await packageStore.CreateStagingAsync(manifest, source, mode, cancellationToken).ConfigureAwait(false);
            try
            {
                AppLogger.Info("PLONDS.Download", $"Downloading {mode} package from {url.AbsoluteUri}");
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
                AppLogger.Warn("PLONDS.Download", $"Package URL failed: {url.AbsoluteUri} -> {ex.Message}");
                lastError = ex;
                if (File.Exists(staging.PackageZipPath)) File.Delete(staging.PackageZipPath);
            }
        }

        throw new InvalidOperationException($"Failed to prepare PLONDS {mode} package from S3/CDN.", lastError);
    }

    private static IEnumerable<Uri> ExpandUrlCandidates(IReadOnlyList<Uri> urls, PlondsPackageMode mode)
    {
        foreach (var url in urls)
        {
            yield return url;
            // Case variants commonly used by different publishers.
            if (mode is PlondsPackageMode.Full &&
                url.AbsoluteUri.EndsWith(PlondsWireFormat.FullPackageFileName, StringComparison.OrdinalIgnoreCase))
            {
                var alt = url.AbsoluteUri.EndsWith(PlondsWireFormat.FullPackageFileName, StringComparison.Ordinal)
                    ? url.AbsoluteUri[..^PlondsWireFormat.FullPackageFileName.Length] + PlondsWireFormat.FullPackageFileNameLowerCase
                    : url.AbsoluteUri[..^PlondsWireFormat.FullPackageFileNameLowerCase.Length] + PlondsWireFormat.FullPackageFileName;
                if (Uri.TryCreate(alt, UriKind.Absolute, out var altUri) &&
                    !string.Equals(altUri.AbsoluteUri, url.AbsoluteUri, StringComparison.Ordinal))
                {
                    yield return altUri;
                }
            }
        }
    }

    private async Task DownloadToFileAsync(Uri url, string destinationPath, CancellationToken cancellationToken)
    {
        if (resumableDownloader is not null)
        {
            var download = await resumableDownloader.DownloadAsync(url.AbsoluteUri, destinationPath,
                new DownloadOptions(MaxParallelSegments: _downloadThreads), _progress, cancellationToken).ConfigureAwait(false);
            if (!download.Success) throw new IOException(download.ErrorMessage ?? "Package download failed.");
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"PLONDS package download failed: {(int)response.StatusCode} {response.ReasonPhrase} ({url.AbsoluteUri})");
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
                var buffer = new byte[128 * 1024];
                long transferred = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    transferred += read;
                    var total = response.Content.Headers.ContentLength;
                    _progress?.Report(new DownloadProgressInfo(transferred, total, total > 0 ? (double)transferred / total.Value : 0, false, false));
                }
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
            ? new[] { PlondsWireFormat.DeltaPackageFileName, urlFileName }
            : new[] { PlondsWireFormat.FullPackageFileName, PlondsWireFormat.FullPackageFileNameLowerCase, PlondsWireFormat.LegacyWindowsX64PackageFileName, urlFileName };

        return keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
