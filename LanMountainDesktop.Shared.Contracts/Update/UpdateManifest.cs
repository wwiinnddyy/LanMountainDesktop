namespace LanMountainDesktop.Shared.Contracts.Update;

public sealed record UpdateManifest(
    string DistributionId,
    string FromVersion,
    string ToVersion,
    string Platform,
    string Channel,
    DateTimeOffset PublishedAt,
    UpdatePayloadKind Kind,
    string? FileMapUrl,
    string? FileMapSignatureUrl,
    string? FileMapSha256,
    IReadOnlyList<UpdateFileEntry> Files,
    IReadOnlyList<UpdateMirrorAsset>? InstallerMirrors,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsDelta => Kind is UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy;

    public long EstimatedDeltaBytes
    {
        get
        {
            long total = 0;
            foreach (var f in Files)
            {
                if (f.Action is not ("reuse" or "delete"))
                {
                    total += f.Size;
                }
            }
            return total;
        }
    }
}

public sealed record UpdateFileEntry(
    string Path,
    string Action,
    string Sha256,
    long Size,
    string Mode,
    string? ObjectKey,
    string? ObjectUrl,
    string? ArchiveSha256,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record UpdateMirrorAsset(
    string Platform,
    string? Url,
    string? Name,
    string? Sha256,
    long Size);

public sealed record UpdateSettingsState(
    string UpdateChannel,
    string UpdateMode,
    string UpdateDownloadSource,
    int UpdateDownloadThreads,
    string? PreferredDistributionId,
    string? LastAppliedVersion,
    DateTimeOffset? LastAppliedAt,
    int ConsecutiveFailCount,
    DateTimeOffset? LastFailureAt,
    string? PendingUpdateInstallerPath,
    string? PendingUpdateVersion,
    long? PendingUpdatePublishedAtUtcMs,
    long? LastUpdateCheckUtcMs,
    string? PendingUpdateSha256)
{
    public static UpdateSettingsState Default => new(
        UpdateChannel: "stable",
        UpdateMode: "download_then_confirm",
        UpdateDownloadSource: "plonds-api",
        UpdateDownloadThreads: 4,
        PreferredDistributionId: null,
        LastAppliedVersion: null,
        LastAppliedAt: null,
        ConsecutiveFailCount: 0,
        LastFailureAt: null,
        PendingUpdateInstallerPath: null,
        PendingUpdateVersion: null,
        PendingUpdatePublishedAtUtcMs: null,
        LastUpdateCheckUtcMs: null,
        PendingUpdateSha256: null);
}
