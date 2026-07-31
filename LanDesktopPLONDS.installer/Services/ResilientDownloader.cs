using System.Net;
using System.Net.Http.Headers;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 弹性下载器：支持 HTTP Range 续传和传输停滞检测。
/// 重试（指数退避）逻辑由调用方 InstallerPlondsClient 负责。
/// </summary>
internal static class ResilientDownloader
{
    /// <summary>传输停滞超时（秒）：连续无数据则中止当前尝试。</summary>
    internal const int StallTimeoutSeconds = 60;

    /// <summary>
    /// 单次下载尝试：检查 .partial 文件实现 Range 续传，使用停滞检测 CTS 保护传输。
    /// 成功时将 .partial 重命名为 destinationPath。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端（不限全局超时）。</param>
    /// <param name="url">下载 URL。</param>
    /// <param name="destinationPath">最终文件路径（.partial 会被重命名到此处）。</param>
    /// <param name="progress">进度回调，报告已下载字节数。</param>
    /// <param name="parentCancellationToken">外部取消令牌。</param>
    public static async Task DownloadSingleAttemptAsync(
        HttpClient httpClient,
        Uri url,
        string destinationPath,
        IProgress<long>? progress,
        CancellationToken parentCancellationToken)
    {
        var partialPath = $"{destinationPath}.partial";

        // 检查已有的 .partial 文件大小用于续传
        long existingBytes = 0;
        if (File.Exists(partialPath))
        {
            existingBytes = new FileInfo(partialPath).Length;
        }

        // 停滞检测：创建独立 CTS，超时则取消当前尝试
        using var stallCts = new CancellationTokenSource();
        using var stallTimer = new Timer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            stallCts,
            Timeout.Infinite,
            Timeout.Infinite);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            parentCancellationToken, stallCts.Token);

        // 重置停滞定时器
        stallTimer.Change(TimeSpan.FromSeconds(StallTimeoutSeconds), Timeout.InfiniteTimeSpan);

        try
        {
            HttpResponseMessage response;
            if (existingBytes > 0)
            {
                // 尝试 Range 续传
                response = await SendRangeRequestAsync(httpClient, url, existingBytes, linkedCts.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // 服务器不支持续传或已重置，从头下载
                    response.Dispose();
                    existingBytes = 0;
                    File.Delete(partialPath);
                    response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                        .ConfigureAwait(false);
                }
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    // 服务器支持续传，追加到已有文件
                }
                else if (response.IsSuccessStatusCode)
                {
                    // 服务器返回 200（不支持 Range），从头下载
                    existingBytes = 0;
                    File.Delete(partialPath);
                }
                else
                {
                    response.EnsureSuccessStatusCode(); // 抛出异常
                    return; // unreachable but satisfies compiler
                }
            }
            else
            {
                response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }

            try
            {
                var totalBytes = response.Content.Headers.ContentLength;
                await using var responseStream = await response.Content.ReadAsStreamAsync(linkedCts.Token)
                    .ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    partialPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);

                var buffer = new byte[128 * 1024];
                long totalDownloaded = existingBytes;

                while (true)
                {
                    var bytesRead = await responseStream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCts.Token).ConfigureAwait(false);
                    totalDownloaded += bytesRead;

                    // 重置停滞定时器
                    stallTimer.Change(TimeSpan.FromSeconds(StallTimeoutSeconds), Timeout.InfiniteTimeSpan);

                    progress?.Report(totalDownloaded);
                }

                await fileStream.FlushAsync(linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
            }

            // 下载完成，重命名 .partial → 目标文件
            File.Move(partialPath, destinationPath, overwrite: true);
        }
        finally
        {
            stallTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// 发送带 Range 头的 HEAD 请求检测服务器是否支持续传。
    /// </summary>
    public static async Task<bool> CheckRangeSupportAsync(
        HttpClient httpClient,
        Uri url,
        long existingBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return response.Headers.AcceptRanges.Contains("bytes");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 发送带 Range 头的 GET 请求开始续传下载。
    /// </summary>
    private static async Task<HttpResponseMessage> SendRangeRequestAsync(
        HttpClient httpClient,
        Uri url,
        long rangeStart,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(rangeStart, null);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }
}
