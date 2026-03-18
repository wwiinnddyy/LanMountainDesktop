using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

public sealed record RemovableStorageDrive(
    string RootPath,
    string DriveLetter,
    string? VolumeLabel);

public interface IRemovableStorageService
{
    IReadOnlyList<RemovableStorageDrive> GetConnectedDrives();

    bool OpenDrive(string rootPath);

    bool EjectDrive(string rootPath);
}

public sealed class RemovableStorageService : IRemovableStorageService
{
    public IReadOnlyList<RemovableStorageDrive> GetConnectedDrives()
    {
        var drives = new List<RemovableStorageDrive>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Removable || !drive.IsReady)
                {
                    continue;
                }

                var rootPath = NormalizeRootPath(drive.Name);
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    continue;
                }

                var driveLetter = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var volumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? null
                    : drive.VolumeLabel.Trim();

                drives.Add(new RemovableStorageDrive(rootPath, driveLetter, volumeLabel));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("RemovableStorage", $"Failed to inspect drive '{drive.Name}'.", ex);
            }
        }

        return drives
            .OrderBy(drive => drive.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool OpenDrive(string rootPath)
    {
        var normalizedRootPath = NormalizeRootPath(rootPath);
        if (string.IsNullOrWhiteSpace(normalizedRootPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = normalizedRootPath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RemovableStorage", $"Failed to open drive '{normalizedRootPath}'.", ex);
            return false;
        }
    }

    public bool EjectDrive(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var normalizedRootPath = NormalizeRootPath(rootPath);
        if (string.IsNullOrWhiteSpace(normalizedRootPath))
        {
            return false;
        }

        object? shellApplication = null;
        object? computerFolder = null;
        object? driveItem = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shellApplication = Activator.CreateInstance(shellType);
            if (shellApplication is null)
            {
                return false;
            }

            computerFolder = shellType.InvokeMember(
                "NameSpace",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shellApplication,
                args: [17]);
            if (computerFolder is null)
            {
                return false;
            }

            var driveToken = normalizedRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            driveItem = computerFolder.GetType().InvokeMember(
                "ParseName",
                BindingFlags.InvokeMethod,
                binder: null,
                target: computerFolder,
                args: [driveToken]);
            if (driveItem is null)
            {
                return false;
            }

            if (TryInvokeVerb(driveItem, "Eject"))
            {
                return true;
            }

            return TryInvokeLocalizedEjectVerb(driveItem);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("RemovableStorage", $"Failed to eject drive '{normalizedRootPath}'.", ex);
            return false;
        }
        finally
        {
            ReleaseComObject(driveItem);
            ReleaseComObject(computerFolder);
            ReleaseComObject(shellApplication);
        }
    }

    private static bool TryInvokeLocalizedEjectVerb(object driveItem)
    {
        object? verbs = null;

        try
        {
            verbs = driveItem.GetType().InvokeMember(
                "Verbs",
                BindingFlags.InvokeMethod,
                binder: null,
                target: driveItem,
                args: null);
            if (verbs is null)
            {
                return false;
            }

            var verbsType = verbs.GetType();
            var countObject = verbsType.InvokeMember(
                "Count",
                BindingFlags.GetProperty,
                binder: null,
                target: verbs,
                args: null);
            var count = countObject is null
                ? 0
                : Convert.ToInt32(countObject, CultureInfo.InvariantCulture);

            for (var index = 0; index < count; index++)
            {
                object? verb = null;

                try
                {
                    verb = verbsType.InvokeMember(
                        "Item",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: verbs,
                        args: [index]);
                    if (verb is null)
                    {
                        continue;
                    }

                    var verbNameObject = verb.GetType().InvokeMember(
                        "Name",
                        BindingFlags.GetProperty,
                        binder: null,
                        target: verb,
                        args: null);
                    var verbName = Convert.ToString(verbNameObject, CultureInfo.InvariantCulture);
                    if (!IsEjectVerbName(verbName))
                    {
                        continue;
                    }

                    verb.GetType().InvokeMember(
                        "DoIt",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: verb,
                        args: null);
                    return true;
                }
                finally
                {
                    ReleaseComObject(verb);
                }
            }

            return false;
        }
        finally
        {
            ReleaseComObject(verbs);
        }
    }

    private static bool TryInvokeVerb(object driveItem, string verbName)
    {
        try
        {
            driveItem.GetType().InvokeMember(
                "InvokeVerb",
                BindingFlags.InvokeMethod,
                binder: null,
                target: driveItem,
                args: [verbName]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEjectVerbName(string? verbName)
    {
        if (string.IsNullOrWhiteSpace(verbName))
        {
            return false;
        }

        var normalized = string.Concat(
            verbName
                .Where(character => !char.IsWhiteSpace(character) && character != '&'))
            .Trim();

        return normalized.Contains("Eject", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("弹出", StringComparison.Ordinal) ||
               normalized.Contains("安全删除", StringComparison.Ordinal) ||
               normalized.Contains("卸载", StringComparison.Ordinal);
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var trimmed = rootPath.Trim();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{trimmed}:{Path.DirectorySeparatorChar}");
        }

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return trimmed + Path.DirectorySeparatorChar;
        }

        var normalized = trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var resolvedRoot = Path.GetPathRoot(normalized);
        return string.IsNullOrWhiteSpace(resolvedRoot)
            ? normalized
            : resolvedRoot;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
