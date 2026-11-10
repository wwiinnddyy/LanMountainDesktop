using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.ComponentEditors;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 智教Hub 切到 Rin's 图库必须先过成人内容确认：取消 → 不落盘、不刷新、选中还原；
/// 确认 → 才落盘；切其它源不弹。确认弹窗本身用子类覆写掉（真实 FAContentDialog 要交互，
/// 这里钉的是"问没问、问完怎么办"这条决策链）。单会话内部逐场景循环（#G1-I 会话成本）。
/// </summary>
public sealed class ZhiJiaoHubAdultWarningTests
{
    [AvaloniaFact]
    public void RinLitSelection_AsksEveryTime_CancelReverts_ConfirmSaves()
    {
        var accessor = new FakeComponentSettingsAccessor();
        var host = new FakeEditorHost();
        var editor = new ConfirmOverrideEditor(BuildContext(accessor, host));
        var window = new Window { Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combo = editor.GetVisualDescendants().OfType<ComboBox>()
            .First(c => ItemWithTag(c, ZhiJiaoHubSources.RinLit) is not null);

        // 场景 1：取消 —— 弹了一次，但没落盘、没刷新，选中还原回 ClassIsland
        editor.ConfirmResult = false;
        combo.SelectedItem = ItemWithTag(combo, ZhiJiaoHubSources.RinLit);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ConfirmCalls == 1, $"场景1：应弹 1 次确认，实际 {editor.ConfirmCalls}");
        Assert.True(accessor.SavedSources.Count == 0, "场景1：取消后不该落盘");
        Assert.True(host.RefreshCalls == 0, "场景1：取消后不该请求刷新");
        Assert.True(ReferenceEquals(combo.SelectedItem, ItemWithTag(combo, ZhiJiaoHubSources.ClassIsland)),
            "场景1：取消后选中应还原回 ClassIsland");

        // 场景 2：确认 —— 落盘 rinlit 并请求刷新，选中保持
        editor.ConfirmResult = true;
        combo.SelectedItem = ItemWithTag(combo, ZhiJiaoHubSources.RinLit);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ConfirmCalls == 2, $"场景2：每次切到 Rin's 都要再问，实际共 {editor.ConfirmCalls} 次");
        Assert.True(accessor.SavedSources.SequenceEqual(new[] { ZhiJiaoHubSources.RinLit }),
            $"场景2：确认后应落盘 rinlit，实际 [{string.Join(",", accessor.SavedSources)}]");
        Assert.True(host.RefreshCalls == 1, "场景2：落盘后应请求一次刷新");
        Assert.True(ReferenceEquals(combo.SelectedItem, ItemWithTag(combo, ZhiJiaoHubSources.RinLit)),
            "场景2：确认后选中应保持 Rin's");

        // 场景 3：从 Rin's 切到 SECTL —— 不弹，直接落盘
        combo.SelectedItem = ItemWithTag(combo, ZhiJiaoHubSources.Sectl);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ConfirmCalls == 2, $"场景3：切走不该再弹，实际共 {editor.ConfirmCalls} 次");
        Assert.True(accessor.SavedSources.SequenceEqual(new[] { ZhiJiaoHubSources.RinLit, ZhiJiaoHubSources.Sectl }),
            "场景3：切到 SECTL 应直接落盘");

        // 场景 4：再切回 Rin's 仍要问（不是只问第一次）
        editor.ConfirmResult = false;
        combo.SelectedItem = ItemWithTag(combo, ZhiJiaoHubSources.RinLit);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ConfirmCalls == 3, $"场景4：再次切到 Rin's 仍要问，实际共 {editor.ConfirmCalls} 次");
        Assert.True(ReferenceEquals(combo.SelectedItem, ItemWithTag(combo, ZhiJiaoHubSources.Sectl)),
            "场景4：取消后应还原回 SECTL（盘上当前值）");
        Assert.True(accessor.SavedSources.Count == 2, "场景4：取消后不该新增落盘");

        window.Close();
    }

    private static ComboBoxItem? ItemWithTag(ComboBox combo, string tag)
    {
        for (var i = 0; i < combo.ItemCount; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == tag)
            {
                return item;
            }
        }
        return null;
    }

    private static DesktopComponentEditorContext BuildContext(
        FakeComponentSettingsAccessor accessor, FakeEditorHost host)
    {
        return new DesktopComponentEditorContext(
            null!, "zhijiaohub", null,
            new FakeSettingsFacade(), null!, accessor, null!, host);
    }

    private sealed class ConfirmOverrideEditor(DesktopComponentEditorContext context)
        : ZhiJiaoHubComponentEditor(context)
    {
        public bool ConfirmResult { get; set; }
        public int ConfirmCalls { get; private set; }

        protected override Task<bool> ConfirmAdultContentAsync()
        {
            ConfirmCalls++;
            return Task.FromResult(ConfirmResult);
        }
    }

    private sealed class FakeComponentSettingsAccessor : IComponentSettingsAccessor
    {
        public ComponentSettingsSnapshot Snapshot { get; private set; } = new();
        public List<string> SavedSources { get; } = new();

        public string ComponentId => "zhijiaohub";
        public string? PlacementId => null;

        public T LoadSnapshot<T>() where T : new() => Snapshot is T typed ? typed : new T();

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
        {
            if (snapshot is ComponentSettingsSnapshot typed)
            {
                Snapshot = typed;
                SavedSources.Add(typed.ZhiJiaoHubSource);
            }
        }

        public T LoadSection<T>(string sectionId) where T : new() => throw new NotSupportedException();
        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();
        public void DeleteSection(string sectionId) => throw new NotSupportedException();
        public T? GetValue<T>(string key) => throw new NotSupportedException();
        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();
    }

    private sealed class FakeEditorHost : IComponentEditorHostContext
    {
        public int RefreshCalls { get; private set; }
        public void RequestRefresh() => RefreshCalls++;
        public void CloseEditor() { }
        public void RequestRestart(string? reason = null) { }
    }

    private sealed class FakeSettingsFacade : ISettingsFacadeService
    {
        public ISettingsService Settings => throw new NotSupportedException();
        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => throw new NotSupportedException();
        public IWallpaperSettingsService Wallpaper => throw new NotSupportedException();
        public IWallpaperMediaService WallpaperMedia => throw new NotSupportedException();
        public IThemeAppearanceService Theme => throw new NotSupportedException();
        public IStatusBarSettingsService StatusBar => throw new NotSupportedException();
        public ITextCapsuleSettingsService TextCapsule => throw new NotSupportedException();
        public IWeatherSettingsService Weather => throw new NotSupportedException();
        public IRegionSettingsService Region { get; } = new FakeRegionSettingsService();
        public IPrivacySettingsService Privacy => throw new NotSupportedException();
        public IUpdateSettingsService Update => throw new NotSupportedException();
        public ILauncherCatalogService LauncherCatalog => throw new NotSupportedException();
        public ILauncherPolicyService LauncherPolicy => throw new NotSupportedException();
        public IAirAppManagementSettingsService AirAppManagement => throw new NotSupportedException();
        public IAirAppCatalogSettingsService AirAppCatalog => throw new NotSupportedException();
        public IApplicationInfoService ApplicationInfo => throw new NotSupportedException();
    }

    private sealed class FakeRegionSettingsService : IRegionSettingsService
    {
        public RegionSettingsState Get() => new("zh-CN", null);
        public void Save(RegionSettingsState state) { }
        public TimeZoneService GetTimeZoneService() => throw new NotSupportedException();
    }
}