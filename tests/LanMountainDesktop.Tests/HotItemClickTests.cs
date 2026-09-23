using Avalonia.Controls;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 点热搜条目那条判据的真值表：左键、sender 是挂点击的 <c>Border</c>、<c>Tag</c> 是能解析的下标、
/// 且落在列表范围内。这四条少任一条都不报错，只在用户手滑时看得见——少范围那一格是点最后一条越界抛，
/// 少左键那一格是中键也弹浏览器，所以逐条钉。
///
/// <c>Open</c>（真事件那一段）这里没钉：离线门里造不出真的 <c>PointerPressedEventArgs</c>，
/// 它只是把 <see cref="HotItemClick.TryGetIndex"/> 与真事件、真列表接起来，
/// "打开哪条"由组件自己的列表决定——这一点如实记下，不宣称整条链都覆盖。
/// </summary>
public sealed class HotItemClickTests
{
    [Fact]
    public void ValidTag_OnBorder_IsTheIndex()
    {
        var host = new Border { Tag = 2 };

        Assert.True(HotItemClick.TryGetIndex(host, leftButtonPressed: true, itemCount: 3, out var index));
        Assert.Equal(2, index);
    }

    [Fact]
    public void MiddleOrRightClick_IsNotAnItemLaunch()
    {
        var host = new Border { Tag = 0 };

        Assert.False(HotItemClick.TryGetIndex(host, leftButtonPressed: false, itemCount: 3, out var index));
        Assert.Equal(-1, index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(-1)]
    public void TagThatIsNotAnIndex_IsRejected(object? tag)
    {
        var host = new Border { Tag = tag };

        Assert.False(HotItemClick.TryGetIndex(host, leftButtonPressed: true, itemCount: 3, out var index));
        Assert.Equal(-1, index);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    public void IndexOutsideTheLoadedList_IsRejected(int tag, int itemCount)
    {
        // 上界是 itemCount：0 条时 0 也不许过；3 条时 2 是最后一条、要放过。
        var host = new Border { Tag = tag };

        Assert.False(HotItemClick.TryGetIndex(host, leftButtonPressed: true, itemCount, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void SenderThatIsNotTheClickableHost_IsRejected()
    {
        Assert.False(HotItemClick.TryGetIndex(new TextBlock { Tag = 0 }, true, 3, out var index));
        Assert.False(HotItemClick.TryGetIndex(null, true, 3, out index));
        Assert.Equal(-1, index);
    }
}
