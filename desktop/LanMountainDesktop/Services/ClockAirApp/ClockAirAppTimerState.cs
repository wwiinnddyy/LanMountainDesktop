using System;

namespace LanMountainDesktop.Services.ClockAirApp;

public sealed class ClockAirAppTimerState
{
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private TimeSpan _remainingBeforeRun = TimeSpan.FromMinutes(5);
    private DateTimeOffset? _startedAt;

    public TimeSpan Duration => _duration;

    public bool IsRunning => _startedAt.HasValue;

    public bool IsCompleted { get; private set; }

    public TimeSpan GetRemaining(DateTimeOffset now)
    {
        if (!_startedAt.HasValue)
        {
            return _remainingBeforeRun < TimeSpan.Zero ? TimeSpan.Zero : _remainingBeforeRun;
        }

        var remaining = _remainingBeforeRun - (now - _startedAt.Value);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void SetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(1);
        }

        _duration = duration;
        Reset();
    }

    public void StartOrResume(DateTimeOffset now)
    {
        if (_startedAt.HasValue)
        {
            return;
        }

        if (_remainingBeforeRun <= TimeSpan.Zero || IsCompleted)
        {
            _remainingBeforeRun = _duration;
            IsCompleted = false;
        }

        _startedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!_startedAt.HasValue)
        {
            return;
        }

        _remainingBeforeRun = GetRemaining(now);
        _startedAt = null;
    }

    public void Reset()
    {
        _remainingBeforeRun = _duration;
        _startedAt = null;
        IsCompleted = false;
    }

    public bool Update(DateTimeOffset now)
    {
        if (!_startedAt.HasValue || GetRemaining(now) > TimeSpan.Zero)
        {
            return false;
        }

        _remainingBeforeRun = TimeSpan.Zero;
        _startedAt = null;
        if (IsCompleted)
        {
            return false;
        }

        IsCompleted = true;
        return true;
    }
}
