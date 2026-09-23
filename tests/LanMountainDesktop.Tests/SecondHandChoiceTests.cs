using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.Views.ComponentEditors;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 秒针走法二选一这条判据的行为钉。两个时钟编辑面板此前各抄一份逐字 23 行，
/// 少哪一件都不报错：没重入闸会自己 recursion 自己；互斥没做就是"看着二选一、存的是一对矛盾值"；
/// 兜底没做就是界面上谁都没选中而保存的仍是旧值（看着像"改了没反应"）。
/// </summary>
public sealed class SecondHandChoiceTests
{
    [AvaloniaFact]
    public void Enforce_PickingTick_ClearsSweep()
    {
        var (tick, sweep) = Pair(out var suppress);

        SecondHandChoice.Enforce(tick, sweep, tick, ref suppress);

        Assert.Equal(true, tick.IsChecked);
        Assert.NotEqual(true, sweep.IsChecked);
    }

    [AvaloniaFact]
    public void Enforce_PickingSweep_ClearsTick()
    {
        var (tick, sweep) = Pair(out var suppress);
        tick.IsChecked = true;

        SecondHandChoice.Enforce(tick, sweep, sweep, ref suppress);

        Assert.Equal(true, sweep.IsChecked);
        Assert.NotEqual(true, tick.IsChecked);
    }

    [AvaloniaFact]
    public void Enforce_NobodyPickedAndNothingChecked_FallsBackToTick()
    {
        var (tick, sweep) = Pair(out var suppress);

        SecondHandChoice.Enforce(tick, sweep, sender: null, ref suppress);

        Assert.Equal(true, tick.IsChecked);
    }

    /// <summary>闸还开着时整段跳过——这条红的条件是"事件自己再触发一次自己"。</summary>
    [AvaloniaFact]
    public void Enforce_WhileTheReentrancyGateIsHeld_TouchesNothing()
    {
        var tick = new ToggleButton();
        var sweep = new ToggleButton { IsChecked = true };
        var suppress = true;

        SecondHandChoice.Enforce(tick, sweep, tick, ref suppress);

        Assert.False(tick.IsChecked);
        Assert.Equal(true, sweep.IsChecked);
        Assert.True(suppress);
    }

    private static (ToggleButton Tick, ToggleButton Sweep) Pair(out bool suppress)
    {
        suppress = false;
        return (new ToggleButton(), new ToggleButton());
    }
}
