using System;
using System.Collections.Generic;

namespace LanMontainDesktop.Models;

public sealed record ClassIslandScheduleReadResult(
    bool Success,
    ClassIslandScheduleSnapshot? Snapshot,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    IReadOnlyList<string>? Warnings = null)
{
    public static ClassIslandScheduleReadResult Ok(
        ClassIslandScheduleSnapshot snapshot,
        IReadOnlyList<string>? warnings = null)
    {
        return new ClassIslandScheduleReadResult(true, snapshot, Warnings: warnings);
    }

    public static ClassIslandScheduleReadResult Fail(
        string errorCode,
        string errorMessage,
        IReadOnlyList<string>? warnings = null)
    {
        return new ClassIslandScheduleReadResult(false, null, errorCode, errorMessage, warnings);
    }
}

public sealed record ClassIslandScheduleSnapshot(
    string SourceRootPath,
    string ProfilePath,
    string ProfileFileName,
    DateTimeOffset LoadedAt,
    ClassIslandScheduleCycleRule CycleRule,
    Guid? SelectedClassPlanGroupId,
    Guid? TempClassPlanGroupId,
    bool IsTempClassPlanGroupEnabled,
    DateOnly? TempClassPlanGroupExpireDate,
    int TempClassPlanGroupType,
    Guid? TempClassPlanId,
    DateOnly? TempClassPlanSetupDate,
    bool IsOverlayClassPlanEnabled,
    Guid? OverlayClassPlanId,
    IReadOnlyDictionary<Guid, ClassIslandSubject> Subjects,
    IReadOnlyDictionary<Guid, ClassIslandTimeLayout> TimeLayouts,
    IReadOnlyDictionary<Guid, ClassIslandClassPlan> ClassPlans,
    IReadOnlyDictionary<DateOnly, Guid> OrderedSchedules,
    IReadOnlyDictionary<Guid, ClassIslandClassPlanGroup> ClassPlanGroups);

public sealed record ClassIslandScheduleCycleRule(
    DateOnly? SingleWeekStartDate,
    int MultiWeekRotationMaxCycle,
    IReadOnlyList<int> MultiWeekRotationOffset);

public sealed record ClassIslandSubject(
    Guid Id,
    string Name,
    string? Initial,
    string? TeacherName,
    bool? IsOutDoor);

public sealed record ClassIslandTimeLayout(
    Guid Id,
    string Name,
    IReadOnlyList<ClassIslandTimeLayoutItem> Items);

public sealed record ClassIslandTimeLayoutItem(
    TimeSpan StartTime,
    TimeSpan EndTime,
    int TimeType,
    bool IsHiddenByDefault,
    Guid? DefaultSubjectId,
    string? BreakName);

public sealed record ClassIslandClassPlan(
    Guid Id,
    string Name,
    Guid TimeLayoutId,
    ClassIslandTimeRule Rule,
    IReadOnlyList<ClassIslandClassInfo> Classes,
    bool IsEnabled,
    bool IsOverlay,
    Guid? OverlaySourceId,
    DateOnly? OverlaySetupDate,
    Guid? AssociatedGroupId);

public sealed record ClassIslandClassInfo(
    Guid? SubjectId,
    bool IsEnabled);

public sealed record ClassIslandTimeRule(
    int WeekDay,
    int WeekCountDiv,
    int WeekCountDivTotal);

public sealed record ClassIslandClassPlanGroup(
    Guid Id,
    string Name,
    bool IsGlobal);

public sealed record ClassIslandResolvedClassPlan(
    Guid ClassPlanId,
    ClassIslandClassPlan ClassPlan,
    string Source);
