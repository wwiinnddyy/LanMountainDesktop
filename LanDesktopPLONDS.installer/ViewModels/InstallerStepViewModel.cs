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

    [ObservableProperty]
    private bool _isCompleted;

    public InstallerStepId StepId { get; } = stepId;

    public string Title { get; } = title;

    public Icon Icon { get; } = icon;

    public bool IsLocked => !IsUnlocked;

    public Icon DisplayIcon => IsLocked
        ? Icon.LockClosed
        : IsCompleted
            ? Icon.CheckmarkCircle
            : Icon;

    public bool IsAvailable => IsUnlocked && !IsSelected && !IsCompleted;

    partial void OnIsUnlockedChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(DisplayIcon));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsAvailable));
    }

    partial void OnIsCompletedChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(DisplayIcon));
        OnPropertyChanged(nameof(IsAvailable));
    }
}
