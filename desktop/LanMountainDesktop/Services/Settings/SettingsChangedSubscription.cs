namespace LanMountainDesktop.Services.Settings;

using System;
using LanMountainDesktop.AirAppSdk;

/// <summary>
/// "订了设置变更就要在释放时退一次、且不许退两遍"只认这一处。
///
/// 三个 VM 各自写过一遍六行的守卫 + 退订 + 置位（逐字面曾计到 2 族）。收成一家是因为这条不变量
/// 漂开的两种错法都不报错：少了守卫 —— 同一个处理器被退两次（第二次是静默无操作，但字段已经被
/// 复用过，等于"看着退了其实没退"）；忘了置位 —— 释放之后又被打开，旧处理器继续响应设置变更。
/// 事件与处理器类型都由这里固定（<see cref="ISettingsService.Changed"/> 的委托），
/// 调用方只交出自己那个方法组。
/// </summary>
internal static class SettingsChangedSubscription
{
    public static void UnsubscribeOnce(ref bool disposed, ISettingsService settings, EventHandler<SettingsChangedEvent> handler)
    {
        if (disposed)
        {
            return;
        }

        settings.Changed -= handler;
        disposed = true;
    }
}
