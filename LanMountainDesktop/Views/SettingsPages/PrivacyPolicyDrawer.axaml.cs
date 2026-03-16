using Avalonia.Controls;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PrivacyPolicyDrawer : UserControl
{
    public PrivacyPolicyDrawer()
    {
        InitializeComponent();
    }

    public PrivacyPolicyDrawer(PrivacyPolicyViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
