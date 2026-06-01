using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Plonds.Core.Publishing;

public sealed class PlondsS3Client : IDisposable
{
    private const string ServiceName = "s3";
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly PlondsS3ClientOptions options;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public PlondsS3Client(PlondsS3ClientOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options with
        {
            Endpoint = NormalizeEndpoint(options.Endpoint),
            Region = Require(options.Region, nameof(options.Region)),
            Bucket = Require(options.Bucket, nameof(options.Bucket)),
            AccessKey = Require(options.AccessKey, nameof(options.AccessKey)),
            SecretKey = Require(options.SecretKey, nameof(options.SecretKey)),
            PublicBaseUrl = Require(options.PublicBaseUrl, nameof(options.PublicBaseUrl)).TrimEnd('/'),
            PublicBaseKeyPrefix = NormalizeOptionalKeyPrefix(options.PublicBaseKeyPrefix)
        };

        this.httpClient = httpClient ?? new HttpClient();
        ownsHttpClient = httpClient is null;
    }

    public async Task UploadFileAsync(PlondsS3ObjectUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var sourcePath = Path.GetFullPath(upload.SourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("S3 upload source file not found.", sourcePath);
        }

        var key = NormalizeKey(upload.Key);
        var payloadHash = PayloadUtilities.ComputeSha256(sourcePath);
        var contentLength = new FileInfo(sourcePath).Length;
        var now = DateTimeOffset.UtcNow;
        var requestUri = BuildObjectUri(key);

        using var content = new StreamContent(File.OpenRead(sourcePath));
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(upload.ContentType)
            ? "application/octet-stream"
            : upload.ContentType);
        content.Headers.ContentLength = contentLength;

        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = content
        };

        SignRequest(request, key, payloadHash, now);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"S3 upload failed for {key}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 512)}");
        }
    }

    public async Task EnsureObjectExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var now = DateTimeOffset.UtcNow;
        var requestUri = BuildObjectUri(normalizedKey);

        using var request = new HttpRequestMessage(HttpMethod.Head, requestUri);
        SignRequest(request, normalizedKey, EmptyPayloadHash, now);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"S3 object verification failed for {normalizedKey}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    public string BuildPublicUrl(string key)
    {
        var normalizedKey = NormalizeKey(key);
        if (!string.IsNullOrWhiteSpace(options.PublicBaseKeyPrefix) &&
            (string.Equals(normalizedKey, options.PublicBaseKeyPrefix, StringComparison.OrdinalIgnoreCase) ||
             normalizedKey.StartsWith($"{options.PublicBaseKeyPrefix}/", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedKey = normalizedKey[options.PublicBaseKeyPrefix.Length..].TrimStart('/');
        }

        return $"{options.PublicBaseUrl}/{normalizedKey}";
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private void SignRequest(HttpRequestMessage request, string key, string payloadHash, DateTimeOffset now)
    {
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credentialScope = $"{dateStamp}/{options.Region}/{ServiceName}/aws4_request";
        var canonicalUri = BuildCanonicalUri(key);
        var host = request.RequestUri?.IsDefaultPort == true
            ? request.RequestUri.Host
            : request.RequestUri?.Authority;

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Cannot sign an S3 request without a host.");
        }

        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

        var canonicalHeaders = new StringBuilder();
        canonicalHeaders.Append("host:").Append(host).Append('\n');
        canonicalHeaders.Append("x-amz-content-sha256:").Append(payloadHash).Append('\n');
        canonicalHeaders.Append("x-amz-date:").Append(amzDate).Append('\n');

        var signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = string.Join('\n',
        [
            request.Method.Method,
            canonicalUri,
            string.Empty,
            canonicalHeaders.ToString(),
            signedHeaders,
            payloadHash
        ]);

        var stringToSign = string.Join('\n',
        [
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Sha256Hex(canonicalRequest)
        ]);

        var signingKey = GetSignatureKey(options.SecretKey, dateStamp, options.Region, ServiceName);
        var signature = HmacSha256Hex(signingKey, stringToSign);
        var authorization = $"AWS4-HMAC-SHA256 Credential={options.AccessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    private Uri BuildObjectUri(string key)
    {
        var bucketPrefix = Uri.EscapeDataString(options.Bucket).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        var path = $"{options.Endpoint.AbsolutePath.TrimEnd('/')}/{bucketPrefix}/{BuildCanonicalKey(key)}";
        var builder = new UriBuilder(options.Endpoint)
        {
            Path = path
        };

        return builder.Uri;
    }

    private string BuildCanonicalUri(string key)
    {
        var bucketPrefix = Uri.EscapeDataString(options.Bucket).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        return $"{options.Endpoint.AbsolutePath.TrimEnd('/')}/{bucketPrefix}/{BuildCanonicalKey(key)}";
    }

    private static string BuildCanonicalKey(string key)
    {
        return string.Join("/", NormalizeKey(key)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid S3 object key: {value}", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeOptionalKeyPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return NormalizeKey(value);
    }

    private static Uri NormalizeEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("S3 endpoint must be an absolute URI.", nameof(endpoint));
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = endpoint.AbsolutePath.TrimEnd('/')
        };

        return builder.Uri;
    }

    private static string Require(string value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Trim();
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    }

    private static string HmacSha256Hex(byte[] key, string data)
    {
        return Convert.ToHexString(HmacSha256(key, data)).ToLowerInvariant();
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{key}"), dateStamp);
        var kRegion = HmacSha256(kDate, regionName);
        var kService = HmacSha256(kRegion, serviceName);
        return HmacSha256(kService, "aws4_request");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
