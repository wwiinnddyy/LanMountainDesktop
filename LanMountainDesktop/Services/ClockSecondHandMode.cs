using System;

namespace LanMountainDesktop.Services;

public static class ClockSecondHandMode
{
    public const string Tick = "Tick";
    public const string Sweep = "Sweep";

    public static string Normalize(string? mode)
    {
        return string.Equals(mode?.Trim(), Sweep, StringComparison.OrdinalIgnoreCase)
            ? Sweep
            : Tick;
    }

    public static bool IsSweep(string? mode)
    {
        return string.Equals(Normalize(mode), Sweep, StringComparison.OrdinalIgnoreCase);
    }
}
