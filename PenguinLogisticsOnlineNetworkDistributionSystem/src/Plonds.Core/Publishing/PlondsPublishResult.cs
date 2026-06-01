namespace Plonds.Core.Publishing;

public sealed record PlondsPublishResult(
    string ReleaseTag,
    string Version,
    string VersionPrefix,
    string ManifestKey,
    string ManifestUrl,
    string ChangedZipKey,
    string ChangedZipUrl,
    string ChangedFolderKey,
    string ChangedFolderUrl,
    string FilesZipKey,
    string FilesZipUrl,
    string FilesFolderKey,
    string FilesFolderUrl,
    int ChangedFileCount,
    int FilesFileCount);
