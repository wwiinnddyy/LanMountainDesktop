namespace LanMountainDesktop.Shared.Contracts.Update;

public enum UpdatePhase
{
    Idle,
    Checking,
    Checked,
    Downloading,
    PausedDownloading,
    Downloaded,
    Installing,
    PausedInstalling,
    Installed,
    Verifying,
    Completed,
    Failed,
    Recovering,
    RollingBack,
    RolledBack
}

public enum UpdatePayloadKind
{
    DeltaPlonds,
    DeltaLegacy,
    FullInstaller
}

public enum InstallStage
{
    None,
    VerifySignature,
    CreateTarget,
    ApplyFiles,
    VerifyHashes,
    ActivateDeployment,
    Cleanup,
    Completed,
    Failed,
    RollingBack
}

public enum UpdateChannel
{
    Stable,
    Preview
}

public enum UpdateMode
{
    Manual,
    DownloadThenConfirm,
    SilentOnExit
}

public enum UpdateDownloadSource
{
    PlondsApi,
    GitHub,
    GhProxy
}

public static class UpdatePhaseExtensions
{
    public static bool IsTerminal(this UpdatePhase phase) =>
        phase is UpdatePhase.Completed or UpdatePhase.Failed or UpdatePhase.RolledBack;

    public static bool IsBusy(this UpdatePhase phase) =>
        phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing
            or UpdatePhase.Verifying or UpdatePhase.Recovering or UpdatePhase.RollingBack;

    public static bool CanCheck(this UpdatePhase phase) =>
        phase is UpdatePhase.Idle or UpdatePhase.Checked or UpdatePhase.Downloaded
            or UpdatePhase.Completed or UpdatePhase.Failed or UpdatePhase.RolledBack;

    public static bool CanDownload(this UpdatePhase phase) =>
        phase is UpdatePhase.Checked;

    public static bool CanInstall(this UpdatePhase phase) =>
        phase is UpdatePhase.Downloaded;

    public static bool CanRollback(this UpdatePhase phase) =>
        phase is UpdatePhase.Failed;

    public static bool CanPause(this UpdatePhase phase) =>
        phase is UpdatePhase.Downloading;

    public static bool CanResume(this UpdatePhase phase) =>
        phase is UpdatePhase.PausedDownloading or UpdatePhase.PausedInstalling;

    public static bool CanCancel(this UpdatePhase phase) =>
        phase.IsBusy() || phase.CanResume();

    public static bool IsPaused(this UpdatePhase phase) =>
        phase is UpdatePhase.PausedDownloading or UpdatePhase.PausedInstalling;
}
