using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LanDesktopPLONDS.Installer.Views;

public partial class UninstallConfirmWindow : Window
{
    public UninstallConfirmWindow()
    {
        InitializeComponent();
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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UninstallConfirmViewModel vm)
        {
            vm.Confirm();
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is UninstallConfirmViewModel vm)
        {
            vm.Cancel();
        }
    }
}
