using Avalonia.Controls;

namespace LanDesktopPLONDS.Installer.Views;

public partial class UninstallConfirmWindow : Window
{
    public UninstallConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
