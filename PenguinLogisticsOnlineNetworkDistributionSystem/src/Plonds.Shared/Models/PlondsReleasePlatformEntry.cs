namespace Plonds.Shared.Models;

public sealed record PlondsReleasePlatformEntry(
    string Platform,
    string DistributionId,
    string? BaselineTag,
    string? BaselineVersion,
    string TargetVersion,
    bool IsFullPayload,
    string FilesZipAsset,
    string UpdateZipAsset,
    string FileMapAsset,
    string FileMapSignatureAsset,
    string Sha256);
