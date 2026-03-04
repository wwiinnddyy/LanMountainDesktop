using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class StudyEnvironmentWidgetSettingsWindow : UserControl
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private string _languageCode = "zh-CN";
    private bool _suppressEvents;

    public event EventHandler? SettingsChanged;

    public StudyEnvironmentWidgetSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
    }

    private void LoadState()
    {
        var snapshot = _appSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);

        var showDisplayDb = snapshot.StudyEnvironmentShowDisplayDb;
        var showDbfs = snapshot.StudyEnvironmentShowDbfs;
        if (!showDisplayDb && !showDbfs)
        {
            showDisplayDb = true;
        }

        _suppressEvents = true;
        ShowDisplayDbCheckBox.IsChecked = showDisplayDb;
        ShowDbfsCheckBox.IsChecked = showDbfs;
        _suppressEvents = false;
    }

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("study.environment.settings.title", "环境组件设置");
        DescriptionTextBlock.Text = L(
            "study.environment.settings.desc",
            "配置右侧实时噪音值显示内容。");
        ShowDisplayDbCheckBox.Content = L(
            "study.environment.settings.show_display_db",
            "显示 display dB");
        ShowDbfsCheckBox.Content = L(
            "study.environment.settings.show_dbfs",
            "显示 dBFS");
        HintTextBlock.Text = L(
            "study.environment.settings.hint",
            "至少启用一种显示方式。");
    }

    private void OnDisplayModeChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var showDisplayDb = ShowDisplayDbCheckBox.IsChecked == true;
        var showDbfs = ShowDbfsCheckBox.IsChecked == true;
        if (!showDisplayDb && !showDbfs)
        {
            _suppressEvents = true;
            ShowDisplayDbCheckBox.IsChecked = true;
            _suppressEvents = false;
            showDisplayDb = true;
        }

        var snapshot = _appSettingsService.Load();
        snapshot.StudyEnvironmentShowDisplayDb = showDisplayDb;
        snapshot.StudyEnvironmentShowDbfs = showDbfs;
        _appSettingsService.Save(snapshot);

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
