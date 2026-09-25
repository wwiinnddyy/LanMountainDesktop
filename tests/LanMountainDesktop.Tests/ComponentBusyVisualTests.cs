using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 忙态画法的钉（家在 <see cref="ComponentBusyVisual"/>）。
///
/// 收口前七个组件各写两份语句（<c>IsEnabled</c> 一行、<c>Opacity</c> 一行），实测三家的这两行
/// 判据不一样，形状是"按钮禁用了却完全不淡"；淡出值还漂成五档。2026-09-26 两件事都定了：
/// 淡出判据＝禁用判据（逃生口连重载一起删），淡出值只剩家里的一个常量。
/// 调用点不许再传数值，这条由 <c>SourceIntegrityTests.BusyVisualDimValue_LivesInTheHomeOnly</c> 拦。
/// </summary>
public sealed class ComponentBusyVisualTests
{
    /// <summary>档位改动必须经过这里：数值写在两处就会漂回五档。</summary>
    [Fact]
    public void DimmedOpacity_IsTheSingleHomeValue()
    {
        Assert.Equal(0.60, ComponentBusyVisual.DimmedOpacity);
    }

    [Fact]
    public void Apply_DimsExactlyWhenItDisables()
    {
        var button = new Button();

        ComponentBusyVisual.Apply(button, enabled: false);
        Assert.False(button.IsEnabled);
        Assert.Equal(ComponentBusyVisual.DimmedOpacity, button.Opacity);

        ComponentBusyVisual.Apply(button, enabled: true);
        Assert.True(button.IsEnabled);
        Assert.Equal(1d, button.Opacity);
    }

    [Fact]
    public void Fade_DimsWithoutTouchingInteractivity()
    {
        var glyph = new TextBlock { IsEnabled = false };

        ComponentBusyVisual.Fade(glyph, dimmed: true);
        Assert.Equal(ComponentBusyVisual.DimmedOpacity, glyph.Opacity);

        ComponentBusyVisual.Fade(glyph, dimmed: false);
        Assert.Equal(1d, glyph.Opacity);
        Assert.False(glyph.IsEnabled);
    }
}
