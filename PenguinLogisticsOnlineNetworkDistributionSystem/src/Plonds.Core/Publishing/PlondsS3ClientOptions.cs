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

    /// <summary>单次重试退避的上限。跨境链路需要长窗口耐心重试时调大（如 120s）。</summary>
    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>上传限速（KB/s）。0 = 不限速。用于绕过对持续大流量上传的中间设备干扰——慢慢传。</summary>
    public int UploadRateKilobytesPerSecond { get; init; } = 0;

    /// <summary>强制 HTTP/1.1（禁用 HTTP/2）。部分中间设备会掐断长 HTTP/2 数据帧上传。</summary>
    public bool ForceHttp1 { get; init; } = false;

    public long MultipartThresholdBytes { get; init; } = 8L * 1024 * 1024;

    public long MultipartPartSizeBytes { get; init; } = 8L * 1024 * 1024;

    public int MultipartConcurrency { get; init; } = 4;
}
