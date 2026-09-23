using LanMountainDesktop.Launcher.Startup;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 探活这一家钉的是"非法 pid 不当活"与"正在跑的进程要认出来是活的"两格，其余都要如实说清边界。
///
/// 实测出来的真边界（三条注入逐个量过，别信"看着像钉住了"）：
/// ① 非法 pid 那 3 格钉的是 <c>processId &lt;= 0</c> 那道守卫，**钉不住"吞不吞异常"**：守卫在 <c>try</c> 之前，
///   那三个 pid 根本走不到 <c>catch</c>。反过来把守卫写成 <c>&lt; 0</c> 也仍全绿（5 格全过，实测）——
///   <c>GetProcessById(0)</c> 抛、被 <c>catch</c> 吞掉，还是 <c>false</c>。
///   也就是说这两道防线**彼此遮蔽**：任何一条单独注入都不红，把 <c>catch</c> 改成 <c>throw;</c> 实测也 0 红。
///   要真钉住"取不到句柄不许把异常抛给调用方"，需要一个"pid 合法但拿不到/正在退"的夹具（真起真退一个进程），
///   离线门里没做——这一条是<b>未覆盖</b>，不是"已排除"。
/// ② 能钉住的是 <c>HasExited</c> 反向：把 <c>return !process.HasExited</c> 写成 <c>return process.HasExited</c>
///   → 红 2 格（"自家进程活着"的两个入口各一格，实测），另外 3 格仍绿——它们与本条判据无关。
/// ③ <c>IsLive</c> 释放句柄这一点离线量不到（数不到内核句柄）。收口前 7 个调用点全写成 <c>out _</c>、
///   每探一次留一个句柄，这次修复靠的是"两个入口分所有权"这条判据，不宣称测试覆盖。
/// </summary>
public sealed class LiveProcessProbeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void IllegalPid_IsNotLive_AndThrowsNothing(int processId)
    {
        Assert.False(LiveProcessProbe.IsLive(processId));
        Assert.False(LiveProcessProbe.TryGet(processId, out var process));
        Assert.Null(process);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RunningProcess_IsLiveOnBothEntrypoints(bool viaTryGet)
    {
        var self = Environment.ProcessId;

        if (viaTryGet)
        {
            Assert.True(LiveProcessProbe.TryGet(self, out var process));
            Assert.NotNull(process);
            Assert.Equal(self, process!.Id);
            Assert.False(process.HasExited);
            process.Dispose();
            return;
        }

        Assert.True(LiveProcessProbe.IsLive(self));
    }
}
