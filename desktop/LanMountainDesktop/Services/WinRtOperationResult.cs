using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// WinRT 异步操作"结果类型是谁"的唯一判据：先看操作类型自己的一元泛型实参，
/// 再看它实现的 <c>Windows.Foundation.IAsyncOperation`1</c>；两边都对不上给 <c>null</c>。
///
/// 此前三个服务各抄一份：`LocationService` 与 `WindowsSmtcMusicControlService` 逐字相同，
/// `WindowsNotificationListener` 是等价写法（先把一元泛型那步并成一个条件、接口那步用 && 连起来）。
/// 三抄本没漂开过，但"没漂"是靠人抄——判据认不出结果类型时症状是**静默拿不到值**
/// （定位 / 通知 / 播放状态一条都不报错），因为调用方一律把 <c>null</c> 当成"这次不算"直接回 null。
///
/// 按 FullName 字符串认接口是刻意的：宿主不在 WinRT 投影里编译，拿不到那个 CLR 类型；
/// <see cref="StringComparison.Ordinal"/> 也不能换——区域敏感性会让同一个类型名在别的机器上认不出来。
/// </summary>
internal static class WinRtOperationResult
{
    internal const string AsyncOperationInterfaceFullName = "Windows.Foundation.IAsyncOperation`1";

    public static Type? ResolveType(Type operationType)
    {
        if (operationType.IsGenericType && operationType.GetGenericArguments().Length == 1)
        {
            return operationType.GetGenericArguments()[0];
        }

        foreach (var iface in operationType.GetInterfaces())
        {
            if (iface.IsGenericType &&
                string.Equals(
                    iface.GetGenericTypeDefinition().FullName,
                    AsyncOperationInterfaceFullName,
                    StringComparison.Ordinal))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
