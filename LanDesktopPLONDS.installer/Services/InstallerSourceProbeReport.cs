namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 记录单个下载源探测失败信息，用于诊断聚合。
/// </summary>
internal sealed record InstallerSourceProbeFailure(
    string SourceId,
    string ManifestUrl,
    string ErrorMessage);

/// <summary>
/// 聚合所有下载源探测失败信息，当所有源均不可用时生成中文诊断摘要。
/// </summary>
internal sealed class InstallerSourceProbeReport
{
    private readonly List<InstallerSourceProbeFailure> _failures = [];

    /// <summary>所有记录的失败信息。</summary>
    public IReadOnlyList<InstallerSourceProbeFailure> Failures => _failures;

    /// <summary>是否至少记录了一个失败。</summary>
    public bool HasFailures => _failures.Count > 0;

    /// <summary>记录一次源探测失败。</summary>
    public void AddFailure(string sourceId, string manifestUrl, Exception exception)
    {
        _failures.Add(new InstallerSourceProbeFailure(sourceId, manifestUrl, exception.Message));
    }

    /// <summary>记录一次源探测失败（字符串消息）。</summary>
    public void AddFailure(string sourceId, string manifestUrl, string errorMessage)
    {
        _failures.Add(new InstallerSourceProbeFailure(sourceId, manifestUrl, errorMessage));
    }

    /// <summary>
    /// 生成中文诊断摘要，列出每个失败的源 ID 和错误信息。
    /// 用于抛出 InvalidOperationException 时的 message 参数。
    /// </summary>
    public string FormatChineseSummary()
    {
        if (_failures.Count == 0)
        {
            return "所有下载源均不可用。";
        }

        var lines = _failures.Select(f => $"- {f.SourceId}: {f.ErrorMessage}");
        return $"所有下载源均不可用：\n{string.Join("\n", lines)}";
    }
}
