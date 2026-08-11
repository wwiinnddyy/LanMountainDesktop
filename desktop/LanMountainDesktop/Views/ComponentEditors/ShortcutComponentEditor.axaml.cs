using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class ShortcutComponentEditor : ComponentEditorViewBase
{
    private ShortcutEditorViewModel? _viewModel;

    public ShortcutComponentEditor()
        : this(null)
    {
    }

    public ShortcutComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        _viewModel = new ShortcutEditorViewModel(context);
        DataContext = _viewModel;
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
            Title = "选择目标文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("可执行文件")
                {
                    Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        var files = await storageProvider.OpenFilePickerAsync(options);
        var localPath = files.FirstOrDefault()?.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(localPath))
        {
            var folderOptions = new FolderPickerOpenOptions
            {
                Title = "选择目标文件夹",
                AllowMultiple = false
            };

            var folders = await storageProvider.OpenFolderPickerAsync(folderOptions);
            localPath = folders.FirstOrDefault()?.TryGetLocalPath();
        }

        if (!string.IsNullOrWhiteSpace(localPath))
        {
            _viewModel?.SetTargetPath(localPath);
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ClearTargetPath();
    }
}
