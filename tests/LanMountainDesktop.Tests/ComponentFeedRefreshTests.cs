using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件"单飞取数"协议的行为钉（家在 <see cref="ComponentFeedRefresh"/>）。
///
/// 这套判据原来是 <c>DailyWordWidget</c> 与 <c>DailyWord2x2Widget</c> 各抄一份逐字相同的 45 行。
/// 抄本最坑的地方在于<strong>少抄一句不会报错</strong>：少一句"等完之后重新问一次挂载"就是往已经分离的
/// 可视树上落地；少一句 finally 里的复位就是按钮永久灰着。所以这里逐条钉判据，而不是只钉"能跑通"。
///
/// 有一条没钉：<c>Interlocked.Exchange</c> 那句"先换发再取消旧源"的顺序——它在单飞守卫下从公开入口走不到
/// （第二趟在飞的时候第一趟还占着忙，直接被挡在门外），所以只能算防御性写法，不是可达行为。
/// 别把它当成已经验证过的路径。
/// </summary>
public sealed class ComponentFeedRefreshTests
{
    [Fact]
    public async Task RunAsync_DoesNotStart_WhenTheComponentIsNotAttached()
    {
        var calls = new List<string>();
        var feed = new ComponentFeedRefresh();

        await feed.RunAsync(
            () => false,
            () => calls.Add("begin"),
            _ => { calls.Add("request"); return Task.FromResult(true); },
            () => calls.Add("failure"),
            () => calls.Add("end"));

        Assert.Empty(calls);
        Assert.False(feed.IsBusy);
    }

    [Fact]
    public async Task RunAsync_IgnoresASecondRequest_WhileOneIsInFlight()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var feed = new ComponentFeedRefresh();

        var first = feed.RunAsync(
            () => true,
            () => { },
            async _ =>
            {
                Interlocked.Increment(ref requests);
                started.SetResult();
                await release.Task;
                return true;
            },
            () => { },
            () => { });

        await started.Task;

        var second = feed.RunAsync(
            () => true,
            () => { },
            _ => { Interlocked.Increment(ref requests); return Task.FromResult(true); },
            () => { },
            () => { });

        await second;
        Assert.Equal(1, requests);

        release.SetResult();
        await first;
        Assert.Equal(1, requests);
        Assert.False(feed.IsBusy);
        Assert.Null(feed.InFlight);
    }

    [Fact]
    public async Task RunAsync_SkipsTheLanding_WhenTheComponentGoesAwayMidFlight()
    {
        var attached = true;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var feed = new ComponentFeedRefresh();

        var run = feed.RunAsync(
            () => attached,
            () => calls.Add("begin"),
            async _ =>
            {
                attached = false;
                await release.Task;
                return true;
            },
            () => calls.Add("failure"),
            () => calls.Add("end"));

        release.SetResult();
        await run;

        Assert.DoesNotContain("failure", calls);
        Assert.Contains("end", calls);
        Assert.False(feed.IsBusy);
        Assert.Null(feed.InFlight);
    }

    [Fact]
    public async Task RunAsync_PaintsFailure_WhenTheRequestReportsNoData()
    {
        var calls = new List<string>();
        var feed = new ComponentFeedRefresh();

        await feed.RunAsync(
            () => true,
            () => calls.Add("begin"),
            _ => Task.FromResult(false),
            () => calls.Add("failure"),
            () => calls.Add("end"));

        Assert.Equal(new[] { "begin", "failure", "end" }, calls);
    }

    [Fact]
    public async Task RunAsync_PaintsFailure_WhenTheRequestThrows()
    {
        var calls = new List<string>();
        var feed = new ComponentFeedRefresh();

        await feed.RunAsync(
            () => true,
            () => calls.Add("begin"),
            _ => Task.FromException<bool>(new InvalidOperationException("boom")),
            () => calls.Add("failure"),
            () => calls.Add("end"));

        Assert.Equal(new[] { "begin", "failure", "end" }, calls);
    }

    [Fact]
    public async Task RunAsync_KeepsQuiet_OnACancelledRequest()
    {
        var calls = new List<string>();
        var feed = new ComponentFeedRefresh();

        await feed.RunAsync(
            () => true,
            () => calls.Add("begin"),
            _ => Task.FromException<bool>(new OperationCanceledException()),
            () => calls.Add("failure"),
            () => calls.Add("end"));

        Assert.Equal(new[] { "begin", "end" }, calls);
    }

    [Fact]
    public async Task RunAsync_ResetsBusyAndReleasesTheSource_EvenAfterAFailure()
    {
        var feed = new ComponentFeedRefresh();

        await feed.RunAsync(
            () => true,
            () => { },
            _ => Task.FromException<bool>(new InvalidOperationException("boom")),
            () => { },
            () => { });

        Assert.False(feed.IsBusy);
        Assert.Null(feed.InFlight);
    }
}
