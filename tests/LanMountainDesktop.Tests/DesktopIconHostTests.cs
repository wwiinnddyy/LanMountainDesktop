using System;
using System.Collections.Generic;

using LanMountainDesktop.Platform;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "桌面图标层是哪个窗口"那条两趟判据的家唯一的证据。
///
/// 为什么值得单独钉：此前两个桌面层服务各抄一份逐字相同的 35 行（连带两套 P/Invoke），
/// 抄歪的两种后果都不报错——只留一趟，壁纸窗没开 WorkerW 的机器上找不到宿主（窗口挂不上桌面）；
/// 两趟顺序反了，会把"顶层直挂的那层"当成宿主，窗口确实挂上了、但图标盖在它上面或被它盖住。
/// 判据与 Win32 取数是分开的：这里注入一个按类名查子的假函数，所以这两条错法在 Linux/CI 上也红。
/// 覆盖面边界：<c>Resolve()</c> 那个真调 <c>EnumWindows</c>/<c>FindWindowEx</c> 的重载这里量不到
/// （headless 没有真桌面窗口）——它只钉"取数怎么取"，判据一格不落在它身上。
/// </summary>
public sealed class DesktopIconHostTests
{
    private static readonly IntPtr Progman = new(0x1000);
    private static readonly IntPtr WorkerW = new(0x2000);
    private static readonly IntPtr NestedDefView = new(0x3000);
    private static readonly IntPtr DirectDefView = new(0x4000);

    [Fact]
    public void Resolve_PrefersTheDefViewNestedUnderWorkerW()
    {
        // 这个夹具是"两种形状同时成立"的对照写法：平时 DefView 直挂在 Progman 下（形状 a），
        // 开了幻灯壁纸时它被搬进 Progman 的 WorkerW 里（形状 c）。真机上一次只出现一种，
        // 但要把"先嵌套、后直挂"这条顺序钉住，必须让两边都有值——否则两趟顺序反了也不红。
        // 挂错层的症状不报错：窗口确实挂上了，桌面图标却盖在它上面（或被它盖住）。
        var fake = new FakeDesktop(
            (Progman, "WorkerW", WorkerW),
            (WorkerW, "SHELLDLL_DefView", NestedDefView),
            (Progman, "SHELLDLL_DefView", DirectDefView));

        var host = DesktopIconHost.ResolveFrom([Progman], fake.Find);

        Assert.Equal(NestedDefView, host);
    }

    [Fact]
    public void Resolve_FallsBackToTheTopLevelDefView_WhenNoWorkerWHasOne()
    {
        var fake = new FakeDesktop(
            (Progman, "WorkerW", WorkerW),
            (WorkerW, "SHELLDLL_DefView", IntPtr.Zero),
            (Progman, "SHELLDLL_DefView", DirectDefView));

        var host = DesktopIconHost.ResolveFrom([Progman], fake.Find);

        // WorkerW 存在但里面没有图标层（壁纸窗自己占了它）：这时顶层直挂的那个才是宿主。
        Assert.Equal(DirectDefView, host);
    }

    [Fact]
    public void Resolve_KeepsLookingThroughLaterWindows_ForTheNestedHost()
    {
        var second = new IntPtr(0x5000);
        var secondWorker = new IntPtr(0x6000);
        var secondDefView = new IntPtr(0x7000);
        var fake = new FakeDesktop(
            (Progman, "WorkerW", WorkerW),
            (WorkerW, "SHELLDLL_DefView", IntPtr.Zero),
            (second, "WorkerW", secondWorker),
            (secondWorker, "SHELLDLL_DefView", secondDefView));

        Assert.Equal(secondDefView, DesktopIconHost.ResolveFrom([Progman, second], fake.Find));
    }

    [Fact]
    public void Resolve_ReturnsZero_WhenNeitherPassFindsAnything()
    {
        // 拿不到宿主时两个调用方的判据一律是"0 就跳过这次挂接"，在这里抛出去只会把一次刷新变成崩溃。
        var fake = new FakeDesktop();

        Assert.Equal(IntPtr.Zero, DesktopIconHost.ResolveFrom([Progman, WorkerW], fake.Find));
    }

    [Fact]
    public void Resolve_AsksByClassNames_InTheTwoPassOrder()
    {
        // 钉的是类名拼写与问的顺序：类名写成 "Workerw"/"SHELLDLL_Defview" 这种大小写或漏字母的错法，
        // 在这台机器上就是"永远找不到宿主"，而它在真机上不报错——只有注入的假函数会红。
        var fake = new FakeDesktop((Progman, "SHELLDLL_DefView", DirectDefView));

        Assert.Equal(DirectDefView, DesktopIconHost.ResolveFrom([Progman], fake.Find));

        List<(string Parent, string Class)> expected =
            [("Progman", "WorkerW"), ("Progman", "SHELLDLL_DefView")];
        Assert.Equal(expected, fake.Asked);
    }

    [Fact]
    public void Resolve_EmptyWindowList_IsZero()
    {
        Assert.Equal(IntPtr.Zero, DesktopIconHost.ResolveFrom([], new FakeDesktop().Find));
    }

    private sealed class FakeDesktop
    {
        private readonly Dictionary<(IntPtr Parent, string Class), IntPtr> _children;

        public FakeDesktop(params (IntPtr Parent, string Class, IntPtr Child)[] children)
        {
            _children = new Dictionary<(IntPtr, string), IntPtr>();
            foreach (var (parent, clazz, child) in children)
            {
                _children[(parent, clazz)] = child;
            }
        }

        /// <summary>家问过的 (父窗口, 类名)，按顺序记；父窗口用名字回显，测试里好看。</summary>
        public List<(string Parent, string Class)> Asked { get; } = new();

        public IntPtr Find(IntPtr parent, string? className)
        {
            var name = parent == Progman ? "Progman" : parent == WorkerW ? "WorkerW" : parent.ToString();
            Asked.Add((name, className ?? string.Empty));

            return _children.GetValueOrDefault((parent, className ?? string.Empty));
        }
    }
}
