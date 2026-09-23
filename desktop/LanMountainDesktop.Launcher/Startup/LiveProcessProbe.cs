using System.Diagnostics;

namespace LanMountainDesktop.Launcher.Startup;

/// <summary>
/// "这个 pid 现在还活着吗"只认这一家。收口前三份抄本全在启动器里：
/// <c>LaunchResultBuilder.TryGetLiveProcess</c>（公开）与 <c>StartupAttemptRegistry</c> 那份逐字相同，
/// <c>LauncherGuiCoordinator</c> 还有第三种形状（只回 bool、内部 <c>using</c> 掉句柄）。
///
/// 漂开的那半不是风格问题，是漏句柄：前两份把 <c>Process</c> 从 <c>GetProcessById</c> 里递出来，
/// 而当时 <b>7 个调用点全部写成 <c>out _</c></b>——拿到的 <c>Process</c> 没人释放，
/// 每探一次活死就留一个内核句柄，要等终结器才收。启动器探活是反复做的（协调器状态回灌、
/// 收养判定、清理陈旧登记），所以它是慢慢涨的那种症状，不会当场报错。
/// 于是这一家分成两个入口，各管一种所有权：<see cref="IsLive"/> 只问活死、句柄自己释放（那 7 个点走这条）；
/// <see cref="TryGet"/> 把句柄交出去，<b>拿到就要负责释放</b>（只有一个调用点真要那个句柄：
/// 收养已在跑的宿主时，要把进程交给后续挂接）。
///
/// <c>HasExited</c> 本身也可能抛（权限、进程正在退），所以整个探活包在 <c>catch</c> 里、失败一律算"不当活"：
/// 判成死掉的后果是重新起一个宿主，判成活着的后果是把 IPC 连到一个正在消失的进程上——前者可恢复，后者不可。
/// </summary>
internal static class LiveProcessProbe
{
    public static bool IsLive(int processId)
    {
        if (!TryGet(processId, out var process))
        {
            return false;
        }

        process?.Dispose();
        return true;
    }

    /// <summary>句柄交给调用方：返回 true 时 <c>process</c> 非空，调用方必须释放。</summary>
    public static bool TryGet(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }
}
