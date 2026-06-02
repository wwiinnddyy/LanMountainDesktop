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
}
