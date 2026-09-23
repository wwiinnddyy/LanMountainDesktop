using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Launcher.Ipc;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// Launcher ↔ 宿主那一帧 IPC 的"读满 N 字节"判据的行为钉，纯离线（假流自己控制每次给几个字节）。
///
/// 为什么值得单独钉：管道流允许"一次只给一部分"，把一次 <c>ReadAsync</c> 的返回值当成"读完了"
/// 会让帧长与帧体错位——症状是对端解出垃圾或超时，两边各错一半时最难看。
/// 此前收发两端各抄一份逐字相同的实现（改动只落在一边就是这种错法的成因）。
/// </summary>
public sealed class LauncherIpcStreamIoTests
{
    [Fact]
    public async Task ReadExactAsync_FillsTheBuffer_WhenTheStreamGivesItAllAtOnce()
    {
        var buffer = new byte[4];

        var ok = await LauncherIpcStreamIo.ReadExactAsync(
            new ScriptedStream(new byte[] { 1, 2, 3, 4 }, chunk: 4), buffer, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
    }

    [Fact]
    public async Task ReadExactAsync_KeepsLooping_WhenTheStreamFeedsOneByteAtATime()
    {
        // 这一格是整条判据的存在理由：只信第一次 ReadAsync 的实现会在这里拿到 1 字节就当读完。
        var buffer = new byte[4];

        var ok = await LauncherIpcStreamIo.ReadExactAsync(
            new ScriptedStream(new byte[] { 7, 8, 9, 10 }, chunk: 1), buffer, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(new byte[] { 7, 8, 9, 10 }, buffer);
    }

    [Fact]
    public async Task ReadExactAsync_ReturnsFalse_WhenThePeerStopsEarly()
    {
        var buffer = new byte[4];

        var ok = await LauncherIpcStreamIo.ReadExactAsync(
            new ScriptedStream(new byte[] { 1, 2 }, chunk: 2), buffer, CancellationToken.None);

        Assert.False(ok);
        // 已读到的部分不回滚（调用方拿到 false 就整帧作废）；这条钉的是"别把半截帧当成功"。
        Assert.Equal(new byte[] { 1, 2, 0, 0 }, buffer);
    }

    [Fact]
    public async Task ReadExactAsync_EmptyBuffer_IsTrue_WithoutTouchingTheStream()
    {
        Assert.True(await LauncherIpcStreamIo.ReadExactAsync(
            new ScriptedStream(Array.Empty<byte>(), chunk: 1, throwOnRead: true),
            Array.Empty<byte>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReadExactAsync_PropagatesCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // 取消不许被判成"对端收线"（那会返回 false，调用方就把它当成一次正常的坏帧继续跑）。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LauncherIpcStreamIo.ReadExactAsync(
            new ScriptedStream(new byte[] { 1, 2, 3, 4 }, chunk: 4, hangUntilCancelled: true),
            new byte[4],
            cancelled.Token));
    }

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private readonly bool _throwOnRead;
        private readonly bool _hangUntilCancelled;
        private int _position;

        public ScriptedStream(
            byte[] data,
            int chunk,
            bool throwOnRead = false,
            bool hangUntilCancelled = false)
        {
            _data = data;
            _chunk = chunk;
            _throwOnRead = throwOnRead;
            _hangUntilCancelled = hangUntilCancelled;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_throwOnRead)
            {
                throw new InvalidOperationException("这一格要求实现根本不去读流");
            }

            if (_hangUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (_position >= _data.Length)
            {
                return 0;
            }

            var take = Math.Min(Math.Min(buffer.Length, _chunk), _data.Length - _position);
            _data.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
