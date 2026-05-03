using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Services;

public interface IUpdateProgressReporter
{
    void ReportProgress(InstallProgressReport report);
    void ReportComplete(InstallCompleteReport report);
}
