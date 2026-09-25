using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 忙态画法的钉（家在 <see cref="ComponentBusyVisual"/>）。
///
/// 收口前七个组件各写两份语句（<c>IsEnabled</c> 一行、<c>Opacity</c> 一行），实测有 3 家把这两行的
/// 判据写得不一样——其中两家的形状是"按钮禁用了却完全不淡"。这里既钉住"同判据"这个默认，
/// 也钉住"两个判据分开"这个逃生口：分开必须是显式的，而不是七份抄本里谁也没注意到。
/// </summary>
public sealed class ComponentBusyVisualTests
{
    [Fact]
    public void Apply_WithOnePredicate_DimsExactlyWhenItDisables()
    {
        var button = new Button();

        ComponentBusyVisual.Apply(button, enabled: false, dimmedOpacity: 0.65);
        Assert.False(button.IsEnabled);
        Assert.Equal(0.65, button.Opacity);

        ComponentBusyVisual.Apply(button, enabled: true, dimmedOpacity: 0.65);
        Assert.True(button.IsEnabled);
        Assert.Equal(1d, button.Opacity);
    }

    [Fact]
    public void Apply_WithSeparatePredicates_KeepsTheTwoAparts()
    {
        var button = new Button();

        // 收口前 Cnr 与每日一词 1x1 就是这个形状（忙→禁用，但只有"没上台面"才淡），原样保留。
        ComponentBusyVisual.Apply(button, enabled: false, dimmed: false, dimmedOpacity: 0.6);
        Assert.False(button.IsEnabled);
        Assert.Equal(1d, button.Opacity);

        ComponentBusyVisual.Apply(button, enabled: true, dimmed: true, dimmedOpacity: 0.6);
        Assert.True(button.IsEnabled);
        Assert.Equal(0.6, button.Opacity);
    }

    [Fact]
    public void Fade_DimsWithoutTouchingInteractivity()
    {
        var glyph = new TextBlock();

        ComponentBusyVisual.Fade(glyph, dimmed: true, dimmedOpacity: 0.56);
        Assert.Equal(0.56, glyph.Opacity);

        ComponentBusyVisual.Fade(glyph, dimmed: false, dimmedOpacity: 0.56);
        Assert.Equal(1d, glyph.Opacity);
    }

    /// <summary>淡到哪一档是各组件自己的视觉口径（实测五档并存），家不许替它们统一，所以换档必须看得出来。</summary>
    [Theory]
    [InlineData(0.56)]
    [InlineData(0.58)]
    [InlineData(0.60)]
    [InlineData(0.65)]
    [InlineData(0.85)]
    public void Apply_UsesTheCallersOwnDimValue(double dimmedOpacity)
    {
        var button = new Button();

        ComponentBusyVisual.Apply(button, enabled: false, dimmedOpacity: dimmedOpacity);

        Assert.Equal(dimmedOpacity, button.Opacity);
    }
}
