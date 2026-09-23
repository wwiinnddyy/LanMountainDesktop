using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Launcher.Ipc;

/// <summary>
/// Launcher 与宿主之间那一帧 IPC 的"读满 N 个字节"唯一实现：要么把 <paramref name="buffer"/>
/// 整块填满（返回 <c>true</c>），要么在对端提前收线时返回 <c>false</c>；取消原样抛出。
///
/// 此前 <c>LauncherCoordinatorIpcClient</c> 与 <c>LauncherCoordinatorIpcServer</c> 各抄一份逐字相同的实现。
/// 收口的理由是这条判据错法都不报错：管道流允许"一次只给一部分"，把 <c>ReadAsync</c> 的返回值当成
/// "读完了"就会让帧长与帧体错位——症状是对端解出一坨垃圾或直接超时，两边各错一半时更难看（一边以为发完了）。
/// 空缓冲区返回 <c>true</c> 且不碰流：调用方拿它读 4 字节长度头，长度头之外也可能给一个零长数组。
/// </summary>
internal static class LauncherIpcStreamIo
{
    internal static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }
}
