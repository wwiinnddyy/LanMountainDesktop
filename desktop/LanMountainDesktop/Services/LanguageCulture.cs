using System.Globalization;

namespace LanMountainDesktop.Services;

/// <summary>
/// "语言码 → CultureInfo，码认不出时别抛"这件事的唯一写法。
///
/// 收口前全仓有五份抄本（<c>App.axaml.cs</c>、<c>SettingsViewModels.cs</c>、
/// <c>LauncherSettingsPageViewModel.cs</c>、<c>WeatherSettingsPageViewModel.cs</c> 各一份
/// <c>catch (CultureNotFoundException)</c>，<c>DailyArtworkWidget</c> 那份是 <c>catch { }</c> 兜住一切），
/// 其中两份逐字相同（重复普查里算一族）。<b>退哪一档是各场景自己的口径</b>：
/// 装界面语言要退"默认语言"（不能让界面掉进不变区域），格式化数字/日期退 <c>InvariantCulture</c>
/// （宁可看到 01/01/0001 也不要看到一半中文一半英文的混排），所以那一档由调用方传进来。
///
/// 三条实测到的语义边界，写在这里是因为它们决定了"什么时候真的会退档"：
/// <list type="bullet">
/// <item><description>空串 <b>不抛</b>：<c>GetCultureInfo("")</c> 直接给不变区域
/// （<c>Name=""/></c>、LCID 127）。也就是说调用方传的 fallback 对空码是<b>不起作用的</b>——
/// 今天五个调用点传进去的码都先过 <c>LanguageCodes.Normalize</c>（空码会被换成默认语言），
/// 所以这条够不到。别把它当"空码会退到我给的档"来依赖。</description></item>
/// <item><description>纯空白与不成形的码才抛 <c>CultureNotFoundException</c>（实测 <c>"   "</c>、
/// <c>"中文"</c> 都抛），退档分支走的就是这一支。</description></item>
/// <item><description><b>看着像标签的错码不抛、也不退档</b>：实测 <c>"not-a-real-tag-xx"</c> 成功，
/// 拿到的是 <c>Name="not"</c>（ICU 只认前一段当语言子标签，后面全丢）。
/// 所以"拼错语言码"的症状不是退档，而是安静地用一个不存在的档位——要防这件事得在码表那一头，
/// 不是这里（<c>LanguageCodes</c> 的家已在 #G1-AE 收过）。</description></item>
/// </list>
/// </summary>
internal static class LanguageCulture
{
    /// <summary>
    /// 认得出 <paramref name="languageCode"/> 就返回它的档位，抛 <see cref="CultureNotFoundException"/>
    /// 时返回调用方给的 <paramref name="fallback"/>。空串按 <c>GetCultureInfo</c> 的原义走（给不变区域），
    /// 这里刻意不做"空串也退档"的额外判断——那是改口径，不是收口。
    /// </summary>
    public static CultureInfo GetOrFallback(string languageCode, CultureInfo fallback)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageCode);
        }
        catch (CultureNotFoundException)
        {
            return fallback;
        }
    }
}
