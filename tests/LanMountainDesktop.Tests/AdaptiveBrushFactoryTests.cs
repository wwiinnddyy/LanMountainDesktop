using System.Collections.Generic;

using Avalonia.Media;

using LanMountainDesktop.Theme;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 7 个学习组件此前各自抄了同一段"在半透明面板上挑一个读得清的前景色"，
/// 其中 1 份还是自己写的单遍循环变体。这段策略合到 <see cref="AdaptiveBrushFactory"/> 后由这里钉住语义。
/// </summary>
public sealed class AdaptiveBrushFactoryTests
{
    private static readonly IReadOnlyList<Color> DarkPanel = [Color.Parse("#FF0B1220")];

    private static readonly IReadOnlyList<Color> LightPanel = [Color.Parse("#FFF1F5FA")];

    [Fact]
    public void Pick_ReturnsFirstCandidateThatMeetsContrastOnEverySample()
    {
        // #4B5565 在深色面板上只有约 2.4 的对比度，够不到 4.5；#F8FAFC 才够。
        var candidates = new[] { Color.Parse("#FF4B5565"), Color.Parse("#FFF8FAFC"), Color.Parse("#FFFFFFFF") };

        var picked = AdaptiveBrushFactory.Pick(DarkPanel, candidates, minContrast: 4.5);

        Assert.Equal(Color.Parse("#FFF8FAFC"), picked);
    }

    [Fact]
    public void Pick_FallsBackToHighestContrastInsteadOfGivingUp()
    {
        var candidates = new[] { Color.Parse("#FF6B7280"), Color.Parse("#FF8A94A6"), Color.Parse("#FF7C8494") };

        var picked = AdaptiveBrushFactory.Pick(LightPanel, candidates, minContrast: 12d);

        var pickedContrast = ColorMath.MinContrastRatio(picked, LightPanel);
        foreach (var candidate in candidates)
        {
            Assert.True(pickedContrast >= ColorMath.MinContrastRatio(candidate, LightPanel));
        }
    }

    [Fact]
    public void Pick_UsesWhiteWhenThereIsNoCandidate()
    {
        Assert.Equal(Color.Parse("#FFFFFFFF"), AdaptiveBrushFactory.Pick(DarkPanel, [], minContrast: 4.5));
    }

    [Fact]
    public void Create_KeepsThePickedColorOpaqueEnoughToReadOnGlass()
    {
        var brush = AdaptiveBrushFactory.Create([.. DarkPanel, .. LightPanel], [Color.Parse("#FFF8FAFC")], 4.5d);

        Assert.Equal(Color.Parse("#FFF8FAFC"), brush.Color);
    }
}
