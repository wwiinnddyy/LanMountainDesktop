namespace LanMountainDesktop.Launcher.Services;

internal interface ISplashStageReporter
{
    void Report(string stage, string message);
    
    /// <summary>
    /// 报告阶段和进度（0-100）
    /// </summary>
    void ReportStage(string stage, int progress);
}
