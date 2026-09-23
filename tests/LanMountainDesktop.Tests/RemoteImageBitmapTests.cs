using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件取网图那条判据的行为钉，全部离线可跑（一个真请求都不发）：
/// 钉的是"哪些地址根本不该发请求、请求要长什么样、失败时不许把异常抛给控件、而取消必须原样抛回去"。
///
/// 收口前 RSS 那两个组件各抄一份逐字 36 行——错法不报错，只会让图片静默不显示，
/// 或者在关闭过程中把"用户取消了"当成"图下坏了"继续往下走、碰到已释放的控件。
///
/// 载体用 <see cref="FakeHandler"/> 而不是真连一个保留端口：真连的写法下"守卫挡住了"和
/// "请求发出去了但失败"返回值都是 <c>null</c>，把守卫整段删掉测试照样绿——那条判据等于没钉住。
/// 记数以后，多一个请求就红；成功一栏另钉住"读流之前要回卷"，少那一行也红。
///
/// 覆盖面边界也量过，别当成"整段都钉住了"：把归一化换成原串时红的是 <c>file://</c>／<c>ftp://</c>／
/// <c>javascript:</c> 三格，空串与 <c>null</c> 两格仍绿——那两条在 <c>HttpRequestMessage</c> 构造处就炸、
/// 被通用 catch 兜成同一个 <c>null</c>，与"守卫挡下"给出同样的可观察结果。
/// 四个注入点逐条验过，各自只红对应的格子。
/// </summary>
public sealed class RemoteImageBitmapTests
{
    private static readonly byte[] OnePixelPng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:void(0)")]
    [InlineData("ftp://127.0.0.1/pub.png")]
    public async Task GetAsync_NonHttpAddress_DoesNotEvenSendARequest(string? url)
    {
        var handler = new FakeHandler(_ => Response(HttpStatusCode.OK, OnePixelPng));

        Bitmap? bitmap = await RemoteImageBitmap.GetAsync(Client(handler), url, CancellationToken.None);

        Assert.Empty(handler.RequestUris);
        Assert.Null(bitmap);
    }

    [AvaloniaFact]
    public async Task GetAsync_HttpAddress_SendsOneRequest_WithBrowserHeaders()
    {
        var handler = new FakeHandler(_ => Response(HttpStatusCode.OK, OnePixelPng));

        Bitmap? bitmap = await RemoteImageBitmap.GetAsync(
            Client(handler), "http://img.example/a.png", CancellationToken.None);

        Assert.Equal("http://img.example/a.png", Assert.Single(handler.RequestUris));
        Assert.Equal("image/avif,image/webp,image/apng,image/*,*/*;q=0.8", handler.Accept[0]);
        Assert.StartsWith("Mozilla/", handler.UserAgents[0]);
        // 这一栏同时是上一栏的对照：不是"什么都不发"才叫守住了，而是该发的照发。
        Assert.NotNull(bitmap);
        Assert.Equal(1, bitmap!.PixelSize.Width);
    }

    [AvaloniaFact]
    public void Payload_IsADecodablePng_OnItsOwn()
    {
        // 夹具自证：上一栏拿到 null 时，得先能区分"回卷那步没做"和"我给的字节本就不是图"。
        using var bitmap = new Bitmap(new MemoryStream(OnePixelPng));

        Assert.Equal(1, bitmap.PixelSize.Width);
    }

    [Fact]
    public async Task GetAsync_NonSuccessStatus_ReturnsNull_AfterTheRequest()
    {
        var handler = new FakeHandler(_ => Response(HttpStatusCode.NotFound, Array.Empty<byte>()));

        Bitmap? bitmap = await RemoteImageBitmap.GetAsync(
            Client(handler), "http://img.example/gone.png", CancellationToken.None);

        Assert.Single(handler.RequestUris);
        Assert.Null(bitmap);
    }

    [Fact]
    public async Task GetAsync_SwallowsTransportFailures_InsteadOfThrowing()
    {
        var handler = new FakeHandler(_ => throw new IOException("connection refused"));

        Bitmap? bitmap = await RemoteImageBitmap.GetAsync(
            Client(handler), "http://127.0.0.1:5/nope.png", CancellationToken.None);

        Assert.Null(bitmap);
    }

    [Fact]
    public async Task GetAsync_RethrowsCancellation()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("cancelled"));

        // 只有"取消要原样抛回去"那条 catch 在位才成立：整段被通用 catch 吞掉时这里拿到 null，红。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RemoteImageBitmap.GetAsync(Client(handler), "http://img.example/a.png", CancellationToken.None));
    }

    private static HttpClient Client(FakeHandler handler) => new(handler);

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string?> RequestUris { get; } = new();

        public List<string?> Accept { get; } = new();

        public List<string?> UserAgents { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.ToString());
            Accept.Add(request.Headers.Accept.ToString());
            UserAgents.Add(request.Headers.UserAgent.ToString());

            return Task.FromResult(_respond(request));
        }
    }
}
