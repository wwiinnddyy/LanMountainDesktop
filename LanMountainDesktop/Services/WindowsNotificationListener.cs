using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

internal sealed class WindowsNotificationListener : IPlatformNotificationListener
{
    private static readonly Type? UserNotificationListenerType =
        ResolveWinRtType("Windows.UI.Notifications.Management.UserNotificationListener");
    private static readonly Type? NotificationKindsType =
        ResolveWinRtType("Windows.UI.Notifications.NotificationKinds");
    private static readonly Type? KnownNotificationBindingsType =
        ResolveWinRtType("Windows.UI.Notifications.KnownNotificationBindings");
    private static readonly Type? AppInfoType =
        ResolveWinRtType("Windows.ApplicationModel.AppInfo");
    private static readonly MethodInfo? AsTaskGenericMethodDefinition = ResolveAsTaskGenericMethod();
    private static readonly MethodInfo? AsStreamForReadMethod = ResolveAsStreamForReadMethod();

    private readonly NotificationListenerService _parent;
    private readonly Dictionary<string, NotificationItem> _lastSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private object? _listener;
    private Task? _pollTask;

    public WindowsNotificationListener(NotificationListenerService parent)
    {
        _parent = parent;
    }

    public async Task<NotificationBoxStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || UserNotificationListenerType is null ||
            NotificationKindsType is null || AsTaskGenericMethodDefinition is null)
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.Unsupported,
                "当前 Windows 版本不支持系统通知监听。",
                "Windows");
        }

        if (!HasPackageIdentity())
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.WaitingForPermission,
                "缺少 Windows 包身份。请使用带通知身份包的安装版本，以便系统授予通知监听权限。",
                "Windows",
                CanRequestPermission: false);
        }

        _listener = GetPropertyValue(UserNotificationListenerType, "Current");
        if (_listener is null)
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.Unsupported,
                "无法创建 Windows 通知监听器。",
                "Windows");
        }

        var accessStatus = ReadAccessStatus(_listener);
        if (!string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase))
        {
            accessStatus = await RequestAccessCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationBoxStatus(
                NotificationBoxServiceState.WaitingForPermission,
                accessStatus.Equals("Denied", StringComparison.OrdinalIgnoreCase)
                    ? "Windows 已拒绝通知监听权限，请在系统设置中允许阑山桌面读取通知。"
                    : "等待用户授予 Windows 通知监听权限。",
                "Windows",
                CanRequestPermission: true);
        }

        await SyncNotificationsAsync(cancellationToken).ConfigureAwait(false);
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);

        return new NotificationBoxStatus(
            NotificationBoxServiceState.Running,
            "Windows 系统通知监听已启动。",
            "Windows");
    }

    public async Task RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is null)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var accessStatus = await RequestAccessCoreAsync(cancellationToken).ConfigureAwait(false);
        _parent.SetStatus(string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase)
            ? new NotificationBoxStatus(NotificationBoxServiceState.Running, "Windows 系统通知监听已启动。", "Windows")
            : new NotificationBoxStatus(
                NotificationBoxServiceState.WaitingForPermission,
                "Windows 通知监听权限尚未授予。",
                "Windows",
                CanRequestPermission: true));

        if (string.Equals(accessStatus, "Allowed", StringComparison.OrdinalIgnoreCase))
        {
            await SyncNotificationsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                await SyncNotificationsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _parent.SetStatus(new NotificationBoxStatus(
                    NotificationBoxServiceState.Degraded,
                    $"Windows 通知同步遇到问题：{ex.Message}",
                    "Windows"));
            }
        }
    }

    private async Task SyncNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        var operation = InvokeMethod(_listener, "GetNotificationsAsync", [ParseNotificationKindsToast()]);
        var notificationsObject = await AwaitWinRtOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        if (notificationsObject is not System.Collections.IEnumerable notifications)
        {
            return;
        }

        var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var notificationObject in notifications)
        {
            var item = await TryMapNotificationAsync(notificationObject, cancellationToken).ConfigureAwait(false);
            if (item is null || string.IsNullOrWhiteSpace(item.SourceNotificationId))
            {
                continue;
            }

            currentIds.Add(item.SourceNotificationId);
            _lastSnapshot[item.SourceNotificationId] = item;
            _parent.AddNotification(item);
        }

        foreach (var removedId in _lastSnapshot.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            _lastSnapshot.Remove(removedId);
            _parent.RemoveNotification(removedId);
        }
    }

    private async Task<NotificationItem?> TryMapNotificationAsync(object? notification, CancellationToken cancellationToken)
    {
        if (notification is null)
        {
            return null;
        }

        try
        {
            var sourceId = ReadUIntProperty(notification, "Id").ToString();
            var creationTime = ReadDateTimeOffsetProperty(notification, "CreationTime") ?? DateTimeOffset.UtcNow;
            var appInfo = GetPropertyValue(notification, "AppInfo");
            var displayInfo = GetPropertyValue(appInfo, "DisplayInfo");
            var appName = ReadStringProperty(displayInfo, "DisplayName");
            var aumid = ReadStringProperty(appInfo, "AppUserModelId");

            if (string.IsNullOrWhiteSpace(aumid))
            {
                aumid = TryReadPackageFamilyName(appInfo);
            }

            var (title, body) = ReadToastText(notification);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(appName))
            {
                appName = SimplifyAppId(aumid);
            }

            var iconBytes = await TryReadAppLogoAsync(displayInfo, cancellationToken).ConfigureAwait(false);

            return new NotificationItem
            {
                Id = $"windows:{sourceId}",
                SourceNotificationId = sourceId,
                Platform = "Windows",
                CaptureMode = "WindowsUserNotificationListener",
                AppId = string.IsNullOrWhiteSpace(aumid) ? appName : aumid,
                AppName = string.IsNullOrWhiteSpace(appName) ? "Windows 应用" : appName,
                Aumid = string.IsNullOrWhiteSpace(aumid) ? null : aumid,
                LaunchTarget = string.IsNullOrWhiteSpace(aumid) ? null : $"shell:AppsFolder\\{aumid}",
                CanActivate = !string.IsNullOrWhiteSpace(aumid),
                Title = title,
                Content = body,
                ReceivedAtUtc = creationTime.ToUniversalTime(),
                ReceivedTime = creationTime.LocalDateTime,
                AppIconBytes = iconBytes
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string Title, string Body) ReadToastText(object notification)
    {
        var notificationPayload = GetPropertyValue(notification, "Notification");
        var visual = GetPropertyValue(notificationPayload, "Visual");
        var toastGeneric = GetPropertyValue(KnownNotificationBindingsType, "ToastGeneric");
        var binding = InvokeMethod(visual, "GetBinding", [toastGeneric]);
        var textElements = InvokeMethod(binding, "GetTextElements", null) as System.Collections.IEnumerable;
        if (textElements is null)
        {
            return (string.Empty, string.Empty);
        }

        var texts = new List<string>();
        foreach (var element in textElements)
        {
            var text = ReadStringProperty(element, "Text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }
        }

        return texts.Count switch
        {
            0 => (string.Empty, string.Empty),
            1 => (texts[0], string.Empty),
            _ => (texts[0], string.Join(Environment.NewLine, texts.Skip(1)))
        };
    }

    private static async Task<byte[]?> TryReadAppLogoAsync(object? displayInfo, CancellationToken cancellationToken)
    {
        if (displayInfo is null || AsStreamForReadMethod is null)
        {
            return null;
        }

        try
        {
            var sizeType = ResolveWinRtType("Windows.Foundation.Size");
            object size = sizeType is not null
                ? Activator.CreateInstance(sizeType, 32d, 32d)!
                : null!;
            if (size is null)
            {
                return null;
            }

            var logoReference = InvokeMethod(displayInfo, "GetLogo", [size]);
            var streamObject = await AwaitWinRtOperationAsync(InvokeMethod(logoReference, "OpenReadAsync", null), cancellationToken)
                .ConfigureAwait(false);
            using var dotnetStream = AsStreamForReadMethod.Invoke(null, [streamObject]) as Stream;
            if (dotnetStream is null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await dotnetStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static object ParseNotificationKindsToast()
    {
        return Enum.Parse(NotificationKindsType!, "Toast");
    }

    private static string ReadAccessStatus(object listener)
    {
        return InvokeMethod(listener, "GetAccessStatus", null)?.ToString() ?? "Unspecified";
    }

    private async Task<string> RequestAccessCoreAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return "Unspecified";
        }

        var result = await AwaitWinRtOperationAsync(InvokeMethod(_listener, "RequestAccessAsync", null), cancellationToken)
            .ConfigureAwait(false);
        return result?.ToString() ?? "Unspecified";
    }

    private static bool HasPackageIdentity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var length = 0;
        var hr = GetCurrentPackageFullName(ref length, null);
        if (hr == AppmodelErrorNoPackage)
        {
            return false;
        }

        if (length <= 0)
        {
            return hr == 0;
        }

        var builder = new StringBuilder(length);
        hr = GetCurrentPackageFullName(ref length, builder);
        return hr == 0;
    }

    private static string TryReadPackageFamilyName(object? appInfo)
    {
        var package = GetPropertyValue(appInfo, "Package");
        var id = GetPropertyValue(package, "Id");
        return ReadStringProperty(id, "FamilyName");
    }

    private static string SimplifyAppId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Windows 应用";
        }

        var text = value;
        var bangIndex = text.IndexOf('!');
        if (bangIndex > 0)
        {
            text = text[..bangIndex];
        }

        if (text.Contains('_'))
        {
            text = text.Split('_')[0];
        }

        if (text.Contains('.'))
        {
            text = text.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? text;
        }

        return text.Replace('_', ' ').Replace('-', ' ').Trim();
    }

    private static async Task<object?> AwaitWinRtOperationAsync(object? operation, CancellationToken cancellationToken)
    {
        if (operation is null || AsTaskGenericMethodDefinition is null)
        {
            return null;
        }

        var resultType = ResolveWinRtOperationResultType(operation.GetType());
        if (resultType is null)
        {
            return null;
        }

        var asTaskMethod = AsTaskGenericMethodDefinition.MakeGenericMethod(resultType);
        var taskObject = asTaskMethod.Invoke(null, [operation]) as Task;
        if (taskObject is null)
        {
            return null;
        }

        await taskObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        return taskObject.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(taskObject);
    }

    private static Type? ResolveWinRtOperationResultType(Type operationType)
    {
        if (operationType.IsGenericType && operationType.GetGenericArguments().Length == 1)
        {
            return operationType.GetGenericArguments()[0];
        }

        foreach (var iface in operationType.GetInterfaces())
        {
            if (iface.IsGenericType &&
                string.Equals(iface.GetGenericTypeDefinition().FullName, "Windows.Foundation.IAsyncOperation`1", StringComparison.Ordinal))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static MethodInfo? ResolveAsTaskGenericMethod()
    {
        var type = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime", throwOnError: false);
        return type?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "AsTask" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
    }

    private static MethodInfo? ResolveAsStreamForReadMethod()
    {
        var type = Type.GetType("System.IO.WindowsRuntimeStreamExtensions, System.Runtime.WindowsRuntime", throwOnError: false);
        return type?
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "AsStreamForRead" && method.GetParameters().Length == 1);
    }

    private static Type? ResolveWinRtType(string typeName)
    {
        return Type.GetType($"{typeName}, Windows, ContentType=WindowsRuntime", throwOnError: false);
    }

    private static object? InvokeMethod(object? target, string methodName, object?[]? parameters)
    {
        return target?.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, parameters);
    }

    private static object? GetPropertyValue(object? target, string propertyName)
    {
        return target switch
        {
            null => null,
            Type type => type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null),
            _ => target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
        };
    }

    private static string ReadStringProperty(object? target, string propertyName)
    {
        return GetPropertyValue(target, propertyName)?.ToString()?.Trim() ?? string.Empty;
    }

    private static uint ReadUIntProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        try
        {
            return Convert.ToUInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTimeOffset? ReadDateTimeOffsetProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset;
        }

        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _pollTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }
        _cts.Dispose();
    }

    private const int AppmodelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
