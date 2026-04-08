using System;
using System.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class StudyAnalyticsServiceTests
{
    [Fact]
    public void SnapshotUpdated_UsesUiPublishThrottle()
    {
        using var recorder = new FakeAudioRecorderService();
        using var service = new StudyAnalyticsService(recorder);
        service.UpdateConfig(new StudyAnalyticsConfig(FrameMs: 20, UiPublishIntervalMs: 120));

        var updateCount = 0;
        service.SnapshotUpdated += (_, _) => Interlocked.Increment(ref updateCount);

        Assert.True(service.StartOrResumeMonitoring());
        Thread.Sleep(280);
        Assert.True(service.PauseMonitoring());

        var totalUpdates = Volatile.Read(ref updateCount);
        Assert.InRange(totalUpdates, 2, 6);
    }

    [Fact]
    public void GetSnapshot_ReusesRealtimeBufferSnapshot_WhenNoNewFramesArrive()
    {
        using var recorder = new FakeAudioRecorderService();
        using var service = new StudyAnalyticsService(recorder);
        service.UpdateConfig(new StudyAnalyticsConfig(FrameMs: 20, UiPublishIntervalMs: 120));

        using var firstUpdate = new ManualResetEventSlim(false);
        service.SnapshotUpdated += (_, args) =>
        {
            if (args.Snapshot.RealtimeBuffer.Count > 0)
            {
                firstUpdate.Set();
            }
        };

        Assert.True(service.StartOrResumeMonitoring());
        Assert.True(firstUpdate.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(service.PauseMonitoring());

        var firstSnapshot = service.GetSnapshot();
        var secondSnapshot = service.GetSnapshot();

        Assert.NotEmpty(firstSnapshot.RealtimeBuffer);
        Assert.Same(firstSnapshot.RealtimeBuffer, secondSnapshot.RealtimeBuffer);
    }

    private sealed class FakeAudioRecorderService : IAudioRecorderService
    {
        private readonly object _syncRoot = new();
        private AudioRecorderRuntimeState _state = AudioRecorderRuntimeState.Ready;

        public AudioRecorderSnapshot GetSnapshot()
        {
            lock (_syncRoot)
            {
                return new AudioRecorderSnapshot(
                    State: _state,
                    Duration: TimeSpan.Zero,
                    InputLevel: _state == AudioRecorderRuntimeState.Recording ? 0.55 : 0,
                    LastSavedFilePath: string.Empty,
                    LastError: string.Empty);
            }
        }

        public bool StartOrResume()
        {
            lock (_syncRoot)
            {
                _state = AudioRecorderRuntimeState.Recording;
                return true;
            }
        }

        public bool Pause()
        {
            lock (_syncRoot)
            {
                _state = AudioRecorderRuntimeState.Paused;
                return true;
            }
        }

        public string? StopAndSave(string? outputPath = null)
        {
            lock (_syncRoot)
            {
                _state = AudioRecorderRuntimeState.Ready;
                return outputPath;
            }
        }

        public void Discard()
        {
            lock (_syncRoot)
            {
                _state = AudioRecorderRuntimeState.Ready;
            }
        }

        public void Dispose()
        {
        }
    }
}
