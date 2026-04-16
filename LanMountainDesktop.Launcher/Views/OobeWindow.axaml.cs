using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LanMountainDesktop.Launcher.Views;

/// <summary>
/// OOBE（首次使用体验）窗口
/// </summary>
public partial class OobeWindow : Window
{
    private readonly TaskCompletionSource<bool> _completionSource = new();

    public OobeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        var enterButton = this.FindControl<Button>("EnterButton");
        if (enterButton is not null)
        {
            enterButton.Click += OnEnterClick;
        }
    }

    /// <summary>
    /// 等待用户点击开始按钮
    /// </summary>
    public Task WaitForEnterAsync() => _completionSource.Task;

    private void OnEnterClick(object? sender, RoutedEventArgs e)
    {
        _completionSource.TrySetResult(true);
    }
}
