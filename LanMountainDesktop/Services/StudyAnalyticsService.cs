using System;
using System.Threading;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public static class StudyAnalyticsServiceFactory
{
    private static readonly Lazy<IStudyAnalyticsService> SharedService = new(
        () => new StudyAnalyticsService(),
        isThreadSafe: true);

    public static IStudyAnalyticsService CreateDefault()
    {
        return SharedService.Value;
    }
}

public sealed class StudyAnalyticsService : IStudyAnalyticsService
{
    private readonly object _syncRoot = new();
    private readonly IAudioRecorderService _audioRecorderService;
    private readonly Timer _samplingTimer;
    private readonly NoiseFramePipeline _pipeline;
    private readonly SessionAccumulator _sessionAccumulator = new();

    private StudyAnalyticsConfig _config = new();
    private StudyAnalyticsRuntimeState _state;
    private NoiseStreamStatus _streamStatus = NoiseStreamStatus.Initializing;
    private StudyDataMode _dataMode = StudyDataMode.Realtime;
    private NoiseRealtimePoint? _latestRealtime;
    private NoiseSliceSummary? _latestSlice;
    private StudySessionReport? _lastSessionReport;
    private string _lastError = string.Empty;
    private bool _disposed;

    public StudyAnalyticsService(IAudioRecorderService? audioRecorderService = null)
    {
        _audioRecorderService = audioRecorderService ?? AudioRecorderServiceFactory.CreateDefault();
        _pipeline = new NoiseFramePipeline(_config);
        _samplingTimer = new Timer(OnSamplingTick, null, Timeout.Infinite, Timeout.Infinite);

        var audioSnapshot = _audioRecorderService.GetSnapshot();
        if (audioSnapshot.IsSupported)
        {
            _state = StudyAnalyticsRuntimeState.Ready;
            _streamStatus = NoiseStreamStatus.Quiet;
            _lastError = string.Empty;
        }
        else
        {
            _state = StudyAnalyticsRuntimeState.Unsupported;
            _streamStatus = NoiseStreamStatus.Error;
            _lastError = audioSnapshot.LastError;
        }
    }

    public event EventHandler<StudyAnalyticsSnapshotChangedEventArgs>? SnapshotUpdated;

    public event EventHandler<NoiseSliceClosedEventArgs>? SliceClosed;

    public event EventHandler<StudySessionCompletedEventArgs>? SessionCompleted;

    public StudyAnalyticsSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }
    }

    public StudyAnalyticsConfig GetConfig()
    {
        lock (_syncRoot)
        {
            return _config;
        }
    }

    public void UpdateConfig(StudyAnalyticsConfig config)
    {
        StudyAnalyticsSnapshot snapshot;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            _config = NormalizeConfig(config);
            _pipeline.UpdateConfig(_config);
            if (_state == StudyAnalyticsRuntimeState.Running)
            {
                StartTimerLocked();
            }

            _latestSlice = null;
            UpdateDataModeLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
    }

    public bool StartOrResumeMonitoring()
    {
        StudyAnalyticsSnapshot snapshot;
        bool started;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            started = TryStartMonitoringLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return started;
    }

    public bool PauseMonitoring()
    {
        StudyAnalyticsSnapshot snapshot;
        bool paused;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            if (_state != StudyAnalyticsRuntimeState.Running)
            {
                return false;
            }

            if (!_audioRecorderService.Pause())
            {
                _state = StudyAnalyticsRuntimeState.Error;
                _streamStatus = NoiseStreamStatus.Error;
                _lastError = _audioRecorderService.GetSnapshot().LastError;
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
                paused = false;
            }
            else
            {
                StopTimerLocked();
                _state = StudyAnalyticsRuntimeState.Paused;
                _lastError = string.Empty;
                UpdateDataModeLocked();
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
                paused = true;
            }
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return paused;
    }

    public bool StopMonitoring()
    {
        StudyAnalyticsSnapshot snapshot;
        StudySessionReport? finishedReport = null;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            if (_state is StudyAnalyticsRuntimeState.Unsupported)
            {
                return false;
            }

            _audioRecorderService.Discard();
            StopTimerLocked();
            _pipeline.Reset();
            _latestRealtime = null;
            _latestSlice = null;
            _state = StudyAnalyticsRuntimeState.Ready;
            _streamStatus = NoiseStreamStatus.Quiet;
            _lastError = string.Empty;

            if (_sessionAccumulator.IsRunning)
            {
                finishedReport = _sessionAccumulator.Stop(DateTimeOffset.UtcNow);
                _lastSessionReport = finishedReport;
            }

            UpdateDataModeLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        if (finishedReport is not null)
        {
            SessionCompleted?.Invoke(this, new StudySessionCompletedEventArgs(finishedReport));
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return true;
    }

    public bool StartStudySession(StudySessionOptions? options = null)
    {
        StudyAnalyticsSnapshot snapshot;
        bool started;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            if (_sessionAccumulator.IsRunning)
            {
                return false;
            }

            if (!TryStartMonitoringLocked())
            {
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
                started = false;
            }
            else
            {
                var normalizedOptions = options ?? new StudySessionOptions();
                if (!_sessionAccumulator.Start(DateTimeOffset.UtcNow, normalizedOptions))
                {
                    return false;
                }

                _lastSessionReport = null;
                UpdateDataModeLocked();
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
                started = true;
            }
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return started;
    }

    public bool StopStudySession()
    {
        StudySessionReport? report;
        StudyAnalyticsSnapshot snapshot;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            report = _sessionAccumulator.Stop(DateTimeOffset.UtcNow);
            if (report is null)
            {
                return false;
            }

            _lastSessionReport = report;
            UpdateDataModeLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        SessionCompleted?.Invoke(this, new StudySessionCompletedEventArgs(report));
        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return true;
    }

    public bool CancelStudySession()
    {
        StudyAnalyticsSnapshot snapshot;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            if (!_sessionAccumulator.Cancel())
            {
                return false;
            }

            UpdateDataModeLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        return true;
    }

    public void ClearLastSessionReport()
    {
        StudyAnalyticsSnapshot snapshot;
        lock (_syncRoot)
        {
            ThrowIfDisposedLocked();
            _lastSessionReport = null;
            UpdateDataModeLocked();
            snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
        }

        SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopTimerLocked();
            _samplingTimer.Dispose();
        }
    }

    private void OnSamplingTick(object? state)
    {
        StudyAnalyticsSnapshot? snapshot = null;
        NoiseSliceSummary? closedSlice = null;
        lock (_syncRoot)
        {
            if (_disposed || _state != StudyAnalyticsRuntimeState.Running)
            {
                return;
            }

            var audioSnapshot = _audioRecorderService.GetSnapshot();
            if (!audioSnapshot.IsSupported)
            {
                _state = StudyAnalyticsRuntimeState.Unsupported;
                _streamStatus = NoiseStreamStatus.Error;
                _lastError = audioSnapshot.LastError;
                StopTimerLocked();
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
            }
            else if (audioSnapshot.State == AudioRecorderRuntimeState.Error)
            {
                _state = StudyAnalyticsRuntimeState.Error;
                _streamStatus = NoiseStreamStatus.Error;
                _lastError = string.IsNullOrWhiteSpace(audioSnapshot.LastError)
                    ? "Audio recorder returned an error state."
                    : audioSnapshot.LastError;
                StopTimerLocked();
                snapshot = BuildSnapshotLocked(DateTimeOffset.UtcNow);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                var rms = Math.Clamp(audioSnapshot.InputLevel, 0, 1);
                var dbfs = ConvertInputLevelToDbfs(rms, _config.SilenceFloorDbfs);
                var displayDb = ComputeDisplayDb(dbfs, _config);
                var tickResult = _pipeline.AddFrame(
                    now,
                    rms,
                    dbfs,
                    displayDb,
                    peak: rms);

                _latestRealtime = tickResult.RealtimePoint;
                _streamStatus = tickResult.RealtimePoint.IsOverThreshold
                    ? NoiseStreamStatus.Noisy
                    : NoiseStreamStatus.Quiet;

                if (tickResult.ClosedSlice is not null)
                {
                    closedSlice = tickResult.ClosedSlice;
                    _latestSlice = closedSlice;
                    if (_sessionAccumulator.IsRunning)
                    {
                        _sessionAccumulator.AddSlice(closedSlice);
                    }
                }

                _lastError = string.Empty;
                UpdateDataModeLocked();
                snapshot = BuildSnapshotLocked(now);
            }
        }

        if (snapshot is not null)
        {
            SnapshotUpdated?.Invoke(this, new StudyAnalyticsSnapshotChangedEventArgs(snapshot));
        }

        if (closedSlice is not null)
        {
            SliceClosed?.Invoke(this, new NoiseSliceClosedEventArgs(closedSlice));
        }
    }

    private bool TryStartMonitoringLocked()
    {
        if (_state == StudyAnalyticsRuntimeState.Unsupported)
        {
            return false;
        }

        if (_state == StudyAnalyticsRuntimeState.Running)
        {
            return true;
        }

        if (!_audioRecorderService.StartOrResume())
        {
            _state = StudyAnalyticsRuntimeState.Error;
            _streamStatus = NoiseStreamStatus.Error;
            _lastError = _audioRecorderService.GetSnapshot().LastError;
            return false;
        }

        _state = StudyAnalyticsRuntimeState.Running;
        _streamStatus = NoiseStreamStatus.Quiet;
        _lastError = string.Empty;
        StartTimerLocked();
        UpdateDataModeLocked();
        return true;
    }

    private void StartTimerLocked()
    {
        _samplingTimer.Change(
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromMilliseconds(_config.FrameMs));
    }

    private void StopTimerLocked()
    {
        _samplingTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void UpdateDataModeLocked()
    {
        if (_sessionAccumulator.IsRunning)
        {
            _dataMode = StudyDataMode.SessionRunning;
            return;
        }

        _dataMode = _lastSessionReport is null
            ? StudyDataMode.Realtime
            : StudyDataMode.SessionReport;
    }

    private StudyAnalyticsSnapshot BuildSnapshotLocked(DateTimeOffset now)
    {
        return new StudyAnalyticsSnapshot(
            State: _state,
            StreamStatus: _streamStatus,
            DataMode: _dataMode,
            Config: _config,
            LatestRealtimePoint: _latestRealtime,
            LatestSlice: _latestSlice,
            RealtimeBuffer: _pipeline.GetRealtimeBufferSnapshot(),
            Session: _sessionAccumulator.GetSnapshot(now),
            LastSessionReport: _lastSessionReport,
            LastError: _lastError);
    }

    private static double ConvertInputLevelToDbfs(double level, double silenceFloorDbfs)
    {
        var clampedLevel = Math.Clamp(level, 0, 1);
        if (clampedLevel <= 1e-5)
        {
            return silenceFloorDbfs;
        }

        var dbfs = 20d * Math.Log10(clampedLevel);
        return Math.Clamp(dbfs, silenceFloorDbfs, 0);
    }

    private static double ComputeDisplayDb(double dbfs, StudyAnalyticsConfig config)
    {
        // Keep score and calibration decoupled: scoring uses dBFS, display maps it to user-facing dB.
        var referenceDelta = dbfs - config.ScoreThresholdDbfs;
        return Math.Round(config.BaselineDb + referenceDelta, 2);
    }

    private static StudyAnalyticsConfig NormalizeConfig(StudyAnalyticsConfig config)
    {
        var frameMs = Math.Clamp(config.FrameMs, 20, 250);
        var sliceSec = Math.Clamp(config.SliceSec, 5, 600);
        var threshold = Math.Clamp(config.ScoreThresholdDbfs, -100, -5);
        var mergeGapMs = Math.Clamp(config.SegmentMergeGapMs, 100, 4000);
        var maxSegments = Math.Clamp(config.MaxSegmentsPerMin, 1, 40);
        var silenceFloor = Math.Clamp(config.SilenceFloorDbfs, -100, -20);
        var baselineDb = Math.Clamp(config.BaselineDb, 20, 90);
        var avgWindowSec = Math.Clamp(config.AvgWindowSec, 1, 8);
        var ringCapacity = Math.Clamp(config.RealtimeBufferCapacity, 60, 1200);

        return config with
        {
            FrameMs = frameMs,
            SliceSec = sliceSec,
            ScoreThresholdDbfs = threshold,
            SegmentMergeGapMs = mergeGapMs,
            MaxSegmentsPerMin = maxSegments,
            SilenceFloorDbfs = silenceFloor,
            BaselineDb = baselineDb,
            AvgWindowSec = avgWindowSec,
            RealtimeBufferCapacity = ringCapacity
        };
    }

    private void ThrowIfDisposedLocked()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(StudyAnalyticsService));
        }
    }
}
