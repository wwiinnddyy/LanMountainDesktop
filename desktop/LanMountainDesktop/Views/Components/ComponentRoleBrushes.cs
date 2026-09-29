using Avalonia.Controls;
using Avalonia.Media;

using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件里"文字角色色"只有一个取法：问主题层要那支 <c>Adaptive*</c> 画笔。
///
/// 烧的第一族是状态文字。2026-09-29 之前它有 10 处各写一遍
/// <c>new SolidColorBrush(_isNightVisual ? Color.Parse(『#8B95A5』) : Color.Parse(『#6A6F77』))</c>
/// ——7 个组件逐字相同，另外几处日档还各自漂开（『#7A818E』；<c>NotificationBoxWidget</c> 那两处
/// 连日档都没有，浅色主题下就是一支灰字压在白底上）。
/// 这里的色值一律用全角括号而不是直引号包着：<c>ColorLiteralRatchetTests</c> 数的是引号内的十六进制串，
/// 注释里写一份就等于账上多两处。
///
/// <b>为什么不能只把那两个数统一成一对常量</b>：主题层的 <c>textMuted</c> 是按当前
/// <c>surfaceRaised</c> 混色之后再 <c>ColorMath.EnsureContrast</c> 算出来的
/// （<c>ThemeColorSystemService.cs:124</c>），组件里写死三元组等于绕过整套对比度保证——
/// 换一张浅色壁纸时，"夜里那支亮灰字"会直接压在亮底上看不清。
///
/// 兜底（拿不到键时给中性灰）留在家里而不是逐组件给，因为这 10 处口径本来就相同。
/// 别把它当"主题没注册也没事"的通行证：那件事由 <c>CapabilityEntryPointTests</c> 的键覆盖探针管。
/// </summary>
internal static class ComponentRoleBrushes
{
    private static readonly IBrush MutedTextFallback = new SolidColorBrush(Colors.Gray);

    /// <summary>状态行、"正在加载…"、时间戳这类提示性文字的颜色。</summary>
    internal static IBrush MutedText(IResourceHost host) =>
        AdaptiveTokens.Brush(host, ThemeResourceKeys.TextMutedBrush, MutedTextFallback);

    /// <summary>
    /// 面板上的标题 / 正文 / 与正文同色的图标字形。
    /// 第二族（2026-09-29）：此前 17 处各写一遍 <c>_isNightVisual ? 夜档 : 日档</c>，
    /// 夜档全是同一支 <c>#E8EAED</c>，日档却各自漂开成 6 个不同的近黑值
    /// （『#202327』『#2B2F35』『#11151D』『#141922』『#151922』『#20232A』）——
    /// 同一个"正文"在不同组件里是 6 种深浅，且四种都不随壁纸走。
    /// </summary>
    /// <remarks>
    /// 主题层算 textPrimary 时兜的是 <c>surfaceRaised</c> 的对比度（ThemeColorSystemService.cs:117），
    /// 而有些组件的文字压在 <c>surfaceOverlay</c> 或自家渐变底上——这一族（以及次要、状态两族）
    /// 都继承了同一个近似，不是这次改动新引入的；真要精确，得让主题层按"实际底"出文字色，另说。
    /// </remarks>
    internal static IBrush PrimaryText(IResourceHost host) =>
        AdaptiveTokens.Brush(host, ThemeResourceKeys.TextPrimaryBrush, MutedTextFallback);

    /// <summary>
    /// 次要文字与刷新/搜索这类图标字形（比正文淡一档，但仍要读得清）。
    /// 第三族（2026-09-29）：夜档 17 处全是同一支 <c>#A8B1C2</c>，日档漂成
    /// 『#5E6671』『#5A6069』『#6B7078』『#7A8088』『#8A9099』『#626870』『#A4A9B2』『#B2B7C0』
    /// 『#4A5466』『#646C79』『#7A7F89』共 11 个值。
    /// </summary>
    internal static IBrush SecondaryText(IResourceHost host) =>
        AdaptiveTokens.Brush(host, ThemeResourceKeys.TextSecondaryBrush, MutedTextFallback);

    /// <summary>
    /// 组件自己的那层面板底（根框与内层卡片——今天这两层用的是同一个值）。
    /// 第四族（2026-09-29）：9 处各写一遍夜档 <c>#1B2129</c>、日档 <c>#FCFCFD</c>/<c>#FCFBFA</c>/
    /// <c>#ECEFF3</c>/<c>#F4F5F7</c>。选 <c>surfaceRaised</c> 而不是随手挑一层，理由要说清：
    /// 主题那三个文字角色的对比度全是**按 raised 算的**（ThemeColorSystemService.cs:117-124），
    /// 而这一层上正好放的就是那些文字——底与字用同一块基准，那道保证才真的成立。
    /// </summary>
    internal static IBrush RaisedSurface(IResourceHost host) =>
        AdaptiveTokens.Brush(host, ThemeResourceKeys.SurfaceRaisedBrush, MutedTextFallback);



    /// <summary>
    /// 压在面板/卡片之上的那一层控件底（刷新按钮、搜索框、录音的存与弃按钮、头像占位格）。
    /// 第五族（2026-09-29）：夜档 10 处全是 <c>#2D3440</c>，日档 6 个值
    /// 『#EFF1F5』『#ECF2FA』『#EEF1F4』『#14A0A6AF』『#F5F5F5』『#F8FAFD』『#F7F8FA』。
    /// 映射到 <c>surfaceOverlay</c> 的依据不是"值接近"，而是阶梯方向：
    /// 今天芯片夜里比卡片亮、白天比卡片暗，而 <c>SurfaceLadder_KeepsTheControlReadableOnTheCard</c>
    /// 实测主题的 raised/overlay 正是同一个方向（第三族落地时就钉过）。
    /// </summary>
    internal static IBrush OverlaySurface(IResourceHost host) =>
        AdaptiveTokens.Brush(host, ThemeResourceKeys.SurfaceOverlayBrush, MutedTextFallback);
}
