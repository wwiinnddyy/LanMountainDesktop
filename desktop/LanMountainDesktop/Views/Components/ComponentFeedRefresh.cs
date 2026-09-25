using System;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件"发起一次取数、期间可能被换掉或摘掉"这套单飞协议只认这一处。
///
/// 钉住的判据（每一条错了都不报错，只是行为不对）：
/// ① 没挂在桌面上、或已经有一趟在跑 → 一趟都不发起；
/// ② 起一趟之前先置忙并让 <paramref name="begin"/> 画"正在刷新"，顺序反了按钮就不变灰；
/// ③ 新的 <see cref="CancellationTokenSource"/> 先换上再取消旧的（<c>Interlocked.Exchange</c>，
///    反过来会把还在用的源取消掉）；
/// ④ 等完之后**重新问一次**挂载与取消——请求在飞的时候组件可能已经被摘掉，
///    这时落地就是碰一棵已经分离的可视树；
/// ⑤ 取不到数据与抛异常都画失败态，但 <see cref="OperationCanceledException"/> 静默
///    （那是我们自己换掉/摘掉的，不是失败）；
/// ⑥ 收尾只在"这把还是我的那把"时清空字段，然后释放、复位忙、再画一次按钮。
///
/// 为什么收成一家：<c>DailyWordWidget</c> 与 <c>DailyWord2x2Widget</c> 各抄了一份逐字相同的 45 行
/// （2026-09-24 由普查尺子量出），另有 9 个组件是同一套骨架换了别的取数调用（挂在 #G1-BC）。
/// 抄本少一句 ④ 的症状是"关掉组件后偶发 ObjectDisposedException / 界面再也不刷新"，
/// 少一句 ⑥ 则是每次刷新都往一个已释放的源上取消。
/// </summary>
internal sealed class ComponentFeedRefresh
{
    /// <summary>
    /// 当前在飞的那把源；由本类换与释放，<c>ComponentRefreshLifetime.Detach</c> 以 <c>ref</c> 摘掉它
    /// （所以它得是字段而不是属性）。
    /// </summary>
    public CancellationTokenSource? InFlight;

    /// <summary>是否有一趟在飞（按钮的禁用与图标透明度读它）。</summary>
    public bool IsBusy { get; private set; }

    /// <param name="request">真正去取数并落地。返回 false 表示这次没拿到数据（按失败态画）；
    /// 抛异常表示失败。<b>不要</b>在这里画失败态——④ 那道重问由家负责。</param>
    public async Task RunAsync(
        Func<bool> isAttached,
        Action begin,
        Func<CancellationToken, Task<bool>> request,
        Action applyFailure,
        Action? end = null)
    {
        if (!isAttached() || IsBusy)
        {
            return;
        }

        IsBusy = true;
        begin();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref InFlight, cts);
        CancellationHelper.CancelAndDispose(previous);

        try
        {
            var succeeded = await request(cts.Token);
            if (!isAttached() || cts.IsCancellationRequested)
            {
                return;
            }

            if (!succeeded)
            {
                applyFailure();
            }
        }
        catch (OperationCanceledException)
        {
            // 自己换掉或摘掉的：不算失败，也不画失败态。
        }
        catch
        {
            if (isAttached() && !cts.IsCancellationRequested)
            {
                applyFailure();
            }
        }
        finally
        {
            if (ReferenceEquals(InFlight, cts))
            {
                InFlight = null;
            }

            cts.Dispose();
            IsBusy = false;
            end();
        }
    }
}
