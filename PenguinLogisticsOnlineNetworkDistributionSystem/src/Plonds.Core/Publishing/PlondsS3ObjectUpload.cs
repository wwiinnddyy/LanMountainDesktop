namespace Plonds.Core.Publishing;

public sealed record PlondsS3ObjectUpload(
    string SourcePath,
    string Key,
    string ContentType);
