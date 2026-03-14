using Avalonia.Controls;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginMarketDetailDrawer : UserControl
{
    public PluginMarketDetailDrawer()
    {
        InitializeComponent();
    }

    public PluginMarketDetailDrawer(PluginMarketDetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
