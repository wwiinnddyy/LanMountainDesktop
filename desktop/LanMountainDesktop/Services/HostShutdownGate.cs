namespace LanMountainDesktop.Services;

internal enum HostShutdownMode
{
    Exit = 0,
    Restart = 1
}

internal readonly record struct HostShutdownSubmission(
    bool Accepted,
    bool IsFirstSubmission,
    HostShutdownMode EffectiveMode,
    HostShutdownMode RequestedMode);

internal sealed class HostShutdownGate
{
    private readonly object _gate = new();
    private bool _submitted;
    private HostShutdownMode _mode;

    public bool IsShutdownRequested
    {
        get
        {
            lock (_gate)
            {
                return _submitted;
            }
        }
    }

    public HostShutdownMode? EffectiveMode
    {
        get
        {
            lock (_gate)
            {
                return _submitted ? _mode : null;
            }
        }
    }

    public HostShutdownSubmission Submit(HostShutdownMode requestedMode)
    {
        lock (_gate)
        {
            if (!_submitted)
            {
                _submitted = true;
                _mode = requestedMode;
                return new HostShutdownSubmission(
                    Accepted: true,
                    IsFirstSubmission: true,
                    EffectiveMode: requestedMode,
                    RequestedMode: requestedMode);
            }

            return new HostShutdownSubmission(
                Accepted: _mode == requestedMode,
                IsFirstSubmission: false,
                EffectiveMode: _mode,
                RequestedMode: requestedMode);
        }
    }
}
