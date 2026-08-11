using System;

namespace LanMountainDesktop.Models;

public sealed record AttendanceSessionRecord(
    string SessionId,
    string Label,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Status,
    double? Score,
    string? PayloadJson);

public sealed record AttendanceEventRecord(
    string EventId,
    string SessionId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? PayloadJson);

