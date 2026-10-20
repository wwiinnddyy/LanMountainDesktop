using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Plonds.Core.Publishing;
using Plonds.Shared.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PlondsPublishingRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LanDesktop.PublishingTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static PlondsS3ClientOptions Options => new(new Uri("https://s3.test"), "region", "bucket", "fixture-key", "fixture-secret", "https://public.test/bucket")
        { MaxUploadAttempts = 1 };

    [Fact]
    public void PublicUrl_PreservesTheUploadedObjectKey()
    {
        using var client = new PlondsS3Client(Options);
        Assert.Equal("https://public.test/bucket/lanmountain/update/plonds/2.0.0/Files.zip",
            client.BuildPublicUrl("lanmountain/update/plonds/2.0.0/Files.zip"));
    }

    [Fact]
    public void LegacyManifest_ForcesOldClientToUseCompleteInstaller()
    {
        var manifest = Manifest("2.0.0") with
        {
            Downloads = new("v2.0.0",
                new("https://fixture.test/release", "https://fixture.test/manifest", "https://fixture.test/delta", "https://fixture.test/full"),
                new("bucket", "prefix", "manifest", "https://fixture.test/manifest", "delta", "https://fixture.test/delta", "folder", "https://fixture.test/folder",
                    "full", "https://fixture.test/full", "folder", "https://fixture.test/folder"))
        };
        var legacy = PlondsPublisher.CreateLegacyBootstrapManifest(manifest);
        Assert.True(legacy.RequiresCleanInstall);
        Assert.Empty(legacy.Downloads!.GitHub.FilesZipUrl);
        Assert.Empty(legacy.Downloads.S3.FilesZipUrl);
        Assert.Empty(legacy.Downloads.S3.FilesZipKey);
        Assert.Equal("https://fixture.test/full", manifest.Downloads!.S3.FilesZipUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSizeObject_IsSkippedOnlyWhenContentHashMatches(bool sameHash)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "content.bin");
        File.WriteAllText(source, "new");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("new"))).ToLowerInvariant();
        var puts = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[3]) };
                response.Headers.TryAddWithoutValidation("x-amz-meta-sha256", sameHash ? expected : new string('0', 64));
                return response;
            }
            if (request.Method == HttpMethod.Put)
            {
                puts++;
                Assert.Equal(expected, request.Headers.GetValues("x-amz-meta-sha256").Single());
                Assert.Contains("x-amz-meta-sha256", request.Headers.GetValues("Authorization").Single());
            }
            return new(HttpStatusCode.OK);
        }));
        using var client = new PlondsS3Client(Options, http);
        var changed = await client.UploadFileIfChangedAsync(new(source, "content.bin", "application/octet-stream"));
        Assert.Equal(!sameHash, changed);
        Assert.Equal(sameHash ? 0 : 1, puts);
    }

    [Fact]
    public async Task PublicVerification_UsesAnonymousUrlAndRejectsMissingObject()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("public.test", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.NotFound);
        }));
        using var client = new PlondsS3Client(Options, http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VerifyPublicObjectAsync("Files.zip", CancellationToken.None));
    }

    [Theory]
    [InlineData("3.0.0", false)]
    [InlineData("1.0.0", true)]
    public async Task Publisher_DoesNotMoveLatestBackwards(string previousVersion, bool expectedPromotion)
    {
        var options = CreatePublishOptions();
        var promotions = 0;
        var uploaded = new HashSet<string>(StringComparer.Ordinal);
        uploaded.Add("/bucket/lanmountain/update/plonds/windows-x64/stable/PLONDS.json");
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Put) uploaded.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/stable/PLONDS.json", StringComparison.Ordinal)) promotions++;
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Manifest(previousVersion), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })) };
            return new(request.Method == HttpMethod.Head && request.RequestUri!.Host == "s3.test" &&
                !uploaded.Contains(request.RequestUri.AbsolutePath) ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        }));
        var publisher = new PlondsPublisher(s3 => new PlondsS3Client(s3, http));
        await publisher.PublishAsync(options);
        Assert.Equal(expectedPromotion ? 1 : 0, promotions);
    }

    [Fact]
    public async Task Publisher_PublicDownloadFailure_PreventsLatestPromotion()
    {
        var options = CreatePublishOptions();
        var promotions = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.EndsWith("/stable/PLONDS.json", StringComparison.Ordinal)) promotions++;
            return new(request.Method == HttpMethod.Head ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlondsPublisher(s3 => new PlondsS3Client(s3, http)).PublishAsync(options));
        Assert.Equal(0, promotions);
    }

    private PlondsPublishOptions CreatePublishOptions()
    {
        Directory.CreateDirectory(_root);
        var files = Path.Combine(_root, "source");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "file.txt"), "content");
        var zip = Path.Combine(_root, "Files.zip");
        ZipFile.CreateFromDirectory(files, zip);
        var changed = Path.Combine(_root, "changed-windows-x64.zip");
        File.Copy(zip, changed);
        var manifest = Path.Combine(_root, "PLONDS-windows-x64.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(Manifest("2.0.0"), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return new("v2.0.0", "fixture/repo", manifest, changed, zip, Path.Combine(_root, "work"),
            "lanmountain/update/plonds/windows-x64/stable", Options);
    }

    private static PlondsManifest Manifest(string version) =>
        new("2.0", version, "1.0.0", false, false, "stable", "windows-x64", DateTimeOffset.UtcNow,
            new Dictionary<string, PlondsFileEntry>(), new Dictionary<string, PlondsChangedFileEntry>(), new Dictionary<string, string>());

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
