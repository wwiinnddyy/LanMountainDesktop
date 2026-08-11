using Avalonia.Controls;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class AirAppCatalogDetailDrawer : UserControl
{
    public AirAppCatalogDetailDrawer()
    {
        InitializeComponent();
    }

    public AirAppCatalogDetailDrawer(AirAppCatalogDetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
