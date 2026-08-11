using System;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

internal static class UiExceptionGuard
{
    public static bool IsFatalException(Exception? exception)
    {
        return exception is OutOfMemoryException or AccessViolationException or StackOverflowException;
    }

    public static void FireAndForgetGuarded(
        Func<Task> action,
        string actionName,
        string? context = null,
        Func<Exception, Task>? onHandledException = null)
    {
        _ = RunGuardedUiActionAsync(action, actionName, context, onHandledException);
    }

    public static async Task RunGuardedUiActionAsync(
        Func<Task> action,
        string actionName,
        string? context = null,
        Func<Exception, Task>? onHandledException = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action();
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            LogHandledException("GuardedUiAction", actionName, ex, context, isFatal: false);
            if (onHandledException is not null)
            {
                try
                {
                    await onHandledException(ex);
                }
                catch (Exception handlerEx) when (!IsFatalException(handlerEx))
                {
                    LogHandledException("GuardedUiActionHandler", actionName, handlerEx, context, isFatal: false);
                }
            }
        }
    }

    public static string BuildContext(params (string Key, object? Value)[] parts)
    {
        if (parts is null || parts.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "; ",
            Array.ConvertAll(parts, part => $"{part.Key}={part.Value ?? "<null>"}"));
    }

    private static void LogHandledException(
        string category,
        string actionName,
        Exception exception,
        string? context,
        bool isFatal)
    {
        var message =
            $"Action={actionName}; ExceptionType={exception.GetType().FullName}; IsFatal={isFatal}; Context={context ?? string.Empty}";
        if (isFatal)
        {
            AppLogger.Critical(category, message, exception);
            return;
        }

        AppLogger.Warn(category, message, exception);
    }
}
