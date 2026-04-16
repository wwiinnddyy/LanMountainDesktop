using Avalonia.Threading;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class DeferredSplashStageReporter : ISplashStageReporter
{
    private ISplashStageReporter? _inner;
    private readonly List<(string Stage, string Message)> _pending = [];

    public void SetInner(ISplashStageReporter inner)
    {
        _inner = inner;
        foreach (var (stage, message) in _pending)
        {
            _inner.Report(stage, message);
        }
        _pending.Clear();
    }

    public void Report(string stage, string message)
    {
        if (_inner is not null)
        {
            _inner.Report(stage, message);
        }
        else
        {
            _pending.Add((stage, message));
        }
    }
}
