namespace LanMountainDesktop.DesktopEditing;

/// <summary>
/// 桌面网格密度的取值范围与默认值。设置页的滑杆、运行期的读取、编辑态的钳制三处都得用同一套数，
/// 否则症状是"滑杆能拖到的值，网格根本不支持"或反过来——用户把密度拖到头却没变化。
/// 收口前这 6 个数在 <c>MainWindow</c>（含它的 partial 分片）、<c>FusedDesktopEditGridAdapter</c>
/// 与 <c>AppSettingsSnapshot</c> 的默认值里各写了一份，滑杆还在 <c>ComponentsSettingsPage.axaml</c>
/// 里抄了第四份 6/96 与 0/30；现在量程经 <c>ComponentsSettingsPageViewModel</c> 从这里读。
/// </summary>
internal static class DesktopGridLimits
{
    /// <summary>短边格子数下限。</summary>
    public const int MinShortSideCells = 6;

    /// <summary>短边格子数上限。</summary>
    public const int MaxShortSideCells = 96;

    /// <summary>没存过或存了非法值时的密度。</summary>
    public const int DefaultShortSideCells = 12;

    /// <summary>边缘留白下限（占短边百分比）。</summary>
    public const int MinEdgeInsetPercent = 0;

    /// <summary>边缘留白上限（占短边百分比）。</summary>
    public const int MaxEdgeInsetPercent = 30;

    /// <summary>没存过时的边缘留白。</summary>
    public const int DefaultEdgeInsetPercent = 18;
}
