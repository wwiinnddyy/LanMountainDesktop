using LanMountainDesktop.Helpers;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 一次性 <see cref="CancellationTokenSource"/> 的收尾三步（摘下字段 → Cancel → Dispose）的口径。
/// 收口前这一步在宿主里有 10 份方法复制 + 12 处手写两行，漂掉的通常是 Dispose；
/// 下面两条判据分别钉住"取消到了"和"真的释放了"，少一步就红。
/// </summary>
public sealed class CancellationHelperTests
{
    [Fact]
    public void CancelAndDispose_CancelsThenReleasesTheSource()
    {
        var source = new CancellationTokenSource();

        CancellationHelper.CancelAndDispose(source);

        Assert.True(source.IsCancellationRequested);
        // 释放过的源再 Cancel 会抛 ObjectDisposedException：这就是"Dispose 真的发生了"的证据
        Assert.Throws<ObjectDisposedException>(() => source.Cancel());
    }

    [Fact]
    public void CancelAndDispose_ByRef_DetachesTheFieldSoNobodyCancelsItTwice()
    {
        var source = new CancellationTokenSource();
        CancellationTokenSource? field = source;

        CancellationHelper.CancelAndDispose(ref field);

        Assert.Null(field);
        Assert.True(source.IsCancellationRequested);
    }

    [Fact]
    public void CancelAndDispose_OfNothing_IsANoOp()
    {
        CancellationTokenSource? field = null;

        CancellationHelper.CancelAndDispose(field);
        CancellationHelper.CancelAndDispose(ref field);

        Assert.Null(field);
    }
}
