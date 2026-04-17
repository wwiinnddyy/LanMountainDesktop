using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// 错误调试窗口 - 开发人员专用调试设置
/// </summary>
public partial class ErrorDebugWindow : Window
{
    private string? _selectedHostPath;
    private bool _isInitialized = false;

    /// <summary>
    /// 是否启用了开发模式
    /// </summary>
    public bool IsDevModeEnabled { get; private set; }

    /// <summary>
    /// 选择的主程序路径
    /// </summary>
    public string? SelectedHostPath => _selectedHostPath;

    public ErrorDebugWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 延迟到窗口加载完成后再初始化组件
        this.Loaded += OnWindowLoaded;
    }

    public ErrorDebugWindow(bool devModeEnabled, string? initialPath) : this()
    {
        IsDevModeEnabled = devModeEnabled;
        _selectedHostPath = initialPath;
    }

    /// <summary>
    /// 窗口加载完成事件
    /// </summary>
    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;
        
        Console.WriteLine("[ErrorDebugWindow] Window loaded, initializing components...");
        InitializeComponents();
        
        // 设置初始值（在视觉树准备好后）
        var devModeToggle = this.FindControl<ToggleSwitch>("DevModeToggle");
        if (devModeToggle is not null)
        {
            devModeToggle.IsChecked = IsDevModeEnabled;
        }

        UpdatePathDisplay(_selectedHostPath);
    }

    private void InitializeComponents()
    {
        // 开发模式开关
        var devModeToggle = this.FindControl<ToggleSwitch>("DevModeToggle");
        if (devModeToggle is not null)
        {
            devModeToggle.IsCheckedChanged += (s, e) =>
            {
                IsDevModeEnabled = devModeToggle.IsChecked ?? false;
                Console.WriteLine($"[ErrorDebugWindow] DevMode changed to: {IsDevModeEnabled}");
            };
            Console.WriteLine("[ErrorDebugWindow] DevModeToggle event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorDebugWindow] Failed to find DevModeToggle!");
        }

        // 浏览按钮
        var browseButton = this.FindControl<Button>("BrowseButton");
        if (browseButton is not null)
        {
            browseButton.Click += OnBrowseClick;
            Console.WriteLine("[ErrorDebugWindow] BrowseButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorDebugWindow] Failed to find BrowseButton!");
        }

        // 确定按钮
        var okButton = this.FindControl<Button>("OkButton");
        if (okButton is not null)
        {
            okButton.Click += (s, e) => Close();
            Console.WriteLine("[ErrorDebugWindow] OkButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorDebugWindow] Failed to find OkButton!");
        }

        // 取消按钮
        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton is not null)
        {
            cancelButton.Click += (s, e) =>
            {
                // 取消时恢复原始状态
                IsDevModeEnabled = false;
                _selectedHostPath = null;
                Console.WriteLine("[ErrorDebugWindow] Cancel clicked, resetting state");
                Close();
            };
            Console.WriteLine("[ErrorDebugWindow] CancelButton event bound");
        }
        else
        {
            Console.Error.WriteLine("[ErrorDebugWindow] Failed to find CancelButton!");
        }
        
        Console.WriteLine("[ErrorDebugWindow] Components initialization completed");
    }

    /// <summary>
    /// 浏览按钮点击
    /// </summary>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (storageProvider is null) return;

        var options = new FilePickerOpenOptions
        {
            Title = "选择阑山桌面主程序",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? new[] { "*.exe" }
                        : new[] { "*" }
                }
            }
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        if (result.Count > 0)
        {
            _selectedHostPath = result[0].Path.LocalPath;
            Console.WriteLine($"[ErrorDebugWindow] Selected host path: {_selectedHostPath}");
            UpdatePathDisplay(_selectedHostPath);
        }
    }

    /// <summary>
    /// 更新路径显示
    /// </summary>
    private void UpdatePathDisplay(string? path)
    {
        var pathTextBlock = this.FindControl<TextBlock>("PathTextBlock");
        if (pathTextBlock is not null)
        {
            pathTextBlock.Text = string.IsNullOrEmpty(path) ? "未选择" : path;
        }
        else
        {
            Console.Error.WriteLine("[ErrorDebugWindow] Failed to find PathTextBlock!");
        }
    }
}
