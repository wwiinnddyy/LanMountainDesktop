using System;
using System.Reflection;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// <see cref="WinRtReflection"/> 的行为钉。
///
/// 钉的是"认不出来给 null、绝不抛"和"按名字 + 恰好一个参数挑方法"这两条——三处消费者
/// （定位、通知、播放状态）都拿 <c>null</c> 当"本机没这个能力"，所以这里一旦改成抛或改成
/// 挑错重载，症状就是那条能力要么崩、要么安静地没了。
/// 没钉的：本机真能找到 <c>AsStreamForRead</c> 与否——那取决于装在哪个 .NET 上跑，headless 里
/// 判不出来，别当成这条测试已经覆盖。
/// </summary>
public sealed class WinRtReflectionTests
{
    [Fact]
    public void ResolveStaticMethod_ToleratesAMissingType_WithoutThrowing()
    {
        Assert.Null(WinRtReflection.ResolveStaticMethod(null, "AsStreamForRead"));
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForAnUnknownName()
    {
        Assert.Null(WinRtReflection.ResolveProjectionType("No.Such.Projection.Type"));
        Assert.Null(WinRtReflection.ResolveWinRtType("Windows.No.Such.Type"));
    }

    [Fact]
    public void ResolveStaticMethod_PicksTheSingleParameterOverloadOfThatName()
    {
        var method = WinRtReflection.ResolveStaticMethod(typeof(Fixture), nameof(Fixture.Foo));

        Assert.NotNull(method);
        Assert.Equal("Foo", method!.Name);
        Assert.Single(method.GetParameters());
    }

    /// <summary>
    /// 干扰项是刻意拼出来的：<c>Bar(int)</c> 与 <c>Foo</c> 的目标形状只差名字，
    /// <c>Foo()</c> 与 <c>Foo(int, int)</c> 只差参数个数——两个判据各断掉时红的是同一条。
    /// </summary>
    private static class Fixture
    {
        public static int Bar(int value) => value;

        public static int Foo() => 0;

        public static int Foo(int first, int second) => first + second;

        public static int Foo(int value) => value;
    }
}
