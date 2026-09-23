using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "设置变了之后重刷卡片"那三步的钉：作废缓存 → 重读本组件的自动刷新参数 → 只在已挂载时强制刷一次。
/// 三步记成一条顺序流水（<see cref="Steps"/>)，所以**顺序本身可测**：把作废挪到刷新之后，
/// 或者干脆不调它，<see cref="AfterSettingsChange_InvalidatesTheCache_First"/> 就红 ——
/// 这正是这一族的真实症状（刷回来的还是旧缓存，下一次自动刷新又盖回去，看着像偶发）。
///
/// <c>applySettings</c> 传 <c>null</c> 的那一格钉的是"没有要重读参数的组件也得走同一条流程"
/// （画作卡片就是这种：只作废 + 刷）。未挂载那一格同时钉住"作废照做、只是不刷"——
/// 设置确实变了，缓存不能留着。
/// </summary>
public sealed class RecommendationSettingsRefreshTests
{
    private readonly List<string> Steps = [];

    [Fact]
    public void AfterSettingsChange_InvalidatesTheCache_First()
    {
        Run(attached: true, applySettings: true);

        Assert.Equal(["cache", "apply", "refresh"], Steps);
    }

    [Fact]
    public void AfterSettingsChange_WithoutSettingsToReapply_StillInvalidatesAndRefreshes()
    {
        Run(attached: true, applySettings: false);

        Assert.Equal(["cache", "refresh"], Steps);
    }

    [Fact]
    public void AfterSettingsChange_WhileDetached_InvalidatesButDoesNotRefresh()
    {
        Run(attached: false, applySettings: true);

        Assert.Equal(["cache", "apply"], Steps);
    }

    [Fact]
    public void AfterSettingsChange_SynchronousRefreshFailure_StillPropagates()
    {
        Assert.Throws<InvalidOperationException>(() => RecommendationServiceBinding.AfterSettingsChange(
            () => Steps.Add("cache"),
            null,
            () => true,
            () => throw new InvalidOperationException()));

        Assert.Equal(["cache"], Steps);
    }

    private void Run(bool attached, bool applySettings)
    {
        RecommendationServiceBinding.AfterSettingsChange(
            () => Steps.Add("cache"),
            applySettings ? () => Steps.Add("apply") : null,
            () => attached,
            () =>
            {
                Steps.Add("refresh");
                return Task.CompletedTask;
            });
    }
}
