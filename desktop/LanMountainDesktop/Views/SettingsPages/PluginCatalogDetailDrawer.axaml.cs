using Avalonia.Controls;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginCatalogDetailDrawer : UserControl
{
    public PluginCatalogDetailDrawer()
    {
        InitializeComponent();
    }

    public PluginCatalogDetailDrawer(PluginCatalogDetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
