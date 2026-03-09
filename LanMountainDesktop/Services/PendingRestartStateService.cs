using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Services;

public static class PendingRestartStateService
{
    public const string RenderModeReason = "RenderMode";
    public const string PluginCatalogReason = "PluginCatalog";

    private static readonly object Gate = new();
    private static readonly HashSet<string> PendingReasons = new(StringComparer.OrdinalIgnoreCase);

    public static event Action? StateChanged;

    public static bool HasPendingRestart
    {
        get
        {
            lock (Gate)
            {
                return PendingReasons.Count > 0;
            }
        }
    }

    public static bool HasPendingReason(string reason)
    {
        lock (Gate)
        {
            return PendingReasons.Contains(reason);
        }
    }

    public static void SetPending(string reason, bool pending)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var changed = false;
        lock (Gate)
        {
            changed = pending
                ? PendingReasons.Add(reason)
                : PendingReasons.Remove(reason);
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }
}
