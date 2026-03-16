using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public interface IOfficeRecentDocumentsService
{
    List<OfficeRecentDocument> GetRecentDocuments(int maxCount = 20);
    void OpenDocument(string filePath);
}

public sealed class OfficeRecentDocument
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime LastModifiedTime { get; set; }
    public long FileSizeBytes { get; set; }
    public string IconGlyph { get; set; } = string.Empty;
}

public sealed class OfficeRecentDocumentsService : IOfficeRecentDocumentsService
{
    private static readonly string[] OfficeExtensions = { ".doc", ".docx", ".dot", ".dotx", ".rtf" };
    private static readonly string[] ExcelExtensions = { ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv" };
    private static readonly string[] PowerPointExtensions = { ".ppt", ".pptx", ".pptm", ".pps", ".ppsx" };

    public List<OfficeRecentDocument> GetRecentDocuments(int maxCount = 20)
    {
        var documents = new List<OfficeRecentDocument>();
        var recentPaths = GetRecentFolders();

        foreach (var recentPath in recentPaths)
        {
            if (!Directory.Exists(recentPath))
            {
                continue;
            }

            try
            {
                var files = Directory.GetFiles(recentPath, "*.lnk");
                foreach (var lnkPath in files)
                {
                    var targetPath = GetShortcutTarget(lnkPath);
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        continue;
                    }

                    var extension = Path.GetExtension(targetPath).ToLowerInvariant();
                    if (!IsOfficeFile(extension))
                    {
                        continue;
                    }

                    if (!System.IO.File.Exists(targetPath))
                    {
                        continue;
                    }

                    try
                    {
                        var fileInfo = new FileInfo(targetPath);
                        var doc = new OfficeRecentDocument
                        {
                            FileName = Path.GetFileNameWithoutExtension(targetPath),
                            FilePath = targetPath,
                            Extension = extension,
                            LastModifiedTime = fileInfo.LastWriteTime,
                            FileSizeBytes = fileInfo.Length,
                            IconGlyph = GetIconGlyph(extension)
                        };

                        if (!documents.Any(d => d.FilePath == targetPath))
                        {
                            documents.Add(doc);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        return documents
            .OrderByDescending(d => d.LastModifiedTime)
            .Take(maxCount)
            .ToList();
    }

    public void OpenDocument(string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private static List<string> GetRecentFolders()
    {
        var folders = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        folders.Add(Path.Combine(appData, "Microsoft", "Word", "Recent"));
        folders.Add(Path.Combine(appData, "Microsoft", "Excel", "Recent"));
        folders.Add(Path.Combine(appData, "Microsoft", "PowerPoint", "Recent"));

        return folders;
    }

    private static bool IsOfficeFile(string extension)
    {
        return OfficeExtensions.Contains(extension) ||
               ExcelExtensions.Contains(extension) ||
               PowerPointExtensions.Contains(extension);
    }

    private static string GetIconGlyph(string extension)
    {
        return extension switch
        {
            ".doc" or ".docx" or ".dot" or ".dotx" or ".rtf" => "\uE8A5",
            ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".csv" => "\uE9F9",
            ".ppt" or ".pptx" or ".pptm" or ".pps" or ".ppsx" => "\uE8A1",
            _ => "\uE8A5"
        };
    }

    private static string? GetShortcutTarget(string lnkPath)
    {
        return ShortcutHelper.GetShortcutTarget(lnkPath);
    }
}
