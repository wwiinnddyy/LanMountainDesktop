# 遥测系统规范化改进实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Sentry/PostHog 遥测系统的数据一致性问题，添加中文可读标签，规范化上报数据格式，补充缺失业务事件。

**Architecture:** 保持现有三个服务（SentryCrashTelemetryService、PostHogUsageTelemetryService、TelemetryIdentityService）的架构不变，在各服务内部进行数据修复和增强。新增 TelemetryEventNames 静态类统一管理事件名和中文显示名，新增 TelemetryEnvironmentInfo 增强方法。

**Tech Stack:** C# / .NET 8 / Sentry 6.4.1 / PostHog 2.6.0 / Avalonia UI

---

## 文件变更地图

| 文件 | 操作 | 职责 |
|------|------|------|
| `LanMountainDesktop/Services/TelemetryEventNames.cs` | **新建** | 统一管理所有事件名和中文显示名 |
| `LanMountainDesktop/Services/TelemetryEnvironmentInfo.cs` | 修改 | 增强环境信息采集、修复重复方法 |
| `LanMountainDesktop/Services/SentryCrashTelemetryService.cs` | 修改 | 修复 Tags/Extras 冗余、添加中文标签、修复 PII、增加业务上下文 |
| `LanMountainDesktop/Services/PostHogUsageTelemetryService.cs` | 修改 | 修复 distinct_id 不一致、修复 Session 生命周期、添加中文标签、优化 Flush、增强 DescribePlacement |
| `LanMountainDesktop/Views/MainWindow.axaml.cs` | 修改 | 添加 Session 生命周期调用 |
| `LanMountainDesktop/App.axaml.cs` | 修改 | 添加 Session 结束调用 |

---

## Task 1: 新建 TelemetryEventNames 统一事件名管理

**Files:**
- Create: `LanMountainDesktop/Services/TelemetryEventNames.cs`

- [ ] **Step 1: 创建 TelemetryEventNames.cs**

```csharp
using System.Collections.Generic;

namespace LanMountainDesktop.Services;

internal static class TelemetryEventNames
{
    internal static string DisplayName(string eventName) =>
        EventDisplayNames.TryGetValue(eventName, out var displayName)
            ? displayName
            : eventName;

    internal const string AppFirstLaunch = "app_first_launch";
    internal const string AppSessionStart = "app_session_start";
    internal const string AppSessionEnd = "app_session_end";
    internal const string MainWindowOpened = "main_window_opened";
    internal const string MainWindowClosed = "main_window_closed";
    internal const string SettingsWindowOpened = "settings_window_opened";
    internal const string SettingsWindowClosed = "settings_window_closed";
    internal const string SettingsNavigation = "settings_navigation";
    internal const string SettingsDrawerOpened = "settings_drawer_opened";
    internal const string SettingsDrawerClosed = "settings_drawer_closed";
    internal const string DesktopComponentPlaced = "desktop_component_placed";
    internal const string DesktopComponentMoved = "desktop_component_moved";
    internal const string DesktopComponentResized = "desktop_component_resized";
    internal const string DesktopComponentDeleted = "desktop_component_deleted";
    internal const string DesktopComponentEditorOpened = "desktop_component_editor_opened";
    internal const string ThemeChanged = "theme_changed";
    internal const string PluginInstalled = "plugin_installed";
    internal const string PluginUninstalled = "plugin_uninstalled";
    internal const string PluginEnabled = "plugin_enabled";
    internal const string PluginDisabled = "plugin_disabled";
    internal const string UpdateChecked = "update_checked";
    internal const string UpdateInstalled = "update_installed";
    internal const string AppCrash = "app_crash";

    internal const string SentryUnhandledException = "unhandled_exception";
    internal const string SentryTaskException = "task_exception";
    internal const string SentryShutdown = "shutdown";

    private static readonly Dictionary<string, string> EventDisplayNames = new()
    {
        [AppFirstLaunch] = "应用首次启动",
        [AppSessionStart] = "会话开始",
        [AppSessionEnd] = "会话结束",
        [MainWindowOpened] = "主窗口打开",
        [MainWindowClosed] = "主窗口关闭",
        [SettingsWindowOpened] = "设置窗口打开",
        [SettingsWindowClosed] = "设置窗口关闭",
        [SettingsNavigation] = "设置页导航",
        [SettingsDrawerOpened] = "设置抽屉打开",
        [SettingsDrawerClosed] = "设置抽屉关闭",
        [DesktopComponentPlaced] = "桌面组件放置",
        [DesktopComponentMoved] = "桌面组件移动",
        [DesktopComponentResized] = "桌面组件缩放",
        [DesktopComponentDeleted] = "桌面组件删除",
        [DesktopComponentEditorOpened] = "组件编辑器打开",
        [ThemeChanged] = "主题变更",
        [PluginInstalled] = "插件安装",
        [PluginUninstalled] = "插件卸载",
        [PluginEnabled] = "插件启用",
        [PluginDisabled] = "插件禁用",
        [UpdateChecked] = "更新检查",
        [UpdateInstalled] = "更新安装",
        [AppCrash] = "应用崩溃",
        [SentryUnhandledException] = "未处理异常",
        [SentryTaskException] = "任务异常",
        [SentryShutdown] = "应用关闭"
    };
}
```

---

## Task 2: 增强 TelemetryEnvironmentInfo

**Files:**
- Modify: `LanMountainDesktop/Services/TelemetryEnvironmentInfo.cs`

- [ ] **Step 1: 修复 GetClrVersion 重复问题，增加 GetScreenInfo、GetRenderMode、GetSystemLanguageDisplayName**

在 `TelemetryEnvironmentInfo.cs` 中：

1. 修改 `GetClrVersion()` 使其返回实际的 CLR 信息而非与 `GetRuntimeVersion()` 重复：

```csharp
public static string GetClrVersion()
{
    try
    {
        return System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion() ?? "Unknown";
    }
    catch
    {
        return "Unknown";
    }
}
```

2. 新增 `GetScreenInfo()` 方法：

```csharp
public static string GetScreenInfo()
{
    try
    {
        var screenList = new List<string>();
        foreach (var screen in Avalonia.Controls.Screens.All)
        {
            screenList.Add($"{screen.Bounds.Width}x{screen.Bounds.Height}@{screen.Scaling:F1}x");
        }
        return screenList.Count > 0 ? string.Join("; ", screenList) : "Unknown";
    }
    catch
    {
        return "Unknown";
    }
}
```

注意：由于 `TelemetryEnvironmentInfo` 是 `internal static` 类且可能在 UI 线程之外调用，`Screens` API 需要 UI 线程。因此改用更安全的方式：

```csharp
public static string GetScreenInfo()
{
    return "requires_ui_thread";
}
```

并提供一个可从 UI 线程调用的重载：

```csharp
public static string GetScreenInfoFromUiThread(Avalonia.Controls.TopLevel? topLevel)
{
    try
    {
        var screens = topLevel?.Screens;
        if (screens is null)
        {
            return "Unknown";
        }

        var screenList = new List<string>();
        foreach (var screen in screens.All)
        {
            screenList.Add($"{screen.Bounds.Width}x{screen.Bounds.Height}@{screen.Scaling:F1}x");
        }
        return screenList.Count > 0 ? string.Join("; ", screenList) : "Unknown";
    }
    catch
    {
        return "Unknown";
    }
}
```

3. 新增 `GetSystemLanguageDisplayName()` 方法：

```csharp
public static string GetSystemLanguageDisplayName()
{
    try
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.NativeName ?? culture.Name ?? "Unknown";
    }
    catch
    {
        return "Unknown";
    }
}
```

4. 新增 `GetRenderMode()` 方法：

```csharp
public static string GetRenderMode()
{
    return Program.StartupRenderMode ?? "Unknown";
}
```

注意：`Program.StartupRenderMode` 已是 `internal static`，同项目内可直接访问。

---

## Task 3: 修复 SentryCrashTelemetryService — Tags/Extras 冗余、中文标签、PII、业务上下文

**Files:**
- Modify: `LanMountainDesktop/Services/SentryCrashTelemetryService.cs`

- [ ] **Step 1: 修改 EnableSentry 方法 — 关闭 SendDefaultPii**

将第 212 行：
```csharp
options.SendDefaultPii = true;
```
改为：
```csharp
options.SendDefaultPii = false;
```

- [ ] **Step 2: 重写 ApplyCommonScope 方法 — 消除 Tags/Extras 冗余，添加中文标签和业务上下文**

将整个 `ApplyCommonScope` 方法（第 289-346 行）替换为：

```csharp
private void ApplyCommonScope(Scope scope, string source, string eventType, bool includeLogTail)
{
    var installId = TelemetryIdentityService.Instance.InstallId;
    var telemetryId = TelemetryIdentityService.Instance.TelemetryId;

    scope.User = new SentryUser
    {
        Id = telemetryId
    };

    scope.SetTag("telemetry_channel", "sentry");
    scope.SetTag("event_type", eventType);
    scope.SetTag("event_display_name", TelemetryEventNames.DisplayName(eventType));
    scope.SetTag("source", source);
    scope.SetTag("app_version", TelemetryEnvironmentInfo.GetAppVersion());
    scope.SetTag("environment", TelemetryEnvironmentInfo.GetEnvironment());
    scope.SetTag("os_name", TelemetryEnvironmentInfo.GetOsName());
    scope.SetTag("os_version", TelemetryEnvironmentInfo.GetOsVersion());
    scope.SetTag("language", TelemetryEnvironmentInfo.GetSystemLanguage());

    scope.SetExtra("install_id", installId);
    scope.SetExtra("telemetry_id", telemetryId);
    scope.SetExtra("app_version", TelemetryEnvironmentInfo.GetAppVersion());
    scope.SetExtra("environment", TelemetryEnvironmentInfo.GetEnvironment());
    scope.SetExtra("os_name", TelemetryEnvironmentInfo.GetOsName());
    scope.SetExtra("os_version", TelemetryEnvironmentInfo.GetOsVersion());
    scope.SetExtra("os_build", TelemetryEnvironmentInfo.GetOsBuild());
    scope.SetExtra("device_model", TelemetryEnvironmentInfo.GetDeviceModel());
    scope.SetExtra("device_arch", TelemetryEnvironmentInfo.GetDeviceArchitecture());
    scope.SetExtra("processor_count", TelemetryEnvironmentInfo.GetProcessorCount());
    scope.SetExtra("total_memory_mb", TelemetryEnvironmentInfo.GetTotalMemoryMB());
    scope.SetExtra("runtime_version", TelemetryEnvironmentInfo.GetRuntimeVersion());
    scope.SetExtra("clr_version", TelemetryEnvironmentInfo.GetClrVersion());
    scope.SetExtra("language", TelemetryEnvironmentInfo.GetSystemLanguage());
    scope.SetExtra("language_display_name", TelemetryEnvironmentInfo.GetSystemLanguageDisplayName());
    scope.SetExtra("render_mode", TelemetryEnvironmentInfo.GetRenderMode());
    scope.SetExtra("log_file_path", AppLogger.LogFilePath);

    if (includeLogTail)
    {
        var logTail = ReadLogTail(maxLines: 200, maxCharacters: 32_768);
        if (!string.IsNullOrWhiteSpace(logTail))
        {
            scope.SetExtra("log_tail", logTail);
            scope.SetExtra("log_tail_line_count", logTail.Count(character => character == '\n') + 1);
            scope.AddAttachment(
                Encoding.UTF8.GetBytes(logTail),
                "log-tail.txt",
                contentType: "text/plain");
        }
    }
}
```

关键变更：
- Tags 只保留用于过滤/索引的核心字段（6 个），移除 `install_id`、`telemetry_id`、`os_build`、`device_model`、`device_arch`、`processor_count`、`total_memory_mb`、`runtime_version`、`clr_version` 等非索引字段
- Extras 保留所有详细上下文信息
- 新增 `event_display_name` Tag（中文显示名）
- 新增 `language_display_name`、`render_mode` Extra
- 移除 `IpAddr = AutoIpAddress`（配合 SendDefaultPii = false）

- [ ] **Step 3: 修改 CaptureUnhandledException 方法 — 使用 TelemetryEventNames 常量**

将第 107 行：
```csharp
ApplyCommonScope(scope, source, "unhandled_exception", includeLogTail: true);
```
改为：
```csharp
ApplyCommonScope(scope, source, TelemetryEventNames.SentryUnhandledException, includeLogTail: true);
```

- [ ] **Step 4: 修改 CaptureTaskException 方法 — 使用 TelemetryEventNames 常量**

将第 139 行：
```csharp
ApplyCommonScope(scope, source, "task_exception", includeLogTail: true);
```
改为：
```csharp
ApplyCommonScope(scope, source, TelemetryEventNames.SentryTaskException, includeLogTail: true);
```

- [ ] **Step 5: 修改 CaptureShutdown 方法 — 使用 TelemetryEventNames 常量**

将第 160 行：
```csharp
ApplyCommonScope(scope, source, "shutdown", includeLogTail: true);
```
改为：
```csharp
ApplyCommonScope(scope, source, TelemetryEventNames.SentryShutdown, includeLogTail: true);
```

同时将第 158 行的硬编码消息：
```csharp
var eventId = SentrySdk.CaptureMessage("application_shutdown", scope =>
```
改为：
```csharp
var eventId = SentrySdk.CaptureMessage(TelemetryEventNames.SentryShutdown, scope =>
```

---

## Task 4: 修复 PostHogUsageTelemetryService — distinct_id 不一致、Session 生命周期、中文标签、Flush 优化、DescribePlacement 增强

**Files:**
- Modify: `LanMountainDesktop/Services/PostHogUsageTelemetryService.cs`

- [ ] **Step 1: 修复 EnsureBaselineEventSent — 统一使用 telemetryId 作为 distinct_id**

将第 314 行：
```csharp
var distinctId = identity.InstallId;
```
改为：
```csharp
var distinctId = identity.TelemetryId;
```

同时将 personProps 中增加 `install_id`（保留为属性但不再作为 distinct_id）：

将 personProps 定义（第 314-324 行）改为：
```csharp
var distinctId = identity.TelemetryId;
var personProps = new Dictionary<string, object?>
{
    ["install_id"] = identity.InstallId,
    ["telemetry_id"] = identity.TelemetryId,
    ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
    ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
    ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
    ["os_build"] = TelemetryEnvironmentInfo.GetOsBuild(),
    ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
    ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
    ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
    ["clr_version"] = TelemetryEnvironmentInfo.GetClrVersion(),
    ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage(),
    ["language_display_name"] = TelemetryEnvironmentInfo.GetSystemLanguageDisplayName(),
    ["render_mode"] = TelemetryEnvironmentInfo.GetRenderMode()
};
```

同时将 `app_first_launch` 事件名改为使用常量：

将第 329 行：
```csharp
"app_first_launch",
```
改为：
```csharp
TelemetryEventNames.AppFirstLaunch,
```

- [ ] **Step 2: 修复 CaptureEvent — 添加中文 event_display_name，优化环境信息重复**

将整个 `CaptureEvent` 方法（第 436-503 行）替换为：

```csharp
private void CaptureEvent(
    string eventName,
    IReadOnlyDictionary<string, object?>? payload = null,
    IReadOnlyDictionary<string, object?>? stateBefore = null,
    IReadOnlyDictionary<string, object?>? stateAfter = null,
    bool forceFlush = false)
{
    if (!_isInitialized || !_isUsageEnabled || !_sessionActive)
    {
        return;
    }

    var identity = TelemetryIdentityService.Instance;
    var distinctId = identity.TelemetryId;
    var seq = Interlocked.Increment(ref _sequence);

    var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["install_id"] = identity.InstallId,
        ["telemetry_id"] = identity.TelemetryId,
        ["session_id"] = _sessionId,
        ["sequence"] = seq,
        ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("o"),
        ["event_display_name"] = TelemetryEventNames.DisplayName(eventName)
    };

    if (payload is not null)
    {
        foreach (var kvp in payload)
        {
            properties[kvp.Key] = kvp.Value;
        }
    }

    if (stateBefore is not null && stateBefore.Count > 0)
    {
        foreach (var kvp in stateBefore)
        {
            properties[$"state_before_{kvp.Key}"] = kvp.Value;
        }
    }

    if (stateAfter is not null && stateAfter.Count > 0)
    {
        foreach (var kvp in stateAfter)
        {
            properties[$"state_after_{kvp.Key}"] = kvp.Value;
        }
    }

    _client.Capture(
        distinctId,
        eventName,
        properties,
        groups: null,
        sendFeatureFlags: false);

    if (forceFlush)
    {
        _ = _client.FlushAsync();
    }
}
```

关键变更：
- 移除每个事件中重复的 `app_version`、`os_name`、`os_version`、`device_model`、`device_arch`、`runtime_version`、`language`（这些已通过 Identify 设置为 person properties）
- 添加 `event_display_name` 属性（中文显示名）
- 移除 `payload_` 前缀，payload 属性直接使用原始 key

- [ ] **Step 3: 修复 StartSession — 使用 TelemetryEventNames 常量，移除重复环境信息**

将 StartSession 方法中的 CaptureEvent 调用（第 362-378 行）改为：

```csharp
CaptureEvent(
    TelemetryEventNames.AppSessionStart,
    new Dictionary<string, object?>
    {
        ["source"] = source,
        ["launch_id"] = _launchId,
        ["session_start_utc"] = _sessionStartUtc.ToString("o"),
        ["local_hour"] = _sessionStartUtc.ToLocalTime().Hour,
        ["day_part"] = TelemetryEnvironmentInfo.GetLocalDayPart(_sessionStartUtc),
        ["timezone"] = TimeZoneInfo.Local.Id
    },
    forceFlush: true);
```

- [ ] **Step 4: 修复 EndSession — 使用 TelemetryEventNames 常量**

将 EndSession 方法中的 CaptureEvent 调用（第 393-404 行）改为：

```csharp
CaptureEvent(
    TelemetryEventNames.AppSessionEnd,
    new Dictionary<string, object?>
    {
        ["source"] = source,
        ["launch_id"] = _launchId,
        ["session_start_utc"] = _sessionStartUtc.ToString("o"),
        ["session_end_utc"] = endUtc.ToString("o"),
        ["duration_ms"] = durationMs,
        ["is_restart"] = isRestart
    },
    forceFlush: true);
```

- [ ] **Step 5: 修改所有 Track* 方法 — 使用 TelemetryEventNames 常量，移除 payload_ 前缀影响**

将所有 Track 方法中的硬编码事件名替换为常量引用：

`TrackMainWindowOpened`（第 105-114 行）：
```csharp
public void TrackMainWindowOpened(string source, bool isVisible, string windowState)
{
    CaptureEvent(
        TelemetryEventNames.MainWindowOpened,
        new Dictionary<string, object?>
        {
            ["source"] = source,
            ["is_visible"] = isVisible,
            ["window_state"] = windowState
        },
        forceFlush: true);
}
```

`TrackMainWindowClosed`（第 116-127 行）：
```csharp
public void TrackMainWindowClosed(string source, bool wasVisible, string windowState)
{
    CaptureEvent(
        TelemetryEventNames.MainWindowClosed,
        new Dictionary<string, object?>
        {
            ["source"] = source,
            ["was_visible"] = wasVisible,
            ["window_state"] = windowState
        },
        forceFlush: true);
}
```

`TrackSettingsWindowOpened`（第 129-139 行）：
```csharp
public void TrackSettingsWindowOpened(string source, string? currentPageId)
{
    CaptureEvent(
        TelemetryEventNames.SettingsWindowOpened,
        new Dictionary<string, object?>
        {
            ["source"] = source,
            ["current_page_id"] = currentPageId
        },
        forceFlush: true);
}
```

`TrackSettingsWindowClosed`（第 141-151 行）：
```csharp
public void TrackSettingsWindowClosed(string source, string? currentPageId)
{
    CaptureEvent(
        TelemetryEventNames.SettingsWindowClosed,
        new Dictionary<string, object?>
        {
            ["source"] = source,
            ["current_page_id"] = currentPageId
        },
        forceFlush: true);
}
```

`TrackSettingsNavigation`（第 153-165 行）：
```csharp
public void TrackSettingsNavigation(string? fromPageId, string? toPageId, string source)
{
    CaptureEvent(
        TelemetryEventNames.SettingsNavigation,
        new Dictionary<string, object?>
        {
            ["source"] = source,
            ["from_page_id"] = fromPageId,
            ["to_page_id"] = toPageId
        },
        stateBefore: CreatePageState(fromPageId),
        stateAfter: CreatePageState(toPageId));
}
```

`TrackSettingsDrawerOpened`（第 167-177 行）：
```csharp
public void TrackSettingsDrawerOpened(string? pageId, string? drawerTitle)
{
    CaptureEvent(
        TelemetryEventNames.SettingsDrawerOpened,
        new Dictionary<string, object?>
        {
            ["page_id"] = pageId,
            ["drawer_title"] = drawerTitle
        },
        forceFlush: true);
}
```

`TrackSettingsDrawerClosed`（第 179-189 行）：
```csharp
public void TrackSettingsDrawerClosed(string? pageId, string? drawerTitle)
{
    CaptureEvent(
        TelemetryEventNames.SettingsDrawerClosed,
        new Dictionary<string, object?>
        {
            ["page_id"] = pageId,
            ["drawer_title"] = drawerTitle
        },
        forceFlush: true);
}
```

`TrackDesktopComponentPlaced`（第 191-201 行）：
```csharp
public void TrackDesktopComponentPlaced(DesktopComponentPlacementSnapshot placement, string source)
{
    CaptureEvent(
        TelemetryEventNames.DesktopComponentPlaced,
        new Dictionary<string, object?>
        {
            ["source"] = source
        },
        stateAfter: DescribePlacement(placement),
        forceFlush: true);
}
```

`TrackDesktopComponentMoved`（第 203-217 行）：
```csharp
public void TrackDesktopComponentMoved(
    DesktopComponentPlacementSnapshot before,
    DesktopComponentPlacementSnapshot after,
    string source)
{
    CaptureEvent(
        TelemetryEventNames.DesktopComponentMoved,
        new Dictionary<string, object?>
        {
            ["source"] = source
        },
        stateBefore: DescribePlacement(before),
        stateAfter: DescribePlacement(after),
        forceFlush: true);
}
```

`TrackDesktopComponentResized`（第 219-233 行）：
```csharp
public void TrackDesktopComponentResized(
    DesktopComponentPlacementSnapshot before,
    DesktopComponentPlacementSnapshot after,
    string source)
{
    CaptureEvent(
        TelemetryEventNames.DesktopComponentResized,
        new Dictionary<string, object?>
        {
            ["source"] = source
        },
        stateBefore: DescribePlacement(before),
        stateAfter: DescribePlacement(after),
        forceFlush: true);
}
```

`TrackDesktopComponentDeleted`（第 235-245 行）：
```csharp
public void TrackDesktopComponentDeleted(DesktopComponentPlacementSnapshot before, string source)
{
    CaptureEvent(
        TelemetryEventNames.DesktopComponentDeleted,
        new Dictionary<string, object?>
        {
            ["source"] = source
        },
        stateBefore: DescribePlacement(before),
        forceFlush: true);
}
```

`TrackDesktopComponentEditorOpened`（第 247-257 行）：
```csharp
public void TrackDesktopComponentEditorOpened(DesktopComponentPlacementSnapshot placement, string source)
{
    CaptureEvent(
        TelemetryEventNames.DesktopComponentEditorOpened,
        new Dictionary<string, object?>
        {
            ["source"] = source
        },
        stateBefore: DescribePlacement(placement),
        forceFlush: true);
}
```

- [ ] **Step 6: 增强 DescribePlacement — 添加 component_name**

将 `DescribePlacement` 方法（第 513-525 行）改为：

```csharp
private static IReadOnlyDictionary<string, object?> DescribePlacement(DesktopComponentPlacementSnapshot placement)
{
    return new Dictionary<string, object?>
    {
        ["placement_id"] = placement.PlacementId,
        ["component_id"] = placement.ComponentId,
        ["component_name"] = placement.ComponentName ?? placement.ComponentId,
        ["page_index"] = placement.PageIndex,
        ["row"] = placement.Row,
        ["column"] = placement.Column,
        ["width_cells"] = placement.WidthCells,
        ["height_cells"] = placement.HeightCells
    };
}
```

注意：这要求 `DesktopComponentPlacementSnapshot` 有 `ComponentName` 属性。如果不存在，需要在 `DesktopComponentPlacementSnapshot.cs` 中添加：

```csharp
public string ComponentName { get; set; } = string.Empty;
```

并在创建 placement snapshot 的地方（`ClonePlacementSnapshot` 方法等）填充该字段。

- [ ] **Step 7: 优化 Flush 策略 — 仅关键事件 forceFlush**

将以下 Track 方法的 `forceFlush: true` 改为 `forceFlush: false`（仅保留 session 和 first_launch 的 forceFlush）：

- `TrackMainWindowOpened` → `forceFlush: false`
- `TrackMainWindowClosed` → `forceFlush: false`
- `TrackSettingsWindowOpened` → `forceFlush: false`
- `TrackSettingsWindowClosed` → `forceFlush: false`
- `TrackSettingsDrawerOpened` → `forceFlush: false`
- `TrackSettingsDrawerClosed` → `forceFlush: false`
- `TrackDesktopComponentPlaced` → `forceFlush: false`
- `TrackDesktopComponentMoved` → `forceFlush: false`
- `TrackDesktopComponentResized` → `forceFlush: false`
- `TrackDesktopComponentDeleted` → `forceFlush: false`
- `TrackDesktopComponentEditorOpened` → `forceFlush: false`

保留 `forceFlush: true` 的：
- `StartSession`（app_session_start）
- `EndSession`（app_session_end）
- `EnsureBaselineEventSent`（app_first_launch）

---

## Task 5: 修复 Session 生命周期 — MainWindow 和 App 层调用

**Files:**
- Modify: `LanMountainDesktop/Views/MainWindow.axaml.cs`
- Modify: `LanMountainDesktop/App.axaml.cs`

- [ ] **Step 1: 在 MainWindow.OnOpened 中添加 TrackSessionStarted 调用**

在 `MainWindow.axaml.cs` 的 `OnOpened` 方法中，在 `TrackMainWindowOpened` 调用之后（约第 519 行），添加：

```csharp
TelemetryServices.Usage?.TrackSessionStarted("MainWindow.OnOpened");
```

- [ ] **Step 2: 在 App.PerformExitCleanup 中确保 TrackSessionEnded 被调用**

在 `App.axaml.cs` 的 `PerformExitCleanup` 方法中，在 `TelemetryServices.Usage?.Shutdown(...)` 调用之前（约第 1202 行），添加：

```csharp
TelemetryServices.Usage?.TrackSessionEnded("App.PerformExitCleanup");
```

---

## Task 6: 为 DesktopComponentPlacementSnapshot 添加 ComponentName 属性

**Files:**
- Modify: `LanMountainDesktop/Models/DesktopComponentPlacementSnapshot.cs`

- [ ] **Step 1: 添加 ComponentName 属性**

在 `DesktopComponentPlacementSnapshot.cs` 中，在 `ComponentId` 属性之后添加：

```csharp
public string ComponentName { get; set; } = string.Empty;
```

- [ ] **Step 2: 搜索所有 ClonePlacementSnapshot 方法，确保 ComponentName 被正确填充**

在 `MainWindow.ComponentSystem.cs` 和 `MainWindow.DesktopEditing.cs` 中的 `ClonePlacementSnapshot` 方法里，需要确保 `ComponentName` 被赋值。搜索项目中所有 `ClonePlacementSnapshot` 的实现，在克隆时同时复制 `ComponentName` 字段。

---

## Task 7: 构建验证

- [ ] **Step 1: 执行 dotnet build 确保编译通过**

Run: `dotnet build LanMountainDesktop.slnx -c Debug`

Expected: Build succeeded, 0 errors

- [ ] **Step 2: 执行 dotnet test 确保测试通过**

Run: `dotnet test LanMountainDesktop.slnx -c Debug`

Expected: All tests pass
