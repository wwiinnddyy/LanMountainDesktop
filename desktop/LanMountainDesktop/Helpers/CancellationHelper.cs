using System.Threading;

namespace LanMountainDesktop.Helpers;

/// <summary>
/// "取消并释放一个一次性 <see cref="CancellationTokenSource"/>"这件事只认这一处。
/// </summary>
/// <remarks>
/// 顺序是有讲究的：先从字段上摘下来再 Cancel/Dispose，别人才拿不到同一个源去二次取消
/// （对一个已 Dispose 的源再 Cancel 会抛 <c>ObjectDisposedException</c>）。
/// 收口前宿主里两种写法各抄了一遍：10 个组件各自有一份逐字相同的 <c>CancelRefreshRequest</c>，
/// 另有 12 处 <c>?.Cancel()</c> 后面根本不跟 <c>Dispose()</c>，前者漂了没人知道，
/// 后者每刷新一次就留一个没释放的源。
/// 这里只保证"该做的三步都在"，不接管"这个源还该不该被复用"——那是调用点的语义。
/// </remarks>
public static class CancellationHelper
{
    /// <summary>把源从 <paramref name="source"/> 上原子摘下来（置空）后取消并释放；本来就没有就是空操作。</summary>
    public static void CancelAndDispose(ref CancellationTokenSource? source)
        => CancelAndDispose(Interlocked.Exchange(ref source, null));

    /// <summary>取消并释放一个已经不再被任何字段持有的源。</summary>
    public static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        source.Cancel();
        source.Dispose();
    }
}
