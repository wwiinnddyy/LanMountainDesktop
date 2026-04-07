using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class ShortcutComponentEditor : ComponentEditorViewBase
{
    private bool _suppressEvents;

    public ShortcutComponentEditor()
        : this(null)
    {
    }

    public ShortcutComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        ApplyLocalizedText();
        ApplyState();
        AttachEventHandlers();
    }

    private void ApplyLocalizedText()
    {
        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "快捷方式";
        DescriptionTextBlock.Text = L(
            "shortcut.settings.desc",
            "配置快捷方式的目标路径和打开方式。");

        BackgroundLabel.Text = L("shortcut.settings.show_background", "显示背景");
        BackgroundDescription.Text = L(
            "shortcut.settings.show_background.desc",
            "关闭后组件背景将变为透明。");
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var targetPath = snapshot.ShortcutTargetPath;
        var clickMode = snapshot.ShortcutClickMode;
        var transparentBackground = snapshot.ShortcutTransparentBackground;

        _suppressEvents = true;
        TargetPathTextBox.Text = targetPath ?? string.Empty;
        SingleClickRadio.IsChecked = string.Equals(clickMode, "Single", StringComparison.OrdinalIgnoreCase);
        DoubleClickRadio.IsChecked = !SingleClickRadio.IsChecked;
        BackgroundToggle.IsChecked = !transparentBackground;
        _suppressEvents = false;
    }

    private void AttachEventHandlers()
    {
        BackgroundToggle.IsCheckedChanged += OnBackgroundToggleChanged;
        SingleClickRadio.IsCheckedChanged += OnClickModeChanged;
        DoubleClickRadio.IsCheckedChanged += OnClickModeChanged;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = L("shortcut.settings.picker_title", "选择目标文件或文件夹"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L("shortcut.settings.picker_type.executable", "可执行文件"))
                {
                    Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"]
                },
                new FilePickerFileType(L("shortcut.settings.picker_type.all", "所有文件"))
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        var files = await storageProvider.OpenFilePickerAsync(options);
        var file = files.FirstOrDefault();
        var localPath = file?.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(localPath))
        {
            var folderOptions = new FolderPickerOpenOptions
            {
                Title = L("shortcut.settings.picker_title_folder", "选择目标文件夹"),
                AllowMultiple = false
            };

            var folders = await storageProvider.OpenFolderPickerAsync(folderOptions);
            localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        }

        if (!string.IsNullOrWhiteSpace(localPath))
        {
            TargetPathTextBox.Text = localPath;
            var snapshot = LoadSnapshot();
            snapshot.ShortcutTargetPath = localPath;
            SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ShortcutTargetPath));
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        TargetPathTextBox.Text = string.Empty;
        var snapshot = LoadSnapshot();
        snapshot.ShortcutTargetPath = null;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ShortcutTargetPath));
    }

    private void OnClickModeChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var clickMode = SingleClickRadio.IsChecked == true ? "Single" : "Double";
        var snapshot = LoadSnapshot();
        snapshot.ShortcutClickMode = clickMode;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ShortcutClickMode));
    }

    private void OnBackgroundToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var transparentBackground = BackgroundToggle.IsChecked != true;
        var snapshot = LoadSnapshot();
        snapshot.ShortcutTransparentBackground = transparentBackground;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ShortcutTransparentBackground));
    }
}
