using System;
using System.Collections.Generic;

using LanMountainDesktop.Models;
using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 状态行文案那条判据的行为钉：哪个状态出哪个词条、以及**谁在前面**。
/// 收口前两个组件各抄一份逐字 25 行（连键都是同一批 <c>study.environment.status.*</c>），
/// 抄本漂开的症状是同一场监控在两块面板上写着不同的话——顺序错一格就不报错，只是把"出错"说成"吵"。
/// </summary>
public sealed class StudyNoiseStatusTextTests
{
    private static readonly StudyAnalyticsSnapshot Base = new(
        State: StudyAnalyticsRuntimeState.Ready,
        StreamStatus: NoiseStreamStatus.Quiet,
        DataMode: StudyDataMode.Realtime,
        Config: new StudyAnalyticsConfig(),
        LatestRealtimePoint: null,
        LatestSlice: null,
        RealtimeBuffer: [],
        Session: new StudySessionSnapshot(
            State: StudySessionRuntimeState.Idle,
            SessionId: null,
            Label: string.Empty,
            StartedAt: null,
            EndedAt: null,
            Elapsed: TimeSpan.Zero,
            Metrics: new StudySessionMetrics(
                CurrentScore: 0,
                AvgScore: 0,
                MinScore: 0,
                MaxScore: 0,
                WeightedOverRatioDbfs: 0,
                TotalSegmentCount: 0,
                EffectiveDuration: TimeSpan.Zero,
                SliceCount: 0),
            LastError: string.Empty),
        LastSessionReport: null,
        SelectedSessionReportId: null,
        SessionHistory: [],
        LastError: string.Empty);

    private static string KeyOf(StudyAnalyticsRuntimeState state, NoiseStreamStatus stream)
    {
        var picked = string.Empty;
        StudyNoiseStatusText.Describe(
            Base with { State = state, StreamStatus = stream },
            (key, fallback) =>
            {
                picked = key;
                return fallback;
            });

        return picked;
    }

    [Theory]
    [InlineData(StudyAnalyticsRuntimeState.Unsupported, NoiseStreamStatus.Error, "study.environment.status.unsupported")]
    [InlineData(StudyAnalyticsRuntimeState.Error, NoiseStreamStatus.Quiet, "study.environment.status.error")]
    [InlineData(StudyAnalyticsRuntimeState.Running, NoiseStreamStatus.Error, "study.environment.status.error")]
    [InlineData(StudyAnalyticsRuntimeState.Paused, NoiseStreamStatus.Noisy, "study.environment.status.paused")]
    [InlineData(StudyAnalyticsRuntimeState.Running, NoiseStreamStatus.Noisy, "study.environment.status.noisy")]
    [InlineData(StudyAnalyticsRuntimeState.Running, NoiseStreamStatus.Quiet, "study.environment.status.quiet")]
    [InlineData(StudyAnalyticsRuntimeState.Ready, NoiseStreamStatus.Noisy, "study.environment.status.noisy")]
    [InlineData(StudyAnalyticsRuntimeState.Running, NoiseStreamStatus.Initializing, "study.environment.status.initializing")]
    public void Describe_PicksTheKeyInThisOrder(
        StudyAnalyticsRuntimeState state,
        NoiseStreamStatus stream,
        string expectedKey) =>
        Assert.Equal(expectedKey, KeyOf(state, stream));

    [Fact]
    public void Describe_HandsTheEnglishFallbackToTheCaller()
    {
        var fallbacks = new List<string>();

        var text = StudyNoiseStatusText.Describe(
            Base with { State = StudyAnalyticsRuntimeState.Unsupported, StreamStatus = NoiseStreamStatus.Quiet },
            (key, fallback) =>
            {
                fallbacks.Add(fallback);
                return "不支持";
            });

        Assert.Equal(["Unsupported"], fallbacks);
        Assert.Equal("不支持", text);
    }
}
