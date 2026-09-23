using Avalonia.Headless.XUnit;
using Avalonia.Media;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "在盒子里塞得下的最大字号"那条二分判据的行为钉。三个每日组件此前各抄一份逐字相同的 38 行，
/// 并成 <see cref="ComponentTypography.FitFontSize"/> 一份。
///
/// 抄本各留一条命时的错法都不是崩溃，而是"看着不对"：轮数改小会在大盒子里给出偏小的字号；
/// 舍入余量抹掉会让"刚好放得下"那一档被判成放不下（掉一级）；地板改低于 6 磅就读不了了；
    /// 本轮钉住其中三格（带内、盒子变大不变小、行数放宽不变小）+ 地板与反向带各一格；
    /// "空文本按空格量"那格在 headless 量不出可观察差别（实测空格与长文同盒同字号），故未钉，见家注释。
/// </summary>
public sealed class ComponentTypographyFitTests
{
    private const string LongText =
        "床前明月光，疑是地上霜。举头望明月，低头思故乡。——这是一段故意写长的课文，用来把盒子撑满。";

    [AvaloniaFact]
    public void FitFontSize_ResultStaysWithinTheRequestedBand()
    {
        var size = ComponentTypography.FitFontSize(
            LongText, 180, 90, maxLines: 3, minFontSize: 10, maxFontSize: 22,
            FontWeight.Normal, 1.35);

        Assert.InRange(size, 10, 22);
    }

    [AvaloniaFact]
    public void FitFontSize_GivesABiggerFontWhenTheBoxGrows()
    {
        // 正对照：判据若退化成"永远回上限"或"永远回地板"，这一格和上一格不可能同时绿。
        var narrow = ComponentTypography.FitFontSize(
            LongText, 90, 60, 1, 10, 22, FontWeight.Normal, 1.35);
        var wide = ComponentTypography.FitFontSize(
            LongText, 520, 300, 6, 10, 22, FontWeight.Normal, 1.35);

        Assert.True(narrow < wide, $"narrow={narrow} 不该 ≥ wide={wide}");
        Assert.InRange(narrow, 10, 22);
        // 实测改正：二分的返回值永远**略低于**上限（18 轮是渐近逼近，`best` 取最后一次放得下的候选），
        // 所以这里不许写成 Equal(22)——那样期望是我猜的。钉"贴着上限"用区间。
        Assert.InRange(wide, 21, 22);
    }

    [AvaloniaFact]
    public void FitFontSize_AllowingMoreLinesNeverShrinksTheFont()
    {
        var oneLine = ComponentTypography.FitFontSize(LongText, 260, 200, 1, 10, 22, FontWeight.Normal, 1.35);
        var fourLines = ComponentTypography.FitFontSize(LongText, 260, 200, 4, 10, 22, FontWeight.Normal, 1.35);

        Assert.True(oneLine <= fourLines, $"1 行 {oneLine} 不该 > 4 行 {fourLines}");
    }

    [AvaloniaFact]
    public void FitFontSize_KeepsASixPointFloor_AndClampsAnInvertedBand()
    {
        var floored = ComponentTypography.FitFontSize(
            LongText, 20, 12, 1, minFontSize: 2, maxFontSize: 5, FontWeight.Normal, 1.3);

        Assert.True(floored >= 6, $"地板破了：{floored}");

        // min > max 是调用方写错，判据取"以 min 为准"而不是给个负区间让二分乱走。
        var inverted = ComponentTypography.FitFontSize(
            LongText, 200, 100, 2, minFontSize: 30, maxFontSize: 10, FontWeight.Normal, 1.3);

        Assert.Equal(30, inverted);
    }
}
