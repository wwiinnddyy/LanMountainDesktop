using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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

    /// <summary>
    /// Task 5: 安装进行中阻止关闭窗口，弹出中文确认对话框。
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsInstalling: true })
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        var confirmed = await ShowCloseConfirmDialogAsync();
        if (confirmed)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.CancelInstallCommand.Execute(null);
            }

            Close();
        }
    }

    private async Task<bool> ShowCloseConfirmDialogAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        var yesButton = new Button { Content = "确定退出", MinWidth = 100 };
        yesButton.Classes.Add("primary-command");
        yesButton.Click += (_, _) => tcs.TrySetResult(true);

        var noButton = new Button { Content = "继续安装", MinWidth = 100 };
        noButton.Classes.Add("secondary-command");
        noButton.Click += (_, _) => tcs.TrySetResult(false);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);

        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        var panel = new DockPanel { Margin = new Thickness(24) };
        panel.Children.Add(buttonPanel);
        panel.Children.Add(new TextBlock
        {
            Text = "安装正在进行，确定要退出吗？",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
        });

        var dialog = new Window
        {
            Title = "确认退出",
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            WindowDecorations = WindowDecorations.BorderOnly,
            Content = panel,
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        _ = dialog.ShowDialog(this);
        return await tcs.Task;
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
