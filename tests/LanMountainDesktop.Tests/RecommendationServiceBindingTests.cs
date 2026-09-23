using System;
using System.Threading.Tasks;

using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件被下推推荐信息服务那两步的行为钉：换上去的必须是给的那个（给 <c>null</c> 才回落到自己的默认实例），
/// 且**只有已经挂上桌面才刷**、刷的时候字段已经换好。
///
/// 顺序这一格是重点：<see cref="Holder.SeenDuringRefresh"/> 记的是刷新那一刻看到的字段值。
/// 把家改成"先刷后换"，它就退回默认实例（实测红），而这正是组件侧最难看的错法——
/// 刷出来的还是旧服务的数据，界面停在旧卡片上，看着像"服务没生效"，还通常要等下次语言/时区变更才自愈。
/// 挂载条件同理：去掉 <c>isAttached()</c> 那一步，未挂载的组件也会去刷（实测红），而它那时还没准备好被刷。
///
/// 家刻意保留抄本的两个语义，不顺手改良：刷新仍是"发出去不管"，所以**同步抛出的异常照旧往上走**（钉住）；
/// 默认实例仍由各组件自己持有（这里收的是写法，不是那十个实例的归属）。
/// </summary>
public sealed class RecommendationServiceBindingTests
{
    [Fact]
    public void NullService_FallsBackToTheComponentsOwnDefault()
    {
        var holder = new Holder();

        holder.Set(null);

        Assert.Same(holder.Default, holder.Service);
        Assert.Equal(0, holder.RefreshCount);
    }

    [Fact]
    public void WhileAttached_SwapsFirst_ThenRefreshesOnce()
    {
        var holder = new Holder { Attached = true };
        var next = new RecommendationDataService();

        holder.Set(next);

        Assert.Same(next, holder.Service);
        Assert.Equal(1, holder.RefreshCount);
        Assert.Same(next, holder.SeenDuringRefresh);
    }

    [Fact]
    public void WhileDetached_SwapsButDoesNotRefresh()
    {
        var holder = new Holder { Attached = false };
        var next = new RecommendationDataService();

        holder.Set(next);

        Assert.Same(next, holder.Service);
        Assert.Equal(0, holder.RefreshCount);
        Assert.Null(holder.SeenDuringRefresh);
    }

    [Fact]
    public void SynchronousRefreshFailure_StillPropagates()
    {
        IRecommendationInfoService service = new RecommendationDataService();
        var defaultService = service;

        Assert.Throws<InvalidOperationException>(() => RecommendationServiceBinding.Attach(
            ref service, defaultService, defaultService, () => true, () => throw new InvalidOperationException()));
    }

    /// <summary>组件形状：自己一个默认实例、一个字段、一个挂载标志、一个刷新方法。</summary>
    private sealed class Holder
    {
        public Holder()
        {
            Service = Default;
        }

        public IRecommendationInfoService Default { get; } = new RecommendationDataService();

        public IRecommendationInfoService Service;

        public bool Attached { get; set; }

        public int RefreshCount { get; private set; }

        public IRecommendationInfoService? SeenDuringRefresh { get; private set; }

        public void Set(IRecommendationInfoService? next)
        {
            RecommendationServiceBinding.Attach(
                ref Service,
                next,
                Default,
                () => Attached,
                () => RefreshAsync());
        }

        private Task RefreshAsync()
        {
            RefreshCount++;
            SeenDuringRefresh = Service;
            return Task.CompletedTask;
        }
    }
}
