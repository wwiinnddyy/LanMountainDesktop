using System;

using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 噪声/环境两块组件的状态行文案：状态机 → 本地化键，唯一一份。
/// 此前两个组件各抄一份逐字相同的 25 行，**连本地化键都是同一批 `study.environment.status.*`**——
/// 也就是说噪声曲线组件一直在显示环境组件的那套词条。抄本漂开的症状不是崩：
/// 同一场监控在两块面板上写着不同的话，或者某条新状态只在一块面板上有词。
///
/// 判定顺序是有意义的（不支持 > 出错 > 暂停 > 吵 > 运行且安静 > 就绪 > 其余算初始化），
/// 所以家收整段判据而不是只收那张键表；取词交给调用方的 <c>localize</c>，
/// 家不依赖本地化服务，才测得了每一条分支。
/// </summary>
internal static class StudyNoiseStatusText
{
    public static string Describe(StudyAnalyticsSnapshot snapshot, Func<string, string, string> localize)
{
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return localize("study.environment.status.unsupported", "Unsupported");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return localize("study.environment.status.error", "Error");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Paused)
        {
            return localize("study.environment.status.paused", "Paused");
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return localize("study.environment.status.noisy", "Noisy");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return localize("study.environment.status.quiet", "Quiet");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Ready)
        {
            return localize("study.environment.status.ready", "Ready");
        }

        return localize("study.environment.status.initializing", "Initializing");
        }
}
