using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class ZhiJiaoHubComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public ZhiJiaoHubComponentEditor()
        : this(null)
    {
    }

    public ZhiJiaoHubComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyLocalization();
        LoadState();
    }

    private void ApplyLocalization()
    {
        // 标题
        SourceLabelTextBlock.Text = L("zhijiaohub.settings.source", "图片源");
        ClassIslandItem.Content = L("zhijiaohub.settings.classisland", "ClassIsland 图库");
        SectlItem.Content = L("zhijiaohub.settings.sectl", "SECTL 图库");
        RinLitItem.Content = L("zhijiaohub.settings.rinlit", "Rin's 图库");

        // 数据源描述
        SourceDescriptionTextBlock.Text = L("zhijiaohub.settings.source_desc",
            "选择图片来源。ClassIsland 图库包含 ClassIsland 社区的趣味瞬间，SECTL 图库包含 SECTL 社区的内容，Rin's 图库包含 Rin's 社区的内容。");

        // 镜像加速源
        MirrorSourceLabelTextBlock.Text = L("zhijiaohub.settings.mirror_source", "镜像加速");
        DirectMirrorItem.Content = L("zhijiaohub.settings.mirror_direct", "直连（GitHub）");
        GhProxyMirrorItem.Content = L("zhijiaohub.settings.mirror_ghproxy", "镜像加速（推荐）");
        MirrorSourceDescriptionTextBlock.Text = L("zhijiaohub.settings.mirror_source_desc",
            "如果图片加载缓慢或失败，请尝试使用镜像加速。镜像加速通过第三方代理服务加速 GitHub 访问。");

        // 刷新设置
        RefreshSettingsLabelTextBlock.Text = L("zhijiaohub.settings.refresh", "刷新设置");
        AutoRefreshLabelTextBlock.Text = L("zhijiaohub.settings.auto_refresh", "自动刷新");
        AutoRefreshDescriptionTextBlock.Text = L("zhijiaohub.settings.auto_refresh_desc",
            "定期自动刷新图片列表。");
        IntervalLabelTextBlock.Text = L("zhijiaohub.settings.interval", "刷新间隔（分钟）");

        // 关于
        AboutLabelTextBlock.Text = L("zhijiaohub.settings.about", "关于");
        AboutDescriptionTextBlock.Text = L("zhijiaohub.settings.about_desc",
            "智教Hub 展示来自教育技术社区的有趣图片。图片从 GitHub 仓库获取并缓存在本地。");
    }

    private void LoadState()
    {
        _suppressEvents = true;

        var snapshot = LoadSnapshot();

        // 数据源
        var source = ZhiJiaoHubSources.Normalize(snapshot.ZhiJiaoHubSource);
        SourceComboBox.SelectedItem = source switch
        {
            ZhiJiaoHubSources.Sectl => SectlItem,
            ZhiJiaoHubSources.RinLit => RinLitItem,
            _ => ClassIslandItem
        };

        // 镜像加速源
        var mirrorSource = ZhiJiaoHubMirrorSources.Normalize(snapshot.ZhiJiaoHubMirrorSource);
        MirrorSourceComboBox.SelectedItem = mirrorSource switch
        {
            ZhiJiaoHubMirrorSources.GhProxy => GhProxyMirrorItem,
            _ => DirectMirrorItem
        };

        // 自动刷新
        AutoRefreshToggle.IsChecked = snapshot.ZhiJiaoHubAutoRefreshEnabled;

        // 刷新间隔
        var interval = Math.Clamp(snapshot.ZhiJiaoHubAutoRefreshIntervalMinutes, 5, 1440);
        IntervalNumeric.Value = interval;
        IntervalPanel.IsVisible = snapshot.ZhiJiaoHubAutoRefreshEnabled;

        _suppressEvents = false;
    }

    private void OnSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var source = SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? ZhiJiaoHubSources.Normalize(tag)
            : ZhiJiaoHubSources.ClassIsland;

        var snapshot = LoadSnapshot();
        snapshot.ZhiJiaoHubSource = source;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ZhiJiaoHubSource));
    }

    private void OnMirrorSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var mirrorSource = MirrorSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? ZhiJiaoHubMirrorSources.Normalize(tag)
            : ZhiJiaoHubMirrorSources.Direct;

        var snapshot = LoadSnapshot();
        snapshot.ZhiJiaoHubMirrorSource = mirrorSource;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ZhiJiaoHubMirrorSource));
    }

    private void OnAutoRefreshChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var isEnabled = AutoRefreshToggle.IsChecked ?? true;
        IntervalPanel.IsVisible = isEnabled;

        var snapshot = LoadSnapshot();
        snapshot.ZhiJiaoHubAutoRefreshEnabled = isEnabled;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ZhiJiaoHubAutoRefreshEnabled));
    }

    private void OnIntervalValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var interval = (int)Math.Clamp(IntervalNumeric.Value ?? 30, 5, 1440);

        var snapshot = LoadSnapshot();
        snapshot.ZhiJiaoHubAutoRefreshIntervalMinutes = interval;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ZhiJiaoHubAutoRefreshIntervalMinutes));
    }
}
