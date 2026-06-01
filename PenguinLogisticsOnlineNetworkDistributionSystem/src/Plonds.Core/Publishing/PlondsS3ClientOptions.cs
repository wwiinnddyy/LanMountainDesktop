namespace Plonds.Core.Publishing;

public sealed record PlondsS3ClientOptions(
    Uri Endpoint,
    string Region,
    string Bucket,
    string AccessKey,
    string SecretKey,
    string PublicBaseUrl,
    string PublicBaseKeyPrefix = "");
