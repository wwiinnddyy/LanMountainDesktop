using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace LanMountainDesktop.Views.ComponentEditors;

/// <summary>
/// 秒针走法那一组二选一的互斥规则，唯一一份（两个按钮在 XAML 里是 ToggleButton 那一对）。时钟与世界时钟两个编辑面板此前各抄一份
/// 逐字相同的 23 行（各自带一份 <c>_suppressEvents</c> 重入闸）。
///
/// 这段逻辑里每一件都不许少，而且少哪一件都不报错：
/// ① 重入闸——改一个按钮的 <c>IsChecked</c> 会再触发一次 <c>Checked</c>，没闸就是自己 recursion 自己；
/// ② 点谁谁独占——两个都勾上时，画面上看着是"二选一"，存的却是自相矛盾的一对值；
/// ③ 两个都被取消时退回"跳秒"——这条兜底是给用户用鼠标点在已勾选项上那种"全空"状态的，
///    少了它，界面上就没有任何一档被选中，而保存出去的仍是旧值，看着像"改了没反应"。
/// </summary>
internal static class SecondHandChoice
{
    public static void Enforce(ToggleButton tick, ToggleButton sweep, object? sender, ref bool suppressEvents)
    {
        if (suppressEvents)
        {
            return;
        }

        suppressEvents = true;

        if (sender == tick)
        {
            tick.IsChecked = true;
            sweep.IsChecked = false;
        }
        else if (sender == sweep)
        {
            sweep.IsChecked = true;
            tick.IsChecked = false;
        }

        if (tick.IsChecked != true && sweep.IsChecked != true)
        {
            tick.IsChecked = true;
        }

        suppressEvents = false;
    }
}
