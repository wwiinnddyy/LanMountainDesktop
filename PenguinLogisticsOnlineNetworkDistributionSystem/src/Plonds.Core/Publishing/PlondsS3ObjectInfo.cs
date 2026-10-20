namespace Plonds.Core.Publishing;

public sealed record PlondsS3ObjectInfo(
    string Key,
    long? ContentLength,
    string? ETag,
    string? ContentSha256 = null);
