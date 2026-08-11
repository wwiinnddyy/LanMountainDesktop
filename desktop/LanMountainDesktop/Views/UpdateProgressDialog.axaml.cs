using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views;

public partial class UpdateProgressDialog : Window
{
    public UpdateProgressDialog()
    {
        InitializeComponent();
    }

    public UpdateProgressDialog(UpdateProgressViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(UpdateProgressViewModel.IsCompleted) && viewModel.IsCompleted)
            {
                Close(viewModel.IsSuccess);
            }
        };
    }
}
