namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Logger interface for AirApps.
/// </summary>
public interface IAirAppLogger
{
    /// <summary>
    /// Log a debug message.
    /// </summary>
    void Debug(string message);

    /// <summary>
    /// Log an informational message.
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Log a warning message.
    /// </summary>
    void Warn(string message);

    /// <summary>
    /// Log a warning with exception.
    /// </summary>
    void Warn(string message, Exception exception);

    /// <summary>
    /// Log an error message.
    /// </summary>
    void Error(string message);

    /// <summary>
    /// Log an error with exception.
    /// </summary>
    void Error(string message, Exception exception);
}
