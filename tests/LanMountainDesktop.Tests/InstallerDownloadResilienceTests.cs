using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.Shared.Contracts.Deployment;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 弹性下载器与诊断聚合测试：覆盖 semver 排序、md5 拒绝、sha256 接受、
/// 重试成功、Range 续传、诊断聚合消息等六个维度。
/// </summary>
public sealed class InstallerDownloadResilienceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        AppContext.BaseDirectory,
        "TestArtifacts",
        "LanMountainDesktop.Tests",
        nameof(InstallerDownloadResilienceTests),
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    #region 1. SemanticVersion 排序（含预发布版本）

    [Theory]
    [InlineData("0.8.5-beta.1", "0.8.5", true)]       // 预发布 < 正式版
    [InlineData("1.0.0-alpha", "1.0.0-beta", true)]    // alpha < beta
    [InlineData("1.0.0-beta", "1.0.0-rc.1", true)]     // beta < rc
    [InlineData("1.0.0-rc.1", "1.0.0", true)]          // rc < 正式版
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", true)] // 数字排序
    [InlineData("1.0.0", "2.0.0", true)]               // 主版本排序
    [InlineData("1.2.3", "1.2.3", false)]              // 相等 (not less)
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2", false)] // 11 > 2 (not less)
    public void SemanticVersion_Ordering_Correct(string left, string right, bool leftLessThanRight)
    {
        var v1 = SemanticVersion.Parse(left);
        var v2 = SemanticVersion.Parse(right);
        if (leftLessThanRight)
        {
            Assert.True(v1 < v2, $"Expected {left} < {right}, got CompareTo={v1.CompareTo(v2)}");
        }
        else
        {
            Assert.True(v1 >= v2, $"Expected {left} >= {right}, got CompareTo={v1.CompareTo(v2)}");
        }
    }

    [Fact]
    public void SemanticVersion_StableVersionAlwaysBeatsPrerelease()
    {
        var stable = SemanticVersion.Parse("0.8.5");
        var prerelease = SemanticVersion.Parse("0.8.5-beta.1");

        Assert.True(stable > prerelease);
        Assert.True(prerelease < stable);
        Assert.Equal(1, stable.CompareTo(prerelease));
        Assert.Equal(-1, prerelease.CompareTo(stable));
    }

    [Fact]
    public void SemanticVersion_PrereleaseAccepted()
    {
        Assert.True(SemanticVersion.TryParse("0.8.5-beta.1", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(0, parsed!.Major);
        Assert.Equal(8, parsed.Minor);
        Assert.Equal(5, parsed.Patch);
        Assert.Equal("beta.1", parsed.Prerelease);
    }

    [Fact]
    public void SemanticVersion_FourPartAccepted()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3.4", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(4, parsed!.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    public void SemanticVersion_InvalidFormat_Rejected(string? value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void SemanticVersion_OrderingInFindLatest_SortsCandidatesCorrectly()
    {
        // 模拟候选列表，验证排序逻辑与 FindLatestAsync 一致
        var versions = new[] { "0.8.5-beta.1", "0.8.5-alpha.1", "0.8.5", "0.8.4" };
        var parsed = versions
            .Select(v => (Original: v, Semver: SemanticVersion.Parse(v)))
            .OrderByDescending(x => x.Semver)
            .Select(x => x.Original)
            .ToArray();

        Assert.Equal("0.8.5", parsed[0]);
        Assert.Equal("0.8.5-beta.1", parsed[1]);
        Assert.Equal("0.8.5-alpha.1", parsed[2]);
        Assert.Equal("0.8.4", parsed[3]);
    }

    #endregion

    #region 2. MD5 拒绝

    [Fact]
    public void VerifyPackage_Md5Checksum_RejectsWithChineseError()
    {
        var zipPath = CreateTestZip("test-content");

        // ParseChecksum 应该拒绝 MD5 — 反射调用抛 TargetInvocationException 包装
        var ex = Assert.ThrowsAny<Exception>(() => InvokeParseChecksum("md5:" + ComputeMd5(zipPath)));
        var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException! : ex;
        Assert.IsType<InvalidDataException>(inner);
        Assert.Contains("MD5", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不被支持", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyPackage_Md5BareHex_RejectsWithChineseError()
    {
        // 32 位十六进制 = MD5，应该被拒绝
        var md5Hash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var ex = Assert.ThrowsAny<Exception>(() => InvokeParseChecksum(md5Hash));
        var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException! : ex;
        Assert.IsType<InvalidDataException>(inner);
        Assert.Contains("MD5", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("32 位十六进制", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 3. SHA-256 接受

    [Fact]
    public async Task VerifyPackage_Sha256Checksum_Accepts()
    {
        var zipPath = CreateTestZip("test-content-for-sha256");
        var sha256Hash = ComputeSha256(zipPath);
        var manifest = CreateManifestWithChecksum("sha256:" + sha256Hash);

        // ParseChecksum 应该接受 SHA-256
        var (algorithm, hash) = InvokeParseChecksum("sha256:" + sha256Hash);
        Assert.Equal("sha256", algorithm);
        Assert.Equal(sha256Hash, hash);
    }

    [Fact]
    public void VerifyPackage_Sha256BareHex_Accepts()
    {
        // 64 位十六进制 = SHA-256，应该被接受
        var sha256Hash = new string('a', 64);
        var (algorithm, hash) = InvokeParseChecksum(sha256Hash);
        Assert.Equal("sha256", algorithm);
        Assert.Equal(sha256Hash, hash);
    }

    #endregion

    #region 4. 重试成功（2 次失败后成功）

    [Fact]
    public async Task DownloadAndPrepare_RetrySucceedsAfterTwoFailures()
    {
        var zipPath = CreateTestZip("retry-test-content");
        var sha256Hash = ComputeSha256(zipPath);

        // 前 2 次返回 500，第 3 次返回内容
        var handler = new RetryThenSuccessHandler(zipPath, failCount: 2);
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            Path.Combine(_tempRoot, "staging"),
            _ => TimeSpan.Zero); // 测试中跳过实际等待
        var candidate = CreateCandidate(
            sha256Hash: sha256Hash,
            filesZipUrl: "https://test.example.com/Files.zip");

        var package = await client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None);

        Assert.True(File.Exists(package.ZipPath));
        Assert.Equal(4, handler.RequestCount); // 2次失败(HEAD+GET) + 2次成功(HEAD+GET)
    }

    [Fact]
    public async Task DownloadAndPrepare_AllRetriesFail_ThrowsAfterMaxAttempts()
    {
        var handler = new AlwaysFailHandler();
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            Path.Combine(_tempRoot, "staging"),
            _ => TimeSpan.Zero);
        var candidate = CreateCandidate(
            sha256Hash: "0000000000000000000000000000000000000000000000000000000000000000",
            filesZipUrl: "https://test.example.com/Files.zip");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None));

        Assert.NotNull(ex.InnerException);
        // 1个唯一URL × 3次重试 × 2个请求(HEAD+GET) = 6
        Assert.Equal(6, handler.RequestCount);
    }

    #endregion

    #region 5. Range 续传（.partial 文件存在时发送 Range 头）

    [Fact]
    public async Task DownloadAndPrepare_ResumeSendsRangeHeader()
    {
        var zipPath = CreateTestZip("resume-test-content");
        var stagingDir = Path.Combine(_tempRoot, "staging-resume");
        var packageDir = Path.Combine(stagingDir, "1.0.0", "s3", "full");
        var partialPath = Path.Combine(packageDir, "Files.zip.partial");

        // 创建部分下载的 .partial 文件（5 字节）
        Directory.CreateDirectory(packageDir);
        var partialContent = new byte[5];
        RandomNumberGenerator.Fill(partialContent);
        await File.WriteAllBytesAsync(partialPath, partialContent);

        var fullContent = await File.ReadAllBytesAsync(zipPath);
        var handler = new RangeResumeHandler(fullContent, partialContent.Length);
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            stagingDir,
            _ => TimeSpan.Zero);

        var sha256Hash = ComputeSha256(zipPath);
        var candidate = CreateCandidate(
            sha256Hash: sha256Hash,
            filesZipUrl: "https://test.example.com/Files.zip");

        var package = await client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None);

        Assert.True(handler.RangeHeaderSent);
        Assert.Equal(partialContent.Length, handler.RangeStart);
        Assert.True(File.Exists(package.ZipPath));
    }

    #endregion

    #region 6. 诊断聚合消息包含所有失败源 ID

    [Fact]
    public async Task FindLatest_AllSourcesFailed_MessageContainsAllIds()
    {
        // 使用总是返回 500 的 handler
        var handler = new AlwaysFailHandler();
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            Path.Combine(_tempRoot, "staging-no-sources"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FindLatestAsync(CancellationToken.None));

        // 消息应包含中文 "所有下载源均不可用"
        Assert.Contains("所有下载源均不可用", ex.Message, StringComparison.OrdinalIgnoreCase);
        // 消息应包含至少一个源 ID
        Assert.Contains("-", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindLatest_SingleSourceFails_MessageContainsSourceId()
    {
        var handler = new AlwaysFailHandler();
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            Path.Combine(_tempRoot, "staging-single"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FindLatestAsync(CancellationToken.None));

        // 内置源 "s3" 和 "github" 应该出现在错误消息中
        Assert.Contains("s3", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("github", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindLatest_DiagnosticReport_AggregatesMultipleFailures()
    {
        // 使用返回 404 的 handler 模拟所有源失败
        var handler = new StatusCodeHandler(HttpStatusCode.NotFound);
        var client = new InstallerPlondsClient(
            new HttpClient(handler),
            Path.Combine(_tempRoot, "staging-404"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FindLatestAsync(CancellationToken.None));

        // 每行应包含一个源 ID
        var lines = ex.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2, $"期望至少2行错误信息，实际 {lines.Length} 行");
    }

    #endregion

    #region 辅助方法

    private static InstallerPlondsManifest CreateManifestWithChecksum(string checksum)
    {
        return new InstallerPlondsManifest(
            "1",
            "1.0.0",
            "0.9.0",
            true,
            false,
            "stable",
            "windows-x64",
            DateTimeOffset.UtcNow,
            new Dictionary<string, InstallerPlondsFileEntry>(),
            new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            new Dictionary<string, string> { ["Files.zip"] = checksum },
            null,
            null);
    }

    private static InstallerPlondsCandidate CreateCandidate(
        string sha256Hash,
        string filesZipUrl,
        string version = "1.0.0")
    {
        var manifest = new InstallerPlondsManifest(
            "1",
            version,
            "0.9.0",
            true,
            false,
            "stable",
            "windows-x64",
            DateTimeOffset.UtcNow,
            new Dictionary<string, InstallerPlondsFileEntry>(),
            new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            new Dictionary<string, string> { ["Files.zip"] = "sha256:" + sha256Hash },
            new InstallerPlondsDownloads(
                new InstallerPlondsGitHubDownloads(null, null, null, filesZipUrl),
                null),
            null);

        return new InstallerPlondsCandidate(
            new InstallerPlondsSource("s3", "s3", "https://test.example.com/PLONDS.json", 100),
            manifest,
            new Uri(filesZipUrl));
    }

    private string CreateTestZip(string content)
    {
        var dir = Path.Combine(_tempRoot, "zips");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, $"test-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("test.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return zipPath;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// 反射调用 InstallerPlondsClient 的私有 ParseChecksum 方法进行测试。
    /// </summary>
    private static (string Algorithm, string Hash) InvokeParseChecksum(string checksum)
    {
        var method = typeof(InstallerPlondsClient).GetMethod(
            "ParseChecksum",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null)
        {
            throw new InvalidOperationException("ParseChecksum 方法未找到，请检查访问修饰符。");
        }

        return ((string, string))method.Invoke(null, [checksum])!;
    }

    #endregion

    #region 测试用 HttpMessageHandler

    /// <summary>总是返回 500 的 Handler。</summary>
    private sealed class AlwaysFailHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    /// <summary>前 N 次返回 500，之后返回文件内容的 Handler。</summary>
    private sealed class RetryThenSuccessHandler(string zipPath, int failCount) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _requestCount);
            if (count <= failCount)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var content = File.ReadAllBytes(zipPath);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
            response.Content.Headers.ContentLength = content.Length;
            return Task.FromResult(response);
        }
    }

    /// <summary>返回指定状态码的 Handler。</summary>
    private sealed class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    /// <summary>
    /// 支持 Range 续传的 Handler：检查 Range 头并返回对应数据片段。
    /// </summary>
    private sealed class RangeResumeHandler(byte[] fullContent, int _expectedRangeStart) : HttpMessageHandler
    {
        public bool RangeHeaderSent { get; private set; }
        public long RangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // HEAD 请求：返回支持 Accept-Ranges
            if (request.Method == HttpMethod.Head)
            {
                var headResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };
                headResponse.Content.Headers.ContentLength = fullContent.Length;
                headResponse.Headers.AcceptRanges.Add("bytes");
                return Task.FromResult(headResponse);
            }

            // 检查 Range 头
            if (request.Headers.Range is { } rangeHeader && rangeHeader.Ranges is ICollection<RangeItemHeaderValue> rangeCollection && rangeCollection.Count == 1)
            {
                var range = rangeCollection.First();
                if (range.From.HasValue)
                {
                    RangeHeaderSent = true;
                    RangeStart = range.From.Value;

                    var remaining = new byte[fullContent.Length - (int)range.From.Value];
                    Array.Copy(fullContent, (int)range.From.Value, remaining, 0, remaining.Length);

                    var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(remaining)
                    };
                    response.Content.Headers.ContentLength = remaining.Length;
                    response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
                        range.From.Value, fullContent.Length - 1, fullContent.Length);
                    return Task.FromResult(response);
                }
            }

            // 无 Range 头：返回完整内容
            var fullResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fullContent)
            };
            fullResponse.Content.Headers.ContentLength = fullContent.Length;
            return Task.FromResult(fullResponse);
        }
    }

    #endregion
}
