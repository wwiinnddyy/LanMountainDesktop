using System;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 时区在下拉列表里怎么写，只认这一处。收口前两个时钟类编辑器（<c>ClockComponentEditor</c> 与
/// <c>WorldClockComponentEditor</c>）各抄一份逐字相同的六行——漂开的症状是"两个下拉里同一个时区长得不一样"，
/// 不报错也不崩，所以得钉格式而不是只删抄本。
///
/// 口径三条：① 取的是**当下那一瞬间**的偏移（<c>GetUtcOffset(now)</c>，不是 <c>BaseUtcOffset</c>），
/// 带夏令时的时区因此会随季节换数字；② 符号按偏移正负给 <c>+</c>／<c>-</c>，零算正；
/// ③ 小时与分钟各两位（<c>+05:30</c> 而不是 <c>+5:30</c>），名字用 <c>StandardName</c>，
/// 括号与名字之间一个空格。这些形状是给用户读的、也是两个编辑器对齐的依据，改一处要两处一起改。
/// </summary>
internal static class TimeZoneDisplayLabel
{
    public static string Format(TimeZoneInfo timeZone)
    {
        return Format(timeZone, DateTime.UtcNow);
    }

    public static string Format(TimeZoneInfo timeZone, DateTime now)
    {
        var offset = timeZone.GetUtcOffset(now);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var totalMinutes = Math.Abs((int)offset.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {timeZone.StandardName}";
    }
}
