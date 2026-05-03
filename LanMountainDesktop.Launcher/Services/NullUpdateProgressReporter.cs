using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class NullUpdateProgressReporter : IUpdateProgressReporter
{
    public void ReportProgress(InstallProgressReport report) { }
    public void ReportComplete(InstallCompleteReport report) { }
}
