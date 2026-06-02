using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

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
            PublicBaseKeyPrefix = NormalizeOptionalKeyPrefix(options.PublicBaseKeyPrefix),
            RequestTimeout = options.RequestTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : options.RequestTimeout,
            MaxUploadAttempts = Math.Max(1, options.MaxUploadAttempts),
            MultipartThresholdBytes = Math.Max(5L * 1024 * 1024, options.MultipartThresholdBytes),
            MultipartPartSizeBytes = Math.Max(5L * 1024 * 1024, options.MultipartPartSizeBytes),
            MultipartConcurrency = Math.Max(1, options.MultipartConcurrency)
        };

        ownsHttpClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = this.options.RequestTimeout
        };
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

        if (contentLength >= options.MultipartThresholdBytes)
        {
            try
            {
                await UploadFileMultipartAsync(sourcePath, key, upload.ContentType, contentLength, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"S3 multipart upload failed for {key}; falling back to single PUT. {ex.Message}");
            }
        }

        for (var attempt = 1; attempt <= options.MaxUploadAttempts; attempt++)
        {
            try
            {
                await UploadFileOnceAsync(sourcePath, key, upload.ContentType, payloadHash, contentLength, attempt, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < options.MaxUploadAttempts && IsRetriable(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                Console.Error.WriteLine($"S3 upload retry {attempt + 1}/{options.MaxUploadAttempts} for {key} after {delay.TotalSeconds:0}s: {ex.Message}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task UploadFileMultipartAsync(
        string sourcePath,
        string key,
        string? contentType,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var uploadId = await CreateMultipartUploadAsync(key, contentType, cancellationToken).ConfigureAwait(false);
        var partCount = checked((int)((contentLength + options.MultipartPartSizeBytes - 1) / options.MultipartPartSizeBytes));
        var parts = new PlondsS3UploadedPart[partCount];

        Console.WriteLine($"Uploading S3 object {key} ({FormatBytes(contentLength)}) using multipart upload {uploadId}: {partCount} parts, part size {FormatBytes(options.MultipartPartSizeBytes)}, concurrency {options.MultipartConcurrency}.");

        try
        {
            var completed = 0;
            await Parallel.ForEachAsync(
                Enumerable.Range(1, partCount),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MultipartConcurrency,
                    CancellationToken = cancellationToken
                },
                async (partNumber, token) =>
                {
                    var offset = (long)(partNumber - 1) * options.MultipartPartSizeBytes;
                    var length = Math.Min(options.MultipartPartSizeBytes, contentLength - offset);
                    parts[partNumber - 1] = await UploadMultipartPartWithRetriesAsync(
                        sourcePath,
                        key,
                        uploadId,
                        partNumber,
                        offset,
                        length,
                        token).ConfigureAwait(false);

                    var done = Interlocked.Increment(ref completed);
                    Console.WriteLine($"S3 multipart progress {key}: {done}/{partCount} parts uploaded.");
                }).ConfigureAwait(false);

            await CompleteMultipartUploadAsync(key, uploadId, parts, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Uploaded S3 object {key} using multipart upload.");
        }
        catch
        {
            await AbortMultipartUploadBestEffortAsync(key, uploadId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string> CreateMultipartUploadAsync(string key, string? contentType, CancellationToken cancellationToken)
    {
        var requestUri = BuildObjectUri(key, "uploads=");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        SignRequest(request, key, EmptyPayloadHash, DateTimeOffset.UtcNow);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"S3 create multipart upload failed for {key}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 512)}");
        }

        var uploadId = XDocument.Parse(body).Descendants().FirstOrDefault(element => element.Name.LocalName == "UploadId")?.Value;
        return string.IsNullOrWhiteSpace(uploadId)
            ? throw new InvalidOperationException($"S3 create multipart upload response did not include UploadId for {key}.")
            : uploadId;
    }

    private async Task<PlondsS3UploadedPart> UploadMultipartPartWithRetriesAsync(
        string sourcePath,
        string key,
        string uploadId,
        int partNumber,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.MaxUploadAttempts; attempt++)
        {
            try
            {
                return await UploadMultipartPartOnceAsync(
                    sourcePath,
                    key,
                    uploadId,
                    partNumber,
                    offset,
                    length,
                    attempt,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < options.MaxUploadAttempts && IsRetriable(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                Console.Error.WriteLine($"S3 multipart retry {attempt + 1}/{options.MaxUploadAttempts} for {key} part {partNumber} after {delay.TotalSeconds:0}s: {ex.Message}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"S3 multipart upload failed for {key} part {partNumber}.");
    }

    private async Task<PlondsS3UploadedPart> UploadMultipartPartOnceAsync(
        string sourcePath,
        string key,
        string uploadId,
        int partNumber,
        long offset,
        long length,
        int attempt,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildObjectUri(key, $"partNumber={partNumber}&uploadId={Uri.EscapeDataString(uploadId)}");
        var bytes = new byte[length];
        await using (var fileStream = File.OpenRead(sourcePath))
        {
            fileStream.Seek(offset, SeekOrigin.Begin);
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = await fileStream.ReadAsync(bytes.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Unexpected end of file while reading {sourcePath} for part {partNumber}.");
                }

                totalRead += read;
            }
        }

        var payloadHash = Sha256Hex(bytes);
        Console.WriteLine($"Uploading S3 multipart part {partNumber} for {key} ({FormatBytes(length)}), attempt {attempt}/{options.MaxUploadAttempts}.");

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentLength = length;
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = content
        };
        SignRequest(request, key, payloadHash, DateTimeOffset.UtcNow);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"S3 multipart upload failed for {key} part {partNumber}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 512)}");
        }

        var etag = response.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag))
        {
            throw new InvalidOperationException($"S3 multipart upload did not return ETag for {key} part {partNumber}.");
        }

        return new PlondsS3UploadedPart(partNumber, etag);
    }

    private async Task CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<PlondsS3UploadedPart> parts,
        CancellationToken cancellationToken)
    {
        var body = BuildCompleteMultipartUploadBody(parts);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var payloadHash = Sha256Hex(bodyBytes);
        var requestUri = BuildObjectUri(key, $"uploadId={Uri.EscapeDataString(uploadId)}");

        using var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Headers.ContentLength = bodyBytes.Length;
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        SignRequest(request, key, payloadHash, DateTimeOffset.UtcNow);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"S3 complete multipart upload failed for {key}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(responseBody, 512)}");
        }
    }

    private async Task AbortMultipartUploadBestEffortAsync(string key, string uploadId, CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = BuildObjectUri(key, $"uploadId={Uri.EscapeDataString(uploadId)}");
            using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
            SignRequest(request, key, EmptyPayloadHash, DateTimeOffset.UtcNow);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"S3 abort multipart upload failed for {key}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"S3 abort multipart upload failed for {key}: {ex.Message}");
        }
    }

    public async Task<bool> UploadFileIfChangedAsync(PlondsS3ObjectUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var sourcePath = Path.GetFullPath(upload.SourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("S3 upload source file not found.", sourcePath);
        }

        var key = NormalizeKey(upload.Key);
        var contentLength = new FileInfo(sourcePath).Length;
        var existing = await TryGetObjectInfoForUploadAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing?.ContentLength == contentLength)
        {
            Console.WriteLine($"Skipping S3 object {key}; existing object has matching size {FormatBytes(contentLength)}.");
            return false;
        }

        await UploadFileAsync(upload, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task UploadFileOnceAsync(
        string sourcePath,
        string key,
        string? contentType,
        string payloadHash,
        long contentLength,
        int attempt,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var requestUri = BuildObjectUri(key);
        Console.WriteLine($"Uploading S3 object {key} ({FormatBytes(contentLength)}), attempt {attempt}/{options.MaxUploadAttempts}.");

        await using var fileStream = File.OpenRead(sourcePath);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType);
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

        Console.WriteLine($"Uploaded S3 object {key}.");
    }

    public async Task EnsureObjectExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var objectInfo = await TryGetObjectInfoAsync(normalizedKey, cancellationToken).ConfigureAwait(false);
        if (objectInfo is null)
        {
            throw new InvalidOperationException($"S3 object verification failed for {normalizedKey}: object was not found.");
        }
    }

    public async Task<PlondsS3ObjectInfo?> TryGetObjectInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var now = DateTimeOffset.UtcNow;
        var requestUri = BuildObjectUri(normalizedKey);

        using var request = new HttpRequestMessage(HttpMethod.Head, requestUri);
        SignRequest(request, normalizedKey, EmptyPayloadHash, now);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"S3 object metadata lookup failed for {normalizedKey}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return new PlondsS3ObjectInfo(
            Key: normalizedKey,
            ContentLength: response.Content.Headers.ContentLength,
            ETag: response.Headers.ETag?.Tag);
    }

    private async Task<PlondsS3ObjectInfo?> TryGetObjectInfoForUploadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await TryGetObjectInfoAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"S3 object metadata lookup for {key} failed; uploading anyway. {ex.Message}");
            return null;
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
        var canonicalQueryString = BuildCanonicalQueryString(request.RequestUri);
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
            canonicalQueryString,
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

    private Uri BuildObjectUri(string key, string? query = null)
    {
        var bucketPrefix = Uri.EscapeDataString(options.Bucket).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        var path = $"{options.Endpoint.AbsolutePath.TrimEnd('/')}/{bucketPrefix}/{BuildCanonicalKey(key)}";
        var builder = new UriBuilder(options.Endpoint)
        {
            Path = path,
            Query = query ?? string.Empty
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

    private static string BuildCanonicalQueryString(Uri? uri)
    {
        if (uri is null || string.IsNullOrEmpty(uri.Query))
        {
            return string.Empty;
        }

        return string.Join("&", uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter =>
            {
                var parts = parameter.Split('=', 2);
                var name = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                return new KeyValuePair<string, string>(name, value);
            })
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .ThenBy(parameter => parameter.Value, StringComparer.Ordinal)
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
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

    private static string Sha256Hex(byte[] value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static string BuildCompleteMultipartUploadBody(IEnumerable<PlondsS3UploadedPart> parts)
    {
        var document = new XDocument(
            new XElement("CompleteMultipartUpload",
                parts.OrderBy(part => part.PartNumber)
                    .Select(part => new XElement("Part",
                        new XElement("PartNumber", part.PartNumber.ToString(CultureInfo.InvariantCulture)),
                        new XElement("ETag", part.ETag)))));

        return document.ToString(SaveOptions.DisableFormatting);
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

    private static bool IsRetriable(Exception exception)
    {
        if (exception is TaskCanceledException or TimeoutException or HttpRequestException)
        {
            return true;
        }

        return exception.InnerException is not null && IsRetriable(exception.InnerException);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private sealed record PlondsS3UploadedPart(int PartNumber, string ETag);
}
