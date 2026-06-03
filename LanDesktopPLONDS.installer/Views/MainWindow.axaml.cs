using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LanDesktopPLONDS.Installer.ViewModels;

namespace LanDesktopPLONDS.Installer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BrowseRequested = BrowseForFolderAsync;
        }
    }

    private async Task<string?> BrowseForFolderAsync(string currentPath)
    {
        IStorageFolder? startFolder = null;
        if (Directory.Exists(currentPath))
        {
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(currentPath);
        }

        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择安装位置",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        if (result.Count == 0)
        {
            return null;
        }

        var path = result[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("请选择本机文件夹作为安装位置。");
        }

        return path;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (e.Source is Button)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }
}
