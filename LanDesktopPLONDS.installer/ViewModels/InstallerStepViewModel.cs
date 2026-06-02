using CommunityToolkit.Mvvm.ComponentModel;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.ViewModels;

public sealed partial class InstallerStepViewModel(
    InstallerStepId stepId,
    string title,
    string iconKey) : ObservableObject
{
    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isSelected;

    public InstallerStepId StepId { get; } = stepId;

    public string Title { get; } = title;

    public string IconKey { get; } = iconKey;
}
