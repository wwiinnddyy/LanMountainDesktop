using System;
using System.Buffers;

using Avalonia;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 池化点缓冲的租/还。两块 60fps 重绘的自绘图表控件此前各抄一份同样的实现，
/// 错法不是崩：还两次会污染池子（别处读到脏数组），该还时没还就是每帧漏一个数组。
/// </summary>
public sealed class PointBufferPoolTests
{
    [Fact]
    public void RentPointsAtLeast_NonPositiveRequest_LeavesTheBufferUntouched()
    {
        Point[]? buffer = null;

        PointBufferPool.RentPointsAtLeast(ref buffer, 0);
        Assert.Null(buffer);

        buffer = ArrayPool<Point>.Shared.Rent(8);
        var before = buffer;
        PointBufferPool.RentPointsAtLeast(ref buffer, -3);
        Assert.Same(before, buffer);
        PointBufferPool.ReturnPoints(ref buffer);
    }

    [Fact]
    public void RentPointsAtLeast_KeepsTheSameArray_WhileItIsStillBigEnough()
    {
        Point[]? buffer = null;
        PointBufferPool.RentPointsAtLeast(ref buffer, 64);
        Assert.NotNull(buffer);
        Assert.True(buffer!.Length >= 64);

        var first = buffer;
        PointBufferPool.RentPointsAtLeast(ref buffer, 10);
        Assert.Same(first, buffer);

        PointBufferPool.ReturnPoints(ref buffer);
        Assert.Null(buffer);
    }

    [Fact]
    public void RentPointsAtLeast_SwapsInABiggerArray_WhenTheBufferIsTooSmall()
    {
        Point[]? buffer = null;
        PointBufferPool.RentPointsAtLeast(ref buffer, 4);
        var small = buffer;

        PointBufferPool.RentPointsAtLeast(ref buffer, 512);
        Assert.NotSame(small, buffer);
        Assert.True(buffer!.Length >= 512);

        PointBufferPool.ReturnPoints(ref buffer);
    }

    [Fact]
    public void ReturnPoints_IsSafeToCallTwice()
    {
        Point[]? buffer = null;
        PointBufferPool.RentPointsAtLeast(ref buffer, 16);
        PointBufferPool.ReturnPoints(ref buffer);

        // 第二次不许把同一个数组再还一遍（那会往池子里塞两份同一个数组）。
        PointBufferPool.ReturnPoints(ref buffer);
        Assert.Null(buffer);
    }
}
