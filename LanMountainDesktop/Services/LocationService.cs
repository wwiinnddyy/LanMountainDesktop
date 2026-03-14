using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public enum LocationFailureReason
{
    None = 0,
    Unsupported = 1,
    PermissionDenied = 2,
    Disabled = 3,
    Timeout = 4,
    Cancelled = 5,
    Unavailable = 6,
    Unknown = 7
}

public readonly record struct LocationCoordinate(
    double Latitude,
    double Longitude,
    double? AccuracyMeters = null);

public sealed record LocationRequestResult(
    bool Success,
    bool IsSupported,
    LocationCoordinate? Coordinate = null,
    LocationFailureReason FailureReason = LocationFailureReason.None,
    string? ErrorMessage = null)
{
    public static LocationRequestResult Unsupported(string? errorMessage = null)
        => new(false, false, null, LocationFailureReason.Unsupported, errorMessage);

    public static LocationRequestResult Ok(LocationCoordinate coordinate)
        => new(true, true, coordinate, LocationFailureReason.None, null);

    public static LocationRequestResult Fail(LocationFailureReason reason, string? errorMessage = null)
        => new(false, true, null, reason, errorMessage);
}

public interface ILocationService
{
    bool IsSupported { get; }

    Task<LocationRequestResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default);
}

public sealed class UnsupportedLocationService : ILocationService
{
    public bool IsSupported => false;

    public Task<LocationRequestResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(LocationRequestResult.Unsupported("Location service is not supported on this platform."));
    }
}

public sealed class WindowsLocationService : ILocationService
{
    private static readonly Type? GeolocatorType = ResolveWinRtType("Windows.Devices.Geolocation.Geolocator");
    private static readonly MethodInfo? RequestAccessAsyncMethod =
        GeolocatorType?.GetMethod("RequestAccessAsync", BindingFlags.Public | BindingFlags.Static);
    private static readonly MethodInfo? AsTaskGenericMethodDefinition = ResolveAsTaskGenericMethod();

    public bool IsSupported =>
        OperatingSystem.IsWindows() &&
        GeolocatorType is not null &&
        RequestAccessAsyncMethod is not null &&
        AsTaskGenericMethodDefinition is not null;

    public async Task<LocationRequestResult> TryGetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return LocationRequestResult.Unsupported();
        }

        try
        {
            var access = await AwaitWinRtOperationAsync(RequestAccessAsyncMethod!.Invoke(null, null), cancellationToken);
            var accessText = access?.ToString();
            if (string.Equals(accessText, "Denied", StringComparison.OrdinalIgnoreCase))
            {
                return LocationRequestResult.Fail(
                    LocationFailureReason.PermissionDenied,
                    "Location permission was denied by the system.");
            }

            if (string.Equals(accessText, "Unspecified", StringComparison.OrdinalIgnoreCase))
            {
                return LocationRequestResult.Fail(
                    LocationFailureReason.Disabled,
                    "Location access is unavailable on this device.");
            }

            var geolocator = Activator.CreateInstance(GeolocatorType!);
            if (geolocator is null)
            {
                return LocationRequestResult.Fail(LocationFailureReason.Unavailable, "Failed to create a Windows geolocator instance.");
            }

            SetPropertyValue(geolocator, "DesiredAccuracyInMeters", (uint)50);
            SetPropertyValue(geolocator, "MovementThreshold", 0d);
            SetPropertyValue(geolocator, "ReportInterval", (uint)0);

            var geoposition = await AwaitWinRtOperationAsync(
                InvokeMethod(geolocator, "GetGeopositionAsync"),
                cancellationToken);
            if (geoposition is null)
            {
                return LocationRequestResult.Fail(LocationFailureReason.Unavailable, "Location request returned no position.");
            }

            var coordinate = GetPropertyValue(geoposition, "Coordinate");
            var point = GetPropertyValue(coordinate, "Point");
            var position = GetPropertyValue(point, "Position");

            var latitude = ReadDoubleProperty(position, "Latitude");
            var longitude = ReadDoubleProperty(position, "Longitude");
            if (!latitude.HasValue || !longitude.HasValue)
            {
                return LocationRequestResult.Fail(LocationFailureReason.Unavailable, "Location coordinates are not available.");
            }

            var accuracy = ReadDoubleProperty(coordinate, "Accuracy");
            return LocationRequestResult.Ok(new LocationCoordinate(latitude.Value, longitude.Value, accuracy));
        }
        catch (OperationCanceledException)
        {
            return LocationRequestResult.Fail(
                cancellationToken.IsCancellationRequested ? LocationFailureReason.Cancelled : LocationFailureReason.Timeout,
                "Location request was cancelled.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return MapException(ex.InnerException);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static LocationRequestResult MapException(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return LocationRequestResult.Fail(LocationFailureReason.PermissionDenied, ex.Message);
        }

        if (ex is TimeoutException)
        {
            return LocationRequestResult.Fail(LocationFailureReason.Timeout, ex.Message);
        }

        var hr = ex.HResult;
        if (hr == unchecked((int)0x80070422))
        {
            return LocationRequestResult.Fail(LocationFailureReason.Disabled, ex.Message);
        }

        return LocationRequestResult.Fail(LocationFailureReason.Unknown, ex.Message);
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

        await taskObject.WaitAsync(cancellationToken);
        return taskObject
            .GetType()
            .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(taskObject);
    }

    private static Type? ResolveWinRtOperationResultType(Type operationType)
    {
        if (operationType.IsGenericType)
        {
            var genericArguments = operationType.GetGenericArguments();
            if (genericArguments.Length == 1)
            {
                return genericArguments[0];
            }
        }

        foreach (var iface in operationType.GetInterfaces())
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = iface.GetGenericTypeDefinition();
            if (string.Equals(genericTypeDef.FullName, "Windows.Foundation.IAsyncOperation`1", StringComparison.Ordinal))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static MethodInfo? ResolveAsTaskGenericMethod()
    {
        try
        {
            var type = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime", throwOnError: false);
            if (type is null)
            {
                return null;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                try
                {
                    if (!string.Equals(method.Name, "AsTask", StringComparison.Ordinal) ||
                        !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 1)
                    {
                        return method;
                    }
                }
                catch (PlatformNotSupportedException)
                {
                    // Some WinRT bridge overloads throw during metadata inspection on unsupported runtimes.
                }
                catch
                {
                    // Ignore unusable overloads and keep probing for a compatible AsTask<T>.
                }
            }
        }
        catch
        {
            // If the WinRT bridge is unavailable, the location service will gracefully report unsupported.
        }

        return null;
    }

    private static Type? ResolveWinRtType(string typeName)
    {
        return Type.GetType($"{typeName}, Windows, ContentType=WindowsRuntime", throwOnError: false);
    }

    private static object? InvokeMethod(object? target, string methodName)
    {
        return target?.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, null);
    }

    private static object? GetPropertyValue(object? target, string propertyName)
    {
        return target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    private static void SetPropertyValue(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        try
        {
            property.SetValue(target, value);
        }
        catch
        {
        }
    }

    private static double? ReadDoubleProperty(object? target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDouble(value);
        }
        catch
        {
            return null;
        }
    }
}

internal static class HostLocationServiceProvider
{
    private static readonly object Gate = new();
    private static ILocationService? _instance;

    public static ILocationService GetOrCreate()
    {
        lock (Gate)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            if (!OperatingSystem.IsWindows())
            {
                _instance = new UnsupportedLocationService();
                return _instance;
            }

            try
            {
                _instance = new WindowsLocationService();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Location", "Failed to initialize Windows location service. Falling back to unsupported mode.", ex);
                _instance = new UnsupportedLocationService();
            }

            return _instance;
        }
    }
}
