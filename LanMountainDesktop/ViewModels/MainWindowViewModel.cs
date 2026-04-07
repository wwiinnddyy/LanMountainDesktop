using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;

namespace LanMountainDesktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "A modern desktop shell powered by FluentAvalonia.";

    [RelayCommand]
    private void OpenDesignSpec(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", fileName);
        if (!File.Exists(fullPath))
        {
            // Try relative to project root in dev
            fullPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "docs", fileName));
        }

        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                 FileName = fullPath,
                 UseShellExecute = true
            });
        }
    }
}
