using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Services.ClockAirApp;

public sealed class ClockAirAppStopwatchState
{
    private readonly List<TimeSpan> _laps = [];
    private TimeSpan _elapsedBeforeRun = TimeSpan.Zero;
    private DateTimeOffset? _startedAt;

    public bool IsRunning => _startedAt.HasValue;

    public IReadOnlyList<TimeSpan> Laps => _laps;

    public TimeSpan GetElapsed(DateTimeOffset now)
    {
        return _startedAt.HasValue
            ? _elapsedBeforeRun + (now - _startedAt.Value)
            : _elapsedBeforeRun;
    }

    public void StartOrResume(DateTimeOffset now)
    {
        if (_startedAt.HasValue)
        {
            return;
        }

        _startedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!_startedAt.HasValue)
        {
            return;
        }

        _elapsedBeforeRun = GetElapsed(now);
        _startedAt = null;
    }

    public TimeSpan AddLap(DateTimeOffset now)
    {
        var elapsed = GetElapsed(now);
        _laps.Insert(0, elapsed);
        if (_laps.Count > 50)
        {
            _laps.RemoveRange(50, _laps.Count - 50);
        }

        return elapsed;
    }

    public void Reset()
    {
        _elapsedBeforeRun = TimeSpan.Zero;
        _startedAt = null;
        _laps.Clear();
    }
}
