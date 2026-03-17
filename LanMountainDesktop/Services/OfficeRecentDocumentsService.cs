using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Services.Settings;
using Microsoft.Win32;

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

        // 方法1: 从注册表读取Office最近文档（最可靠）
        TryGetFromRegistry(documents);

        // 方法2: 从Recent文件夹读取快捷方式（备用）
        TryGetFromRecentFolders(documents);

        // 方法3: 从Windows Jump List读取（如果可用）
        TryGetFromJumpList(documents);

        return documents
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.LastModifiedTime).First())
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

#pragma warning disable CA1416 // 平台兼容性警告
    private void TryGetFromRegistry(List<OfficeRecentDocument> documents)
    {
        try
        {
            // Word最近文档
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\Word\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\Word\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\15.0\Word\Reading Locations");

            // Excel最近文档
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\Excel\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\Excel\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\15.0\Excel\Reading Locations");

            // PowerPoint最近文档
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\PowerPoint\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\PowerPoint\Reading Locations");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\15.0\PowerPoint\Reading Locations");

            // 通用Office最近文档
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\Common\Open Find\Microsoft Office Word");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\Common\Open Find\Microsoft Office Excel");
            TryGetFromOfficeRegistry(documents, @"Software\Microsoft\Office\16.0\Common\Open Find\Microsoft Office PowerPoint");
        }
        catch
        {
            // 忽略注册表访问错误
        }
    }

    private void TryGetFromOfficeRegistry(List<OfficeRecentDocument> documents, string registryPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var filePath = subKey.GetValue("Path") as string;
                    if (string.IsNullOrEmpty(filePath)) continue;

                    AddDocumentIfExists(documents, filePath);
                }
                catch
                {
                    // 忽略单个子键访问错误
                }
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }
    }
#pragma warning restore CA1416 // 平台兼容性警告

    private void TryGetFromRecentFolders(List<OfficeRecentDocument> documents)
    {
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

                    AddDocumentIfExists(documents, targetPath);
                }
            }
            catch
            {
                // 忽略文件夹访问错误
            }
        }
    }

    private void TryGetFromJumpList(List<OfficeRecentDocument> documents)
    {
        try
        {
            // Windows Jump List存储在以下位置
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var jumpListPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Recent", "AutomaticDestinations");

            if (!Directory.Exists(jumpListPath)) return;

            // Office应用的Jump List文件
            var officeJumpListFiles = new[]
            {
                "a7bd7a3f3d5a4c74.automaticDestinations-ms", // Word
                "9b524fe3be704a4d.automaticDestinations-ms", // Excel
                "d0063c4c7de64e5e.automaticDestinations-ms"  // PowerPoint
            };

            foreach (var jumpFile in officeJumpListFiles)
            {
                var fullPath = Path.Combine(jumpListPath, jumpFile);
                if (File.Exists(fullPath))
                {
                    TryParseJumpListFile(fullPath, documents);
                }
            }
        }
        catch
        {
            // Jump List解析失败，忽略
        }
    }

    private void TryParseJumpListFile(string jumpListPath, List<OfficeRecentDocument> documents)
    {
        try
        {
            // Jump List文件是二进制格式，这里使用简化的方法
            // 读取文件并尝试提取文件路径
            var bytes = File.ReadAllBytes(jumpListPath);
            var text = Encoding.Unicode.GetString(bytes);

            // 查找可能的文件路径（简化实现）
            var possiblePaths = ExtractPossiblePaths(text);
            foreach (var path in possiblePaths)
            {
                AddDocumentIfExists(documents, path);
            }
        }
        catch
        {
            // Jump List解析失败，忽略
        }
    }

    private IEnumerable<string> ExtractPossiblePaths(string text)
    {
        var paths = new List<string>();

        // 查找常见的文件路径模式
        var patterns = new[]
        {
            @"[A-Z]:\\[^\x00-\x1F""<>|]*\.(docx?|xlsx?|pptx?|rtf|csv)",
            @"\\\\[^\\]+\\[^\x00-\x1F""<>|]*\.(docx?|xlsx?|pptx?|rtf|csv)"
        };

        foreach (var pattern in patterns)
        {
            try
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var path = match.Value.Trim('\0', ' ', '"');
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            catch
            {
                // 忽略正则表达式错误
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void AddDocumentIfExists(List<OfficeRecentDocument> documents, string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!IsOfficeFile(extension))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var doc = new OfficeRecentDocument
            {
                FileName = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Extension = extension,
                LastModifiedTime = fileInfo.LastWriteTime,
                FileSizeBytes = fileInfo.Length,
                IconGlyph = GetIconGlyph(extension)
            };

            if (!documents.Any(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                documents.Add(doc);
            }
        }
        catch
        {
            // 忽略单个文件处理错误
        }
    }

    private static List<string> GetRecentFolders()
    {
        var folders = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        folders.Add(Path.Combine(appData, "Microsoft", "Word", "Recent"));
        folders.Add(Path.Combine(appData, "Microsoft", "Excel", "Recent"));
        folders.Add(Path.Combine(appData, "Microsoft", "PowerPoint", "Recent"));

        // 添加Office 365路径
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        folders.Add(Path.Combine(localAppData, "Microsoft", "Office", "Word", "Recent"));
        folders.Add(Path.Combine(localAppData, "Microsoft", "Office", "Excel", "Recent"));
        folders.Add(Path.Combine(localAppData, "Microsoft", "Office", "PowerPoint", "Recent"));

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
