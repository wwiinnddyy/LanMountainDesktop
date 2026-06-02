namespace Plonds.Core.Publishing;

public sealed record PlondsS3ClientOptions(
    Uri Endpoint,
    string Region,
    string Bucket,
    string AccessKey,
    string SecretKey,
    string PublicBaseUrl,
    string PublicBaseKeyPrefix = "")
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public int MaxUploadAttempts { get; init; } = 3;

    public long MultipartThresholdBytes { get; init; } = 8L * 1024 * 1024;

    public long MultipartPartSizeBytes { get; init; } = 8L * 1024 * 1024;

    public int MultipartConcurrency { get; init; } = 4;
}
