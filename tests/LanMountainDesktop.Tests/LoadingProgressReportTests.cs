using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Loading;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 启动进度上报这条链的钉（#G1-CI，2026-09-29）。
///
/// 原来那条"整体进度变了"的事件带三个字段，没有一个对接得上：<c>Stage</c> 是写死的常量
/// （#G1-AZ 之后这条模型就没有"阶段"那一维）、<c>OverallProgressPercent</c> 没人读
/// （订阅者自己从条目重算详单）、<c>Message</c> 反过来从来没人写——被读了个恒 null。
/// 摘掉之后由这两格说清剩下的形状：上报文案里那句人话**只有一个来源**（当前活动条目的消息），
/// 而条目没带消息时也照报（带"哪个条目 + 完成了几/共几个"），不会退化成沉默。
/// 那个覆盖参数（唯一供应方就是恒 null 的字段）一起删了——留着是一条谁也走不到的分支。
///
/// 写这组测试时踩到自己一个错判，值得留在这里：这条链上 <c>StateChanged</c> 与
/// <c>OverallProgressChanged</c> 两条事件**都会**触发上报（注册条目本身就发一条，那时条目还没有消息），
/// 所以"取第一条到达的上报"断言不到进度那条的内容——第一版就是这么假红了一次。
/// 现在按条件等，而不是按顺序取。
/// </summary>
public sealed class LoadingProgressReportTests : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly LoadingStateManager _manager = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly LoadingStateReporter _reporter;

    public LoadingProgressReportTests()
    {
        _reporter = new LoadingStateReporter(_manager, _publisher)
        {
            EnableBatching = false,
            MinReportIntervalMs = 0,
        };
    }

    [Fact]
    public async Task OverallProgressChange_ReportsTheActiveItemsOwnMessage()
    {
        _manager.RegisterItem("weather", LoadingItemType.Data, "天气");
        _manager.StartItem("weather", "正在拉取天气");

        var published = await _publisher.NextMatchingAsync(
            message => message.Message?.Contains("正在拉取天气", StringComparison.Ordinal) == true);

        Assert.Contains("正在拉取天气", published.Message, StringComparison.Ordinal);
        Assert.Contains("[Data] 天气", published.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemWithoutMessage_StillReportsWithCounts()
    {
        _manager.RegisterItem("weather", LoadingItemType.Data, "天气");
        _manager.StartItem("weather");

        var published = await _publisher.NextMatchingAsync(
            message => message.Message?.Contains("(0/1)", StringComparison.Ordinal) == true);

        // 条目没带消息时也照报：文案由"哪个条目 + 计数"拼出来。
        // 钉的是这条链不再依赖那个从来没人写的事件字段——少了它，上报不会变成空白。
        Assert.Contains("[Data] 天气", published.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _reporter.Dispose();
        _manager.Dispose();
    }

    private sealed class RecordingPublisher : IExternalIpcNotificationPublisher
    {
        private readonly BlockingCollection<LoadingStateMessage> _seen = new();

        public Task NotifyAsync<TPayload>(string notifyId, TPayload payload, CancellationToken cancellationToken = default)
            where TPayload : class
        {
            if (payload is LoadingStateMessage message)
            {
                _seen.Add(message);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 等到一条满足条件的上报。取"第一条"不行：这条链上注册条目也会触发一次上报，
        /// 那条本来就没有条目消息（第一版把它当进度那条断言，红过一次）。
        /// </summary>
        public Task<LoadingStateMessage> NextMatchingAsync(Func<LoadingStateMessage, bool> matches)
        {
            var deadline = DateTime.UtcNow.Add(WaitTimeout);
            while (DateTime.UtcNow < deadline)
            {
                if (_seen.TryTake(out var message, TimeSpan.FromMilliseconds(100)) && matches(message))
                {
                    return Task.FromResult(message);
                }
            }

            throw new InvalidOperationException(
                "等不到符合条件的上报：这条链断在事件订阅、排队还是发送那一步？"
                + "发送是 Task.Run 起的后台活，所以必须带超时等——读成\"没发生\"不等于没接线。");
        }
    }
}
