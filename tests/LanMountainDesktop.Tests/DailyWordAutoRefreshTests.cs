using System;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 每日一词自动刷新档位的家：两个面板（1x1 / 2x2）读同一对设置键，
/// 所以"怎么读、兜底成什么"只能有一份实现。这三条钉的是那条兜底与那对键真的被尊重。
/// </summary>
public sealed class DailyWordAutoRefreshTests
{
    [AvaloniaFact]
    public void Apply_HonoursTheStoredToggleAndNormalisesAnIllegalInterval()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        var store = new FakeStore(new ComponentSettingsSnapshot
        {
            DailyWordAutoRefreshEnabled = true,
            DailyWordAutoRefreshIntervalMinutes = 7
        });

        var enabled = DailyWordAutoRefresh.Apply(store, timer, isAttached: true);

        Assert.True(enabled);
        // 7 分钟不是合法档位，必须走档位家钳到最近的合法步（5），而不是照字面起表。
        Assert.Equal(TimeSpan.FromMinutes(RefreshIntervalCatalog.SupportedIntervalsMinutes[0]), timer.Interval);
        Assert.True(timer.IsEnabled);
        timer.Stop();
    }

    [AvaloniaFact]
    public void Apply_StopsARunningTimer_WhenThePanelTurnedAutoRefreshOff()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Start();
        var store = new FakeStore(new ComponentSettingsSnapshot
        {
            DailyWordAutoRefreshEnabled = false,
            DailyWordAutoRefreshIntervalMinutes = 30
        });

        Assert.False(DailyWordAutoRefresh.Apply(store, timer, isAttached: true));
        Assert.False(timer.IsEnabled);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Interval);
    }

    [AvaloniaFact]
    public void Apply_FallsBackToTheDefaultStep_WhenTheStoreCannotRead()
    {
        // 首次启动或盘上快照坏了：面板照常显示，只是这次按默认档（6 小时）跑。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };

        Assert.True(DailyWordAutoRefresh.Apply(new FakeStore(throwOnLoad: true), timer, isAttached: true));
        Assert.Equal(
            TimeSpan.FromMinutes(DailyWordAutoRefresh.DefaultIntervalMinutes),
            timer.Interval);
        timer.Stop();
    }

    private sealed class FakeStore(ComponentSettingsSnapshot? snapshot = null, bool throwOnLoad = false)
        : IComponentInstanceSettingsStore
    {
        public ComponentSettingsSnapshot Load() =>
            throwOnLoad ? throw new IOException("simulated unreadable settings") : snapshot!;

        public void Save(ComponentSettingsSnapshot snapshot)
        {
        }

        public ComponentSettingsSnapshot LoadForComponent(string componentId, string? placementId) =>
            snapshot!;

        public void SaveForComponent(string componentId, string? placementId, ComponentSettingsSnapshot snapshot)
        {
        }

        public void DeleteForComponent(string componentId, string? placementId)
        {
        }

        public T LoadAirAppSettings<T>(string componentId, string? placementId)
            where T : new() => new T();

        public void SaveAirAppSettings<T>(string componentId, string? placementId, T settings)
        {
        }

        public void DeleteAirAppSettings(string componentId, string? placementId)
        {
        }
    }
}
