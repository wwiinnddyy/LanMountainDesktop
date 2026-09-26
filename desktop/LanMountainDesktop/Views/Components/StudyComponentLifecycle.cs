using Avalonia.Controls;
using Avalonia.Media;

using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习组件生命周期里被各抄一遍的不变量，目前有四件：挂载那五步、detach 的固定四步、
/// 活跃页上下文的固定三步、尺寸变了要重算的那两件事。
/// detach 那件是：落"未挂载"状态位 → 放监测租约 → 清渲染门 → 退订快照事件。
/// 顺序是有意的：租约与退订都发生在 _isAttached=false 之后，反过会让协调器以为还有活着的页面。
/// 这四步此前在 6 个学习组件里逐字各抄一份——漏抄最后一行就是一个还在被回调的事件泄漏
/// （本仓真发生过两次：组件库预览换选中项、组件浮窗关停）。
/// </summary>
internal static class StudyComponentLifecycle
{
    internal static void Detach(
        ref bool isAttached,
        ref IDisposable? monitoringLease,
        ref bool isSubscribed,
        IStudyAnalyticsService studyAnalyticsService,
        StudySnapshotRenderGate renderGate)
    {
        isAttached = false;
        StudyMonitoringLease.Release(ref monitoringLease);
        renderGate.Clear();
        StudySnapshotSubscription.Unsubscribe(ref isSubscribed, studyAnalyticsService, renderGate.HandleSnapshotUpdated);
    }

    /// <summary>向渲染门喂一张当下的快照——重画这一个动作只该有一种喂法。</summary>
    internal static void RequestRepaint(
        StudySnapshotRenderGate renderGate,
        IStudyAnalyticsService studyAnalyticsService) =>
        renderGate.Queue(studyAnalyticsService.GetSnapshot());

    /// <summary>
    /// <see cref="Detach"/> 的反向那五步：落"已挂载"状态位 → 重读自己的显示设置 → 订快照事件 →
    /// 重算监测租约 → 按各家口径刷新一次。
    /// <b>状态位必须最先落</b>，这条有后果：<c>StudyMonitoringLease.Sync(…, isAttached, isOnActivePage)</c>
    /// 在 <c>!isAttached</c> 时走的是 <see cref="StudyMonitoringLease.Release"/>（见
    /// <c>Services/StudyAnalyticsMonitoringLeaseCoordinator.cs:110</c>），所以把状态位落在重算之后，
    /// 组件"从桌面摘掉再放回来"那一次就拿不到租约——症状是学习监测不再采数，且不报错。
    /// 订事件与重算租约之间的先后今天换不出差别（两件事不读对方的结果），这里保留五份抄本原本的顺序。
    /// 最后一步各家不同（重画视觉 / 把最新快照排进渲染门 / 还要先复位一次计时器），所以留成实参；
    /// 第一个实参也留成口子：环境面板重读的是 <c>ReloadDisplaySettings</c>，其余四个是 <c>ReloadLanguageCode</c>。
    /// </summary>
    internal static void Attach(
        ref bool isAttached,
        ref bool isSubscribed,
        IStudyAnalyticsService studyAnalyticsService,
        StudySnapshotRenderGate renderGate,
        Action reloadSettings,
        Action updateMonitoringLeaseState,
        Action refresh)
    {
        isAttached = true;
        reloadSettings();
        StudySnapshotSubscription.Subscribe(ref isSubscribed, studyAnalyticsService, renderGate.HandleSnapshotUpdated);
        updateMonitoringLeaseState();
        refresh();
    }

    /// <summary>
    /// 学习组件"在不在活跃页"的固定三步：落状态位 → 重算监测租约 → 只在"从不在到在"的那一刻补一次刷新。
    /// 这六份各抄一遍时唯一的差别就是最后那一下刷新（4 个重画视觉、2 个把最新快照排进渲染门），
    /// 中间的"先重算租约、再判首次进入"顺序没人抄错，也没人测过。
    /// <paramref name="isEditMode"/> 学习组件一律不看，集中在这里吃掉：
    /// 调用方就无需各写一句 <c>_ = isEditMode;</c>，一处能看出"编辑态对学习组件无意义"这个口径。
    /// </summary>
    internal static void ApplyPageContext(
        ref bool cachedIsOnActivePage,
        bool isOnActivePage,
        bool isEditMode,
        Action updateMonitoringLeaseState,
        Action refreshOnFirstEntry)
    {
        _ = isEditMode;

        var wasOnActivePage = cachedIsOnActivePage;
        cachedIsOnActivePage = isOnActivePage;
        updateMonitoringLeaseState();

        if (isOnActivePage && !wasOnActivePage)
        {
            refreshOnFirstEntry();
        }
    }

    /// <summary>
    /// 学习面板"尺寸变了要做哪两件事"的固定两步：先按新尺寸重排，再把<b>重排之后现读的那块面板底色</b>
    /// 交给重绘回调。这五份抄本此前逐字相同（<c>UpdateAdaptiveLayout()</c> +
    /// <c>ApplyTypographyByBackground(StudyPanelPalette.Resolve(this, RootBorder.Background))</c>）。
    /// 两步是同一个判断的两半："尺寸变了就按<b>当下</b>那块面板的底色重算一遍"，只留重排等于把重算跳过。
    ///
    /// 2026-09-26 把 #G1-CD 剩下的两派并了进来，现在<b>八个学习面板里七个走这一条</b>：
    /// <c>StudyNoiseCurveWidget</c> 原来只是换了拼写（重排那一步叫 <c>ApplyCellSize</c>）、
    /// <c>StudySessionHistoryWidget</c> 的重绘走快照那条路，所以回调写成 <c>_ =&gt; RepaintWithSnapshot()</c>
    /// （它自己在 <c>RenderSnapshot</c> 里现取底色，传进来的色它不用）。
    /// 唯一剩下的例外是 <c>StudyEnvironmentWidget</c>：它 resize 只做几何，文字色走 XAML 的
    /// <c>DynamicResource Adaptive*</c>、主题翻档自动跟，"尺寸变了不重算配色"对它自洽——
    /// 这条例外由 <c>StudyVisualRecomputeTests.ResizingHandlers_AllGoThroughTheHome_ExceptTheDocumentedOne</c>
    /// 点名看着，新增面板若自己内联这两步就会红。
    ///
    /// 两步的<b>先后</b>今天在行为上换不出差别——这些面板的重排都不写 <see cref="Border.Background"/>，
    /// 重绘回调也不读重排算出的紧凑档标志；参数收的是面板本身而不是画刷，只为把抄本原本的读点
    /// （重排之后）固定在家里，调用方抄不错，将来重排若开始换底色也不必回来改七处。
    /// </summary>
    internal static void RefreshOnResize(
        Border panel,
        Action reflow,
        Action<Color> repaintAgainstPanelColor)
    {
        reflow();
        repaintAgainstPanelColor(StudyPanelPalette.Resolve(panel, panel.Background));
    }
}
