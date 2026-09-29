using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Downloader;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 下载请求到底带着谁的身份出门（#G1-BF）。
///
/// 症状不是崩：构造 <c>ResumableDownloadService</c> 时传进去的 <c>HttpClient</c> 原来一个字没用过，
/// 于是市场包与更新包的下行请求报的是库的出厂 UA（<c>Downloader/5.9.4</c>），
/// 而本仓的规矩是"对外报出去的身份只有一处字面量、都来自 <c>HttpUserAgents</c>"。
/// 第一格钉映射本身，第二格钉**真到了线上**——因为库没有"收 HttpClient"的公开入口，
/// 只测字段赋值的话，"库其实没把 RequestConfiguration.UserAgent 用出去"这种情况是测不出来的。
///
/// 第二格用环回 TCP 自己搭一个最小白应答服务（不用 <c>HttpListener</c>：它在 Windows 上要 URL 保留，
/// CI 里会以"拒绝访问"红成假缺陷）。
/// </summary>
public sealed class ResumableDownloadIdentityTests
{
    [Fact]
    public void CreateConfiguration_CarriesTheCallersIdentity_AndKeepsTheTransportsOwnTimeout()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LMD-Test/1.0");

        var configuration = ResumableDownloadService.CreateConfiguration(
            new DownloadOptions(), useParallelDownload: false, client);

        Assert.Equal("LMD-Test/1.0", configuration.RequestConfiguration?.UserAgent);

        // 故意**不**把 API 的 20 秒预算搬到几十 MB 的包上：那是改下载语义，不是修身份。
        // 钉的是库出厂值（实测 100000 毫秒）没被改掉——哪天有人"顺手接上超时"，这一格要他先说清代价。
        Assert.Equal(100_000, configuration.HttpClientTimeout);
    }

    [Fact]
    public async Task DownloadAsync_AnnouncesTheApplicationInsteadOfTheLibrary()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = Encoding.UTF8.GetBytes(new string('a', 4096));
        var seenUserAgents = new System.Collections.Concurrent.ConcurrentBag<string?>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 每来一条连接就回同一个正文，并把请求头里的 User-Agent 记下来。
        var acceptLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await listener.AcceptTcpClientAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(tcp, body, seenUserAgents, cts.Token));
            }
        });

        var destination = Path.Combine(
            Path.GetTempPath(), "LMD.DownloadIdentity", Guid.NewGuid().ToString("N"), "payload.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LMD-Marketplace-Test/3.2");
            var service = new ResumableDownloadService(client);

            var result = await service.DownloadAsync(
                $"http://127.0.0.1:{port}/payload.bin",
                destination,
                new DownloadOptions(ExpectedSizeBytes: body.Length),
                progress: null,
                cancellationToken: cts.Token);

            Assert.True(result.Success, $"下载没成：{result.ErrorMessage}");
            Assert.Equal(body.Length, new FileInfo(destination).Length);
            Assert.NotEmpty(seenUserAgents);
            Assert.All(seenUserAgents, ua => Assert.Equal("LMD-Marketplace-Test/3.2", ua));
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            if (Directory.Exists(Path.GetDirectoryName(destination)!))
            {
                try
                {
                    Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true);
                }
                catch (IOException)
                {
                    // 收尾失败不影响判据，别让一条绿测试变成红灯
                }
            }
        }
    }

    private static async Task ServeAsync(
        TcpClient tcp, byte[] body, System.Collections.Concurrent.ConcurrentBag<string?> seen, CancellationToken token)
    {
        using (tcp)
        {
            var stream = tcp.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            string? line;
            string? userAgent = null;
            while ((line = await reader.ReadLineAsync(token)) != null)
            {
                if (line.Length == 0)
                {
                    break;
                }

                if (line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase))
                {
                    userAgent = line.Substring("User-Agent:".Length).Trim();
                }
            }

            seen.Add(userAgent);

            var head = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n"
                + "Content-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\nConnection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            await stream.WriteAsync(headBytes, token);
            await stream.WriteAsync(body, token);
            await stream.FlushAsync(token);
        }
    }
}
