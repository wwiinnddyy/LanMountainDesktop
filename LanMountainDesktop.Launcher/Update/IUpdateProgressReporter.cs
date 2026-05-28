using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Update;

public interface IUpdateProgressReporter
{
    void ReportProgress(InstallProgressReport report);
    void ReportComplete(InstallCompleteReport report);
}
