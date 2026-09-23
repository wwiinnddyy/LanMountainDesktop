using System;
using System.Buffers;
using Avalonia;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 自绘折线那两块"临时点缓冲"的租与还，唯一一份实现。
/// 此前噪声曲线控件与噪声分布面积图控件各抄一份逐字相同的 <c>EnsurePointBufferCapacity</c>（14 行）
/// 与 <c>ReleasePointBuffer</c>（6 行）。
///
/// 为什么值得合成一处：租来的数组**必须带着原来的长度判断还**，写错的方式都不是崩，
/// 而是"同一个数组还了两次"（污染池子，别处就会读到脏数据）或"该还的时候没还"（每帧泄漏一个数组，
/// 这两块控件是 60fps 重绘的）。两处抄本以前只是恰好一样。
///
/// 缓冲留在各控件自己的字段里，这里只接 <c>ref</c>：换数组、置 null 都发生在调用方那一个变量上，
/// 不需要为共享一段逻辑而引入一层对象。
/// </summary>
internal static class PointBufferPool
{
    /// <summary>
    /// 保证 <paramref name="buffer"/> 至少能放 <paramref name="required"/> 个点。
    /// 已经够长就原样返回（不重新租，避免每帧换数组）；<paramref name="required"/> 非正数时不动它。
    /// </summary>
    public static void RentPointsAtLeast(ref Point[]? buffer, int required)
    {
        if (required <= 0)
        {
            return;
        }

        if (buffer is not null && buffer.Length >= required)
        {
            return;
        }

        var next = ArrayPool<Point>.Shared.Rent(required);
        if (buffer is not null)
        {
            ArrayPool<Point>.Shared.Return(buffer, clearArray: false);
        }

        buffer = next;
    }

    /// <summary>把缓冲还给池子并把变量置空；本来就没有缓冲时什么都不做（可以重复调）。</summary>
    public static void ReturnPoints(ref Point[]? buffer)
    {
        if (buffer is null)
        {
            return;
        }

        ArrayPool<Point>.Shared.Return(buffer, clearArray: false);
        buffer = null;
    }
}
