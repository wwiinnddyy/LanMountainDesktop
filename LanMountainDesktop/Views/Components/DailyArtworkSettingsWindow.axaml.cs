using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class DailyArtworkSettingsWindow : UserControl
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly ComponentSettingsService _componentSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private string _languageCode = "zh-CN";
    private bool _suppressEvents;

    public event EventHandler? SettingsChanged;

    public DailyArtworkSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var appSnapshot = _appSettingsService.Load();
        var componentSnapshot = _componentSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        var source = DailyArtworkMirrorSources.Normalize(componentSnapshot.DailyArtworkMirrorSource);
        _suppressEvents = true;
        MirrorSourceComboBox.SelectedIndex = string.Equals(source, DailyArtworkMirrorSources.Domestic, StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
        _suppressEvents = false;
        UpdateSourceStatus(source);
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("artwork.settings.title", "每日图片设置");
        DescriptionTextBlock.Text = L("artwork.settings.desc", "切换每日图片的数据源。");
        MirrorSourceLabelTextBlock.Text = L("artwork.settings.source_label", "镜像源");
        MirrorSourceDomesticItem.Content = L("artwork.settings.source_domestic", "国内镜像");
        MirrorSourceOverseasItem.Content = L("artwork.settings.source_overseas", "国外镜像");
        UpdateSourceStatus(GetSelectedSource());
    }

    private void OnMirrorSourceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var source = GetSelectedSource();
        var snapshot = _componentSettingsService.Load();
        snapshot.DailyArtworkMirrorSource = source;
        _componentSettingsService.Save(snapshot);

        UpdateSourceStatus(source);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetSelectedSource()
    {
        if (MirrorSourceComboBox.SelectedItem is ComboBoxItem comboBoxItem &&
            comboBoxItem.Tag is string tagValue)
        {
            return DailyArtworkMirrorSources.Normalize(tagValue);
        }

        return DailyArtworkMirrorSources.Overseas;
    }

    private void UpdateSourceStatus(string source)
    {
        if (StatusTextBlock is null)
        {
            return;
        }

        StatusTextBlock.Text = string.Equals(source, DailyArtworkMirrorSources.Domestic, StringComparison.OrdinalIgnoreCase)
            ? L("artwork.settings.source_status_domestic", "当前源：国内镜像（优先中国网络）")
            : L("artwork.settings.source_status_overseas", "当前源：国外镜像（艺术馆推荐）");
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
