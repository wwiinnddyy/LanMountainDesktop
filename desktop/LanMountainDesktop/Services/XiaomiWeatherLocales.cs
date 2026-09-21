using LanMountainDesktop.Shared.Contracts.Localization;

namespace LanMountainDesktop.Services;

/// <summary>
/// 小米天气接口 locale 参数的取值——供应商自己的拼法，不是 IETF 语言码。
/// 收口前 <c>en_us</c> / <c>zh_cn</c> 这两个拼法散在 3 份逐字相同的
/// <c>NormalizeWeatherLocale</c>（刷新服务、设置页视图模型、天气组件基类）与 1 处选项默认值里，
/// 一共 4 份。改一份剩下三份照旧：症状是设置页出来的天气是英文、桌面上那个组件还是中文，
/// 或者反过来，而两边都"看起来工作正常"。
/// </summary>
internal static class XiaomiWeatherLocales
{
    public const string English = "en_us";

    public const string Chinese = "zh_cn";

    /// <summary>
    /// 界面语言码 → 接口 locale：不是英文码就按中文走。
    /// 判据保持收口前的逐字比较（<see cref="LanguageCodes.IsEnglishCode"/>），没有顺手改成归一化。
    /// </summary>
    public static string ForLanguageCode(string? languageCode)
        => LanguageCodes.IsEnglishCode(languageCode) ? English : Chinese;
}
