using System.Text.Json;
using System.Text.Json.Serialization;
using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal sealed class DeploymentRootFileTransaction : IDisposable
{
    private readonly string _root;
    private readonly string _backup;
    private readonly FileStream _applyLock;
    private readonly List<string> _replaced = [];
    private readonly HashSet<string> _original = new(StringComparer.OrdinalIgnoreCase);
    private bool _committed;

    internal DeploymentRootFileTransaction(string root)
    {
        _root = Path.GetFullPath(root);
        var incoming = UpdatePaths.GetIncomingDirectory(_root);
        Directory.CreateDirectory(incoming);
        _applyLock = new FileStream(UpdatePaths.GetApplyInProgressLockPath(_root), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _backup = Path.Combine(incoming, "root-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_backup);
    }

    internal void Copy(string sourceRoot, string appDirectory, CancellationToken cancellationToken)
    {
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathContainment.IsSameOrChild(appDirectory, source)) continue;
            var relative = Path.GetRelativePath(sourceRoot, source);
            if (DeploymentLayout.IsDeploymentMarker(relative)) continue;
            Replace(source, relative);

        }
    }

    internal string BackupDirectory => _backup;

    internal void PrepareCommit()
    {
        var state = new RootFileRestoreState(_backup, _replaced.ToArray(), _original.ToArray());
        File.WriteAllText(Path.Combine(_backup, "restore.json"),
            JsonSerializer.Serialize(state, RootFileRestoreJsonContext.Default.RootFileRestoreState));
    }

    internal void Commit() => _committed = true;

    internal static DeploymentRootFileTransaction BeginRestore(string root, RootFileRestoreState state, CancellationToken cancellationToken)
    {
        if (!PathContainment.IsSameOrChild(UpdatePaths.GetIncomingDirectory(root), state.BackupDirectory))
            throw new InvalidDataException("Root rollback backup is outside the update directory.");
        foreach (var relative in state.Original)
            if (!File.Exists(Resolve(state.BackupDirectory, relative)))
                throw new FileNotFoundException("Root rollback file is missing.", relative);
        var transaction = new DeploymentRootFileTransaction(root);
        try
        {
            foreach (var relative in state.Replaced)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (state.Original.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    transaction.Replace(Resolve(state.BackupDirectory, relative), relative);
                else
                    transaction.Remove(relative);
            }
            transaction.PrepareCommit();
            return transaction;
        }
        catch { transaction.Dispose(); throw; }
    }

    private void Preserve(string relative)
    {
        var target = Resolve(_root, relative);
        if (File.Exists(target))
        {
            var backup = Resolve(_backup, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(target, backup);
            _original.Add(relative);
        }
        _replaced.Add(relative);
    }

    private void Replace(string source, string relative)
    {
        var target = Resolve(_root, relative);
        Preserve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".updating-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Remove(string relative)
    {
        Preserve(relative);
        var target = Resolve(_root, relative);
        if (File.Exists(target)) File.Delete(target);
    }

    public void Dispose()
    {
        try
        {
            if (!_committed)
            {
                foreach (var relative in _replaced.AsEnumerable().Reverse())
                {
                    var target = Resolve(_root, relative);
                    if (_original.Contains(relative))
                        File.Copy(Resolve(_backup, relative), target, overwrite: true);
                    else if (File.Exists(target))
                        File.Delete(target);
                }
            }
        }
        finally { _applyLock.Dispose(); }
    }

    private static string Resolve(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!PathContainment.IsSameOrChild(root, path)) throw new InvalidDataException("Root payload escapes its installation directory.");
        return path;
    }
}

internal sealed record RootFileRestoreState(string BackupDirectory, string[] Replaced, string[] Original);
[JsonSerializable(typeof(RootFileRestoreState))]
internal partial class RootFileRestoreJsonContext : JsonSerializerContext;
