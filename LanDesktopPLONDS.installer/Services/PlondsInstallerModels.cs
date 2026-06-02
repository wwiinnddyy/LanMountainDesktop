namespace LanDesktopPLONDS.Installer.Services;

internal sealed record InstallerPlondsSource(
    string Id,
    string Kind,
    string ManifestUrl,
    int Priority = 0);

internal sealed record InstallerPlondsManifest(
    string FormatVersion,
    string CurrentVersion,
    string PreviousVersion,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    string Channel,
    string Platform,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, InstallerPlondsFileEntry> FilesMap,
    IReadOnlyDictionary<string, InstallerPlondsChangedFileEntry> ChangedFilesMap,
    IReadOnlyDictionary<string, string> Checksums,
    InstallerPlondsDownloads? Downloads,
    IReadOnlyList<InstallerPlondsSource>? Sources);

internal sealed record InstallerPlondsFileEntry(
    string Action,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");

internal sealed record InstallerPlondsChangedFileEntry(
    string ArchivePath,
    string Hash,
    long Size,
    string HashAlgorithm = "sha256");

internal sealed record InstallerPlondsDownloads(
    InstallerPlondsGitHubDownloads? GitHub,
    InstallerPlondsS3Downloads? S3);

internal sealed record InstallerPlondsGitHubDownloads(
    string? ReleaseUrl,
    string? ManifestUrl,
    string? ChangedZipUrl,
    string? FilesZipUrl);

internal sealed record InstallerPlondsS3Downloads(
    string? Bucket,
    string? Prefix,
    string? ManifestKey,
    string? ManifestUrl,
    string? ChangedZipKey,
    string? ChangedZipUrl,
    string? ChangedFolderKey,
    string? ChangedFolderUrl,
    string? FilesZipKey,
    string? FilesZipUrl,
    string? FilesFolderKey,
    string? FilesFolderUrl);

public sealed record OnlineInstallPackageInfo(
    string Version,
    string SourceId,
    Uri FilesZipUrl,
    long EstimatedBytes);

public sealed record OnlineInstallOptions(bool CreateDesktopShortcut)
{
    public static OnlineInstallOptions Default { get; } = new(CreateDesktopShortcut: false);
}

internal sealed record InstallerPlondsCandidate(
    InstallerPlondsSource Source,
    InstallerPlondsManifest Manifest,
    Uri FilesZipUrl);

internal sealed record PreparedFilesPackage(
    string Version,
    string SourceId,
    string ZipPath,
    string ExtractDirectory,
    InstallerPlondsManifest Manifest);
