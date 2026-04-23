using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class StartupAttemptRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly string _mutexName;
    private string? _ownedAttemptId;

    public StartupAttemptRegistry()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            ".launcher",
            "state",
            "startup-attempt.json"))
    {
    }

    internal StartupAttemptRegistry(string statePath)
    {
        _statePath = statePath;
        _mutexName = $"LanMountainDesktop.Launcher.StartupAttempt.{ComputePathHash(statePath)}";
    }

    public StartupAttemptRecord StartOwnedAttempt(
        int hostPid,
        string launchSource,
        string successPolicy,
        StartupStage stage,
        string? message)
    {
        var record = new StartupAttemptRecord
        {
            AttemptId = Guid.NewGuid().ToString("N"),
            HostPid = hostPid,
            LaunchSource = launchSource,
            SuccessPolicy = successPolicy,
            LastObservedStage = stage,
            LastObservedMessage = message ?? string.Empty,
            StartedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            State = StartupAttemptState.Pending
        };

        ExecuteWithLock(() =>
        {
            SaveUnsafe(record);
            _ownedAttemptId = record.AttemptId;
        });

        return Clone(record);
    }

    public bool AdoptAttempt(string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return false;
        }

        var adopted = false;
        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is null || !string.Equals(record.AttemptId, attemptId, StringComparison.Ordinal))
            {
                return;
            }

            if (!IsAttachable(record))
            {
                return;
            }

            _ownedAttemptId = record.AttemptId;
            if (record.State == StartupAttemptState.DetachedWaiting)
            {
                record.State = StartupAttemptState.SoftTimeout;
            }

            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe(record);
            adopted = true;
        });

        return adopted;
    }

    public StartupAttemptRecord? TryGetAttachableAttempt(string launchSource, string successPolicy)
    {
        StartupAttemptRecord? result = null;
        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is null ||
                !IsAttachable(record) ||
                !string.Equals(record.LaunchSource, launchSource, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.SuccessPolicy, successPolicy, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result = Clone(record);
        });

        return result;
    }

    public void MarkOwnedIpcConnected()
    {
        UpdateOwned(record => record.IpcConnected = true);
    }

    public void UpdateOwnedStage(StartupStage stage, string? message, bool ipcConnected)
    {
        UpdateOwned(record =>
        {
            record.LastObservedStage = stage;
            record.LastObservedMessage = message ?? string.Empty;
            if (ipcConnected)
            {
                record.IpcConnected = true;
            }
        });
    }

    public void MarkOwnedSoftTimeout(string? message)
    {
        UpdateOwned(record =>
        {
            record.State = StartupAttemptState.SoftTimeout;
            record.LastObservedMessage = message ?? record.LastObservedMessage;
        });
    }

    public void MarkOwnedDetachedWaiting()
    {
        UpdateOwned(record =>
        {
            if (record.State is StartupAttemptState.Pending or StartupAttemptState.SoftTimeout)
            {
                record.State = StartupAttemptState.DetachedWaiting;
            }
        });
    }

    public void MarkOwnedSucceeded(StartupStage stage, string? message)
    {
        UpdateOwned(record =>
        {
            record.State = StartupAttemptState.Succeeded;
            record.LastObservedStage = stage;
            record.LastObservedMessage = message ?? record.LastObservedMessage;
        });
    }

    public void MarkOwnedFailed(StartupStage stage, string? message)
    {
        UpdateOwned(record =>
        {
            record.State = StartupAttemptState.Failed;
            record.LastObservedStage = stage;
            record.LastObservedMessage = message ?? record.LastObservedMessage;
        });
    }

    private void UpdateOwned(Action<StartupAttemptRecord> update)
    {
        if (string.IsNullOrWhiteSpace(_ownedAttemptId))
        {
            return;
        }

        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is null || !string.Equals(record.AttemptId, _ownedAttemptId, StringComparison.Ordinal))
            {
                return;
            }

            update(record);
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe(record);
        });
    }

    private void ExecuteWithLock(Action action)
    {
        using var mutex = new Mutex(false, _mutexName);
        var hasHandle = false;
        try
        {
            try
            {
                hasHandle = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                hasHandle = true;
            }

            if (!hasHandle)
            {
                return;
            }

            action();
        }
        finally
        {
            if (hasHandle)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private StartupAttemptRecord? LoadUnsafe()
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<StartupAttemptRecord>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveUnsafe(StartupAttemptRecord record)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_statePath, JsonSerializer.Serialize(record, SerializerOptions));
    }

    private static bool IsAttachable(StartupAttemptRecord record)
    {
        if (record.State is not (StartupAttemptState.Pending or StartupAttemptState.SoftTimeout or StartupAttemptState.DetachedWaiting))
        {
            return false;
        }

        return TryGetLiveProcess(record.HostPid, out _);
    }

    private static bool TryGetLiveProcess(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private static string ComputePathHash(string statePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(statePath.ToLowerInvariant()));
        return Convert.ToHexString(bytes[..8]);
    }

    private static StartupAttemptRecord Clone(StartupAttemptRecord record)
    {
        return new StartupAttemptRecord
        {
            AttemptId = record.AttemptId,
            HostPid = record.HostPid,
            StartedAtUtc = record.StartedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            LaunchSource = record.LaunchSource,
            SuccessPolicy = record.SuccessPolicy,
            LastObservedStage = record.LastObservedStage,
            LastObservedMessage = record.LastObservedMessage,
            IpcConnected = record.IpcConnected,
            State = record.State
        };
    }
}
