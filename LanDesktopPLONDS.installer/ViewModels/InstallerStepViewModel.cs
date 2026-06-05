using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;
using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.ViewModels;

public sealed partial class InstallerStepViewModel(
    InstallerStepId stepId,
    string title,
    Icon icon) : ObservableObject
{
    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isSelected;

    public InstallerStepId StepId { get; } = stepId;

    public string Title { get; } = title;

    public Icon Icon { get; } = icon;
}
