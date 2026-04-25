using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class StartupAttemptRegistry
{
    private static readonly TimeSpan CoordinatorHeartbeatTimeout = TimeSpan.FromSeconds(10);

    private readonly string _statePath;
    private readonly string _mutexName;
    private string? _ownedAttemptId;

    public StartupAttemptRegistry()
        : this(ResolveDefaultStatePath())
    {
    }

    private static string ResolveDefaultStatePath()
    {
        try
        {
            var appRoot = Commands.ResolveAppRoot(CommandContext.FromArgs([]));
            var resolver = new DataLocationResolver(appRoot);
            return Path.Combine(resolver.ResolveLauncherStatePath(), "startup-attempt.json");
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop",
                "Launcher",
                "state",
                "startup-attempt.json");
        }
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
            CoordinatorPid = Environment.ProcessId,
            LaunchSource = launchSource,
            SuccessPolicy = successPolicy,
            LastObservedStage = stage,
            LastObservedMessage = message ?? string.Empty,
            StartedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            HeartbeatAtUtc = DateTimeOffset.UtcNow,
            State = StartupAttemptState.Pending
        };

        ExecuteWithLock(() =>
        {
            SaveUnsafe(record);
            _ownedAttemptId = record.AttemptId;
        });

        return Clone(record);
    }

    public bool TryReserveCoordinator(
        string launchSource,
        string successPolicy,
        string coordinatorPipeName,
        out StartupAttemptRecord reservedAttempt,
        out StartupAttemptRecord? activeCoordinatorAttempt)
    {
        StartupAttemptRecord? reserved = null;
        StartupAttemptRecord? active = null;

        ExecuteWithLock(() =>
        {
            var existing = LoadUnsafe();
            if (existing is not null && IsCoordinatorLive(existing))
            {
                active = Clone(existing);
                return;
            }

            if (existing is not null && IsRecoverableCoordinatorAttempt(existing))
            {
                existing.CoordinatorPid = Environment.ProcessId;
                existing.CoordinatorPipeName = coordinatorPipeName;
                existing.HeartbeatAtUtc = DateTimeOffset.UtcNow;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                if (existing.HostPid <= 0)
                {
                    existing.ReservedBeforeHostStart = true;
                }

                if (existing.State == StartupAttemptState.DetachedWaiting)
                {
                    existing.State = StartupAttemptState.SoftTimeout;
                }

                _ownedAttemptId = existing.AttemptId;
                SaveUnsafe(existing);
                reserved = Clone(existing);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var record = new StartupAttemptRecord
            {
                AttemptId = Guid.NewGuid().ToString("N"),
                HostPid = 0,
                CoordinatorPid = Environment.ProcessId,
                CoordinatorPipeName = coordinatorPipeName,
                LaunchSource = launchSource,
                SuccessPolicy = successPolicy,
                LastObservedStage = StartupStage.Initializing,
                LastObservedMessage = "Launcher coordinator reserved startup ownership.",
                StartedAtUtc = now,
                UpdatedAtUtc = now,
                HeartbeatAtUtc = now,
                ReservedBeforeHostStart = true,
                State = StartupAttemptState.Pending
            };

            _ownedAttemptId = record.AttemptId;
            SaveUnsafe(record);
            reserved = Clone(record);
        });

        reservedAttempt = reserved ?? new StartupAttemptRecord();
        activeCoordinatorAttempt = active;
        return reserved is not null;
    }

    public StartupAttemptRecord? GetOwnedAttempt()
    {
        StartupAttemptRecord? result = null;
        if (string.IsNullOrWhiteSpace(_ownedAttemptId))
        {
            return null;
        }

        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is not null && string.Equals(record.AttemptId, _ownedAttemptId, StringComparison.Ordinal))
            {
                result = Clone(record);
            }
        });

        return result;
    }

    public StartupAttemptRecord? TryGetLiveCoordinatorAttempt()
    {
        StartupAttemptRecord? result = null;
        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is not null && IsCoordinatorLive(record))
            {
                result = Clone(record);
            }
        });

        return result;
    }

    public StartupAttemptRecord? TryGetLatestAttempt()
    {
        StartupAttemptRecord? result = null;
        ExecuteWithLock(() =>
        {
            var record = LoadUnsafe();
            if (record is not null)
            {
                result = Clone(record);
            }
        });

        return result;
    }

    public StartupAttemptRecord AssignOwnedHostProcess(
        int hostPid,
        StartupStage stage,
        string? message)
    {
        StartupAttemptRecord? result = null;
        UpdateOwned(record =>
        {
            record.HostPid = hostPid;
            record.LastObservedStage = stage;
            record.LastObservedMessage = message ?? record.LastObservedMessage;
            record.ReservedBeforeHostStart = false;
            result = Clone(record);
        });

        return result ?? StartOwnedAttempt(
            hostPid,
            string.Empty,
            string.Empty,
            stage,
            message);
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
        UpdateOwned(record =>
        {
            record.IpcConnected = true;
            record.PublicIpcConnected = true;
        });
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
                record.PublicIpcConnected = true;
            }
        });
    }

    public void UpdateOwnedCoordinatorHeartbeat(LauncherCoordinatorStatus status)
    {
        UpdateOwned(record =>
        {
            record.CoordinatorPid = Environment.ProcessId;
            record.HeartbeatAtUtc = DateTimeOffset.UtcNow;
            record.LastObservedStage = status.LastObservedStage;
            record.LastObservedMessage = status.LastObservedMessage;
            record.IpcConnected = status.PublicIpcConnected;
            record.PublicIpcConnected = status.PublicIpcConnected;
            record.ShellStatus = status.ShellStatus?.ShellState ?? status.State;
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

    public void MarkOwnedWaitingForShell(string? message)
    {
        UpdateOwned(record =>
        {
            if (record.State is StartupAttemptState.Pending or StartupAttemptState.SoftTimeout or StartupAttemptState.DetachedWaiting)
            {
                record.State = StartupAttemptState.WaitingForShell;
            }

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
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.StartupAttemptRecord);
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

        File.WriteAllText(_statePath, JsonSerializer.Serialize(record, AppJsonContext.Default.StartupAttemptRecord));
    }

    private static bool IsAttachable(StartupAttemptRecord record)
    {
        if (record.State is not (
            StartupAttemptState.Pending or
            StartupAttemptState.SoftTimeout or
            StartupAttemptState.DetachedWaiting or
            StartupAttemptState.WaitingForShell))
        {
            return false;
        }

        return TryGetLiveProcess(record.HostPid, out _);
    }

    private static bool IsRecoverableCoordinatorAttempt(StartupAttemptRecord record)
    {
        if (record.State is not (
            StartupAttemptState.Pending or
            StartupAttemptState.SoftTimeout or
            StartupAttemptState.DetachedWaiting or
            StartupAttemptState.WaitingForShell))
        {
            return false;
        }

        if (record.HostPid <= 0)
        {
            return true;
        }

        return TryGetLiveProcess(record.HostPid, out _);
    }

    private static bool IsCoordinatorLive(StartupAttemptRecord record)
    {
        if (record.State is not (
            StartupAttemptState.Pending or
            StartupAttemptState.SoftTimeout or
            StartupAttemptState.DetachedWaiting or
            StartupAttemptState.WaitingForShell))
        {
            return false;
        }

        if (record.CoordinatorPid <= 0 ||
            string.IsNullOrWhiteSpace(record.CoordinatorPipeName) ||
            DateTimeOffset.UtcNow - record.HeartbeatAtUtc > CoordinatorHeartbeatTimeout)
        {
            return false;
        }

        return TryGetLiveProcess(record.CoordinatorPid, out _);
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
            CoordinatorPid = record.CoordinatorPid,
            CoordinatorPipeName = record.CoordinatorPipeName,
            StartedAtUtc = record.StartedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            HeartbeatAtUtc = record.HeartbeatAtUtc,
            LaunchSource = record.LaunchSource,
            SuccessPolicy = record.SuccessPolicy,
            LastObservedStage = record.LastObservedStage,
            LastObservedMessage = record.LastObservedMessage,
            IpcConnected = record.IpcConnected,
            PublicIpcConnected = record.PublicIpcConnected,
            ShellStatus = record.ShellStatus,
            ReservedBeforeHostStart = record.ReservedBeforeHostStart,
            State = record.State
        };
    }
}
