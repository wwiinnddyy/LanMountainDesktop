using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using LanMountainDesktop.Helpers;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件从网上取一张图的唯一写法：只走 http/https、带浏览器 UA 与 image 接受头、
/// 拿不到就回 <c>null</c>（取消除外，要原样抛回去）。
///
/// 此前 RSS 那对组件（Cnr 与 Ifeng）各抄一份逐字相同的 36 行，画风一致但各自一条命：
/// 少一层守卫的症状是"图片没显示"而不是崩溃，所以漂开很难被发现——
/// 例如 <c>OperationCanceledException</c> 那一条要是被后面的通用 <c>catch</c> 吞掉，
/// 组件在关闭过程中就分不清"用户取消了"和"这张图下坏了"，会继续往下走并碰已经释放的控件。
///
/// <c>httpClient</c> 由调用方给：三家组件的超时并不相同（RSS 两个 8 秒、每日画作 10 秒），
/// 共用一个客户端会改连接池与超时行为，那是策略问题（见 #G1-BC 队列注），不在这笔里顺手做。
/// </summary>
internal static class RemoteImageBitmap
{
    public static async Task<Bitmap?> GetAsync(
        HttpClient httpClient,
        string? imageUrl,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = ExternalLinkLauncher.NormalizeHttpUrl(imageUrl);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, normalizedUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", HttpUserAgents.Browser);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return new Bitmap(memory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
