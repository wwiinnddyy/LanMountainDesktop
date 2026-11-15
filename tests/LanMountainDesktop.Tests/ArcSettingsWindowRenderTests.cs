using System;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LanMountainDesktop.Views;

using TKCode.Arc;

using ArcButton = TKCode.Arc.Button;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 设置窗口的运行时渲染守卫（§10.1 教训：XAML 编译过 ≠ 运行时有画面）。
///
/// B 方案落地初期只断言「12 个静态导航项 + 7 张概念卡片」确实画出来了；§11.3 把功能补齐后，
/// 窗口改成「数据驱动外壳 + 真实设置页」，判据随之升级为：导航项来自真实注册表、内容区真的
/// 加载了一个设置页 UserControl、页头 / 搜索框 / 底部操作栏 / 真实 Arc 控件都在可视化树里。
/// 仍然直接 new 生产窗口 + Show + 泵渲染帧，专治「整窗空白」复发。
/// </summary>
public sealed class ArcSettingsWindowRenderTests
{
    [AvaloniaFact]
    public void ArcSettingsWindow_RendersFunctionalSettingsShell()
    {
        var window = new ArcSettingsWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();

        // 1) 导航项由 SettingsPageRegistry 生成（不再是写死的静态 Border），且每项都挂着真实 pageId。
        var navItems = window.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("nav-item"))
            .ToArray();
        Assert.True(navItems.Length >= 12, $"侧栏导航项没渲染出来（实测 {navItems.Length} 个）");
        Assert.All(navItems, border => Assert.True(border.Tag is string, "导航项没有绑定真实 pageId"));
        Assert.True(window.ViewModel.Pages.Count >= 12, "注册表里没有足够的真实设置页");

        // 2) 内容区真的承载了一个设置页 UserControl，而不是空白。
        // 只按 Content is Control 找会命中模板内部的 Panel（按钮 / Arc 控件模板），
        // 因此精确挑出承载 UserControl 的那个 ContentControl（即 ContentFrame）。
        var hostedPage = window.GetVisualDescendants()
            .OfType<ContentControl>()
            .Select(host => host.Content)
            .OfType<UserControl>()
            .FirstOrDefault();
        Assert.NotNull(hostedPage);

        // 3) 页头大标题来自当前页的 ViewModel。
        var title = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(block => block.Classes.Contains("page-title"));
        Assert.NotNull(title);
        Assert.False(string.IsNullOrWhiteSpace(title!.Text));

        // 4) 搜索框与底部操作栏（保存 / 重置）都真实存在。
        Assert.NotEmpty(window.GetVisualDescendants().OfType<AutoCompleteBox>());
        Assert.True(
            window.GetVisualDescendants().OfType<ArcButton>().Count() >= 2,
            "底部操作栏按钮没渲染出来");

        // 5) 真实设置页里的 Arc 控件（Select / Switch）确实落到了可视化树。
        // 这条同时守住 §10.1 的教训：若测试宿主漏注册 .UseArcBlocks()，Blocks 控件"有对象无模板"、
        // SettingsRow 的子控件根本不进可视化树，这里会立刻红，而不是静默假绿。
        Assert.NotEmpty(window.GetVisualDescendants().OfType<Select>());
        Assert.NotEmpty(window.GetVisualDescendants().OfType<Switch>());

        window.Close();
    }

    /// <summary>
    /// 行为守卫（不只是"画出来了"）：走一遍 ReloadPages → NavigateTo → GetOrCreatePage →
    /// ContentFrame → TrySelectNavigationItem 的整条链路，确认换页、VM 页头、导航选中态
    /// 与搜索索引都真的联动。这也是 SettingsWindowService.Open(request.PageId) 最终落到的方法。
    /// </summary>
    [AvaloniaFact]
    public void ArcSettingsWindow_NavigatesToRequestedPageAndSelectsItsNavItem()
    {
        var window = new ArcSettingsWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 5; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();

        var initialPageId = window.ViewModel.CurrentPageId;
        Assert.False(string.IsNullOrWhiteSpace(initialPageId), "首屏应默认落在第一页");
        Assert.True(
            window.ViewModel.SearchResults.Count >= window.ViewModel.Pages.Count,
            "搜索索引应至少为每个真实页建一条页级结果");

        var target = window.ViewModel.Pages.First(page =>
            !string.Equals(page.PageId, initialPageId, StringComparison.OrdinalIgnoreCase));

        window.ReloadPages(target.PageId);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(target.PageId, window.ViewModel.CurrentPageId);
        Assert.Equal(target.Title, window.ViewModel.CurrentPageTitle);

        var hosted = window.GetVisualDescendants()
            .OfType<ContentControl>()
            .Select(host => host.Content)
            .OfType<UserControl>()
            .FirstOrDefault();
        Assert.NotNull(hosted);

        var selectedNav = window.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border =>
                border.Classes.Contains("nav-item") && border.Classes.Contains("selected"));
        Assert.NotNull(selectedNav);
        Assert.Equal(target.PageId, selectedNav!.Tag as string);

        window.Close();
    }

    /// <summary>ISettingsPageHostContext 的抽屉开合真的改 VM 状态并把内容挂进可视化树。</summary>
    [AvaloniaFact]
    public void ArcSettingsWindow_HostContextDrawerOpensAndCloses()
    {
        var window = new ArcSettingsWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.OpenDrawer(new TextBlock { Text = "抽屉内容" }, "详情");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.IsDrawerOpen);
        Assert.Equal("详情", window.ViewModel.DrawerTitle);
        Assert.NotNull(window.GetVisualDescendants()
            .OfType<ContentControl>()
            .FirstOrDefault(host => host.Content is TextBlock { Text: "抽屉内容" }));

        window.CloseDrawer();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ViewModel.IsDrawerOpen);
        Assert.Null(window.ViewModel.DrawerTitle);

        window.Close();
    }
}
