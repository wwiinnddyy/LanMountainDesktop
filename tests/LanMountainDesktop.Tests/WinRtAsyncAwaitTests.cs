using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "把一扇 WinRT 操作等成结果值"那条判据的家唯一的证据，全部离线（不碰 WinRT 投影，靠反射递一个
/// 自造的 AsTask 泛型方法定义进去）。
///
/// 收口前 <c>LocationService</c> 与 <c>WindowsSmtcMusicControlService</c> 各抄一份逐字相同的实现，
/// 三份的共同错法是"静默"：认不出结果类型、拿不到 Task、没有 Result 属性都回 null，
/// 而失败与取消必须原样抛出去——调用方各自的 catch 正靠它分类（定位那条要看 HRESULT 判"被拒绝"还是"没定位到"）。
/// 所以这里既钉"该回 null 的三种情形"，也钉"该抛的两种情形不许多吞"。
///
/// 覆盖面边界：<c>ConfigureAwait</c> 那条轴（<c>WindowsNotificationListener</c> 与这两家的实际差别）
/// 这里量不到——它换的是"续接在哪个上下文"，不是返回值；那条留在 #G1-BC 等拍板，不混进这六格。
/// </summary>
public sealed class WinRtAsyncAwaitTests
{
    private const BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly MethodInfo AsTaskDefinition =
        typeof(WinRtAsyncAwaitTests).GetMethod(nameof(AsTask), NonPublicStatic)!;

    [Fact]
    public async Task AwaitAsync_ReturnsTheTaskResult()
    {
        var value = await WinRtAsyncAwait.AwaitAsync(new Operation<string>("42"), AsTaskDefinition, CancellationToken.None);

        Assert.Equal("42", value);
    }

    [Fact]
    public async Task AwaitAsync_NullOperation_IsNull_WithoutTouchingTheDefinition()
    {
        Assert.Null(await WinRtAsyncAwait.AwaitAsync(null, AsTaskDefinition, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitAsync_MissingAsTaskDefinition_IsNull()
    {
        // 真机上这条对应"WindowsRuntimeSystemExtensions 这个类型没加载上"，服务照样得回 null 而不是抛。
        Assert.Null(await WinRtAsyncAwait.AwaitAsync(new Operation<string>("42"), null, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitAsync_UnresolvableResultType_IsNull()
    {
        // 普通对象：一元泛型那步与 IAsyncOperation 那步都对不上。少了这条判据，
        // MakeGenericMethod(null) 会抛 ArgumentNullException——症状从"拿不到值"变成崩溃。
        Assert.Null(await WinRtAsyncAwait.AwaitAsync(new object(), AsTaskDefinition, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitAsync_WhenAsTaskReturnsSomethingThatIsNotATask_IsNull()
    {
        var notTask = typeof(WinRtAsyncAwaitTests)
            .GetMethod(nameof(NotATask), NonPublicStatic)!;

        Assert.Null(await WinRtAsyncAwait.AwaitAsync(new Operation<string>("42"), notTask, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitAsync_RethrowsFaultedOperation()
    {
        var faulted = typeof(WinRtAsyncAwaitTests)
            .GetMethod(nameof(Faulted), NonPublicStatic)!;

        // 调用方靠这条区分"被拒绝"和"没值"：吞成 null 就是把失败看成了空。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WinRtAsyncAwait.AwaitAsync(new Operation<string>("42"), faulted, CancellationToken.None));
    }

    [Fact]
    public async Task AwaitAsync_HonoursCancellation()
    {
        // 实测量到的一次改正：WaitAsync 对**已经完成**的 Task 不看 token（第一版夹具给的是
        // 立即完成的任务，于是这一格根本不会抛）。要钉"取消要传下去"，操作得真的还没回来。
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var slow = typeof(WinRtAsyncAwaitTests).GetMethod(nameof(Slow), NonPublicStatic)!;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WinRtAsyncAwait.AwaitAsync(new Operation<string>("42"), slow, cancelled.Token));
    }

    private sealed class Operation<T>(T value)
    {
        public T Value { get; } = value;
    }

    private static Task<T> AsTask<T>(Operation<T> operation) => Task.FromResult(operation.Value);

    private static object NotATask<T>(Operation<T> operation) => operation.Value!;

    private static Task<T> Faulted<T>(Operation<T> operation) =>
        Task.FromException<T>(new InvalidOperationException("denied"));

    private static async Task<T> Slow<T>(Operation<T> operation)
    {
        await Task.Delay(3000);
        return operation.Value;
    }
}
