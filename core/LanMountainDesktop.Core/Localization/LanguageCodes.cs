namespace LanMountainDesktop.Shared.Contracts.Localization;

/// <summary>
/// 界面语言码的取值集合、默认值与归一化表。这份契约是跨二进制的：<c>settings.json</c> 里的
/// <c>LanguageCode</c> 由宿主写、由启动器在拉起宿主之前读，两边各判一次——收口前宿主
/// <c>LocalizationService</c>、启动器 <c>LanguagePreferenceService</c> 与宿主侧
/// <c>ClockAirAppTimeFormatter</c> 里各有一份逐字相同的 switch，默认值另有 3 处绕开宿主那份直接写字面量。
/// 漂了的症状不是崩：启动器按自己那份把 <c>ko-KR</c> 判成 <c>zh-CN</c>，宿主起来后又显示成韩语，
/// 用户看到的就是"启动动画是中文、进桌面变韩文"。
/// </summary>
public static class LanguageCodes
{
    /// <summary>默认语言，也是读不到或读不懂设置时的退路。</summary>
    public const string Default = Chinese;

    public const string Chinese = "zh-CN";

    public const string English = "en-US";

    public const string Japanese = "ja-JP";

    public const string Korean = "ko-KR";

    /// <summary>设置页可选的语言，顺序即下拉框顺序。</summary>
    public static IReadOnlyList<string> Supported { get; } = [Chinese, English, Japanese, Korean];

    /// <summary>
    /// 把任意写法（大小写、带区域后缀与否、前后有空格）折成 <see cref="Supported"/> 里的一个；
    /// 认不出来就退回 <see cref="Default"/>。
    /// </summary>
    public static string Normalize(string? languageCode)
    {
        return languageCode?.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" => English,
            "ja" or "ja-jp" => Japanese,
            "ko" or "ko-kr" => Korean,
            "zh" or "zh-cn" => Chinese,
            _ => Default,
        };
    }

    /// <summary>当前语言是不是简体中文。</summary>
    public static bool IsChinese(string? languageCode) => Normalize(languageCode) == Chinese;

    /// <summary>
    /// 逐字比较是不是英文码，不做归一化。天气那三处判据从来就是直接比较，
    /// 收口径时不顺手改成归一化——那会让 <c>"en"</c> 这种写法从"按中文处理"变成"按英文处理"，
    /// 是对第三方接口出参的口径变更，不在这次的范围里。
    /// </summary>
    public static bool IsEnglishCode(string? languageCode)
        => string.Equals(languageCode, English, StringComparison.OrdinalIgnoreCase);
}
