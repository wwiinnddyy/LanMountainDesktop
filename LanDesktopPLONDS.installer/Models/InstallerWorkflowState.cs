namespace LanDesktopPLONDS.Installer.Models;

public sealed record InstallerWorkflowState(
    InstallerStepId CurrentStep,
    InstallerStepId MaxUnlockedStep,
    string InstallPath,
    bool PrivacyConfirmed,
    string? TargetVersion,
    string? ErrorMessage);
