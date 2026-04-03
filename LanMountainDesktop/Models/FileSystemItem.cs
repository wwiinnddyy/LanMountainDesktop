using System;
using System.IO;

namespace LanMountainDesktop.Models;

public enum FileSystemItemType
{
    Drive,
    Directory,
    File
}

public sealed class FileSystemItem
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public FileSystemItemType ItemType { get; init; }
    public long? Size { get; init; }
    public DateTime? LastModified { get; init; }
    public string? Extension { get; init; }

    public bool IsDirectory => ItemType == FileSystemItemType.Directory || ItemType == FileSystemItemType.Drive;

    public static FileSystemItem FromDriveInfo(DriveInfo drive)
    {
        string name;
        long? size = null;

        try
        {
            var volumeLabel = drive.VolumeLabel;
            name = string.IsNullOrWhiteSpace(volumeLabel)
                ? $"{drive.Name.TrimEnd('\\', '/')}"
                : $"{volumeLabel} ({drive.Name.TrimEnd('\\', '/').ToUpperInvariant()})";
        }
        catch
        {
            name = $"{drive.Name.TrimEnd('\\', '/')}";
        }

        try
        {
            var totalSize = drive.TotalSize;
            size = totalSize > 0 ? totalSize : null;
        }
        catch
        {
            size = null;
        }

        return new FileSystemItem
        {
            Name = name,
            FullPath = drive.Name,
            ItemType = FileSystemItemType.Drive,
            Size = size,
            LastModified = null,
            Extension = null
        };
    }

    public static FileSystemItem FromDirectoryInfo(DirectoryInfo directory)
    {
        return new FileSystemItem
        {
            Name = directory.Name,
            FullPath = directory.FullName,
            ItemType = FileSystemItemType.Directory,
            Size = null,
            LastModified = directory.LastWriteTime,
            Extension = null
        };
    }

    public static FileSystemItem FromFileInfo(FileInfo file)
    {
        return new FileSystemItem
        {
            Name = file.Name,
            FullPath = file.FullName,
            ItemType = FileSystemItemType.File,
            Size = file.Length,
            LastModified = file.LastWriteTime,
            Extension = file.Extension
        };
    }
}
