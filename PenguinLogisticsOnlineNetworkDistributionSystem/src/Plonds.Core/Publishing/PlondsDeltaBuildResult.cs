namespace Plonds.Core.Publishing;

public sealed record PlondsDeltaBuildResult(
    string Platform,
    string DistributionId,
    string UpdateArchivePath,
    string FileMapPath,
    string FileMapSignaturePath,
    string SummaryPath,
    bool IsFullPayload,
    string? BaselineTag,
    string? BaselineVersion,
    string TargetVersion);
