using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using MudTools.OfficeInterop;
using MudTools.OfficeInterop.Excel;
using MudTools.OfficeInterop.Word;

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
    internal DateTime? RecentAccessTime { get; set; }
    internal int SourcePriority { get; set; }
    internal int SourceOrder { get; set; }
}

public sealed class OfficeRecentDocumentsService : IOfficeRecentDocumentsService
{
    private const string LogCategory = "OfficeRecentDocs";
    private static readonly string[] OfficeExtensions = { ".doc", ".docx", ".dot", ".dotx", ".rtf" };
    private static readonly string[] ExcelExtensions = { ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv" };
    private static readonly string[] PowerPointExtensions = { ".ppt", ".pptx", ".pptm", ".pps", ".ppsx" };
    private static readonly Regex OfficeFilePathRegex = new(
        @"(?:[A-Z]:\\|\\\\)[^\x00-\x1F""<>|]+?\.(?:docx?|dotx?|rtf|xlsx?|xlsm|xlsb|csv|pptx?|pptm|ppsx?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OfficeMruTimestampRegex = new(
        @"\[T(?<filetime>[0-9A-F]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<OfficeRecentDocument> GetRecentDocuments(int maxCount = 20)
    {
        var documents = new List<OfficeRecentDocument>();

        if (!OperatingSystem.IsWindows())
        {
            return documents;
        }

        TryGetFromRegistry(documents);
        TryGetFromRecentFolders(documents);
        TryGetFromJumpLists(documents);

        if (documents.Count < maxCount)
        {
            TryGetFromMudToolsInterop(documents);
        }

        return documents
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(MergeDocuments)
            .OrderByDescending(d => d.RecentAccessTime ?? DateTime.MinValue)
            .ThenBy(d => d.SourcePriority)
            .ThenBy(d => d.SourceOrder)
            .ThenByDescending(d => d.LastModifiedTime)
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
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, $"Failed to open Office document '{filePath}'.", ex);
        }
    }

    private static OfficeRecentDocument MergeDocuments(IGrouping<string, OfficeRecentDocument> group)
    {
        var preferred = group
            .OrderByDescending(d => d.RecentAccessTime ?? DateTime.MinValue)
            .ThenBy(d => d.SourcePriority)
            .ThenBy(d => d.SourceOrder)
            .ThenByDescending(d => d.LastModifiedTime)
            .First();

        return new OfficeRecentDocument
        {
            FileName = preferred.FileName,
            FilePath = preferred.FilePath,
            Extension = preferred.Extension,
            LastModifiedTime = group.Max(d => d.LastModifiedTime),
            FileSizeBytes = preferred.FileSizeBytes,
            IconGlyph = preferred.IconGlyph,
            RecentAccessTime = group
                .Where(d => d.RecentAccessTime.HasValue)
                .Select(d => d.RecentAccessTime)
                .Max(),
            SourcePriority = preferred.SourcePriority,
            SourceOrder = preferred.SourceOrder
        };
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromMudToolsInterop(List<OfficeRecentDocument> documents)
    {
        try
        {
            RunOnStaThread(() =>
            {
                var sourceOrder = 0;
                TryGetFromWordInterop(documents, ref sourceOrder);
                TryGetFromExcelInterop(documents, ref sourceOrder);
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "MudTools.OfficeInterop recent-document read failed.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromWordInterop(List<OfficeRecentDocument> documents, ref int sourceOrder)
    {
        if (!TryGetOfficeApplication("Word.Application", out var comObject, out var createdNew))
        {
            return;
        }

        object? application = null;

        try
        {
            application = WordFactory.Connection(comObject!);

            if (createdNew)
            {
                TrySetProperty(comObject, "Visible", false);
                TrySetProperty(application, "DisplayAlerts", WdAlertLevel.wdAlertsNone);
            }

            AddInteropRecentFiles(documents, GetPropertyValue(application, "RecentFiles"), 0, ref sourceOrder);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "Failed to read Word recent files via MudTools.OfficeInterop.", ex);
        }
        finally
        {
            CleanupOfficeApplication(application, comObject, createdNew);
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromExcelInterop(List<OfficeRecentDocument> documents, ref int sourceOrder)
    {
        if (!TryGetOfficeApplication("Excel.Application", out var comObject, out var createdNew))
        {
            return;
        }

        object? application = null;

        try
        {
            application = ExcelFactory.Connection(comObject!);

            if (createdNew)
            {
                TrySetProperty(comObject, "Visible", false);
                TrySetProperty(application, "DisplayAlerts", false);
            }

            AddInteropRecentFiles(documents, GetPropertyValue(application, "RecentFiles"), 0, ref sourceOrder);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "Failed to read Excel recent files via MudTools.OfficeInterop.", ex);
        }
        finally
        {
            CleanupOfficeApplication(application, comObject, createdNew);
        }
    }

    private void AddInteropRecentFiles(
        List<OfficeRecentDocument> documents,
        object? recentFiles,
        int sourcePriority,
        ref int sourceOrder)
    {
        if (recentFiles == null)
        {
            return;
        }

        var count = GetIntProperty(recentFiles, "Count");
        var itemProperty = recentFiles.GetType().GetProperty("Item");
        if (count <= 0 || itemProperty == null)
        {
            return;
        }

        for (var index = 1; index <= count; index++)
        {
            try
            {
                var recentFile = itemProperty.GetValue(recentFiles, new object[] { index });
                var filePath = GetStringProperty(recentFile, "Path");
                AddDocumentIfExists(documents, filePath, sourcePriority, sourceOrder++, null);
            }
            catch
            {
                // Ignore a single malformed MRU entry and keep processing the rest.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetOfficeApplication(string progId, out object? comObject, out bool createdNew)
    {
        comObject = null;
        createdNew = false;

        var applicationType = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (applicationType == null)
        {
            return false;
        }

        try
        {
            comObject = Activator.CreateInstance(applicationType);
            createdNew = comObject != null;
            return comObject != null;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CleanupOfficeApplication(object? application, object? comObject, bool createdNew)
    {
        try
        {
            if (createdNew && application != null)
            {
                InvokeParameterlessMethod(application, "Quit");
            }
        }
        catch
        {
        }

        try
        {
            if (application is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
        }

        ReleaseComObject(application);
        if (!ReferenceEquals(application, comObject))
        {
            ReleaseComObject(comObject);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? instance)
    {
        if (instance == null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        using var finished = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                finished.Set();
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        finished.Wait();

        if (exception != null)
        {
            throw new InvalidOperationException("Failed to run Office interop on STA thread.", exception);
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromRegistry(List<OfficeRecentDocument> documents)
    {
        try
        {
            using var officeRoot = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office");
            if (officeRoot == null)
            {
                return;
            }

            var versions = officeRoot
                .GetSubKeyNames()
                .Where(IsOfficeVersionKey)
                .OrderByDescending(ParseVersionKey)
                .ToList();

            var sourceOrder = 0;
            foreach (var version in versions)
            {
                TryGetFromRegistryApp(documents, version, "Word", ref sourceOrder);
                TryGetFromRegistryApp(documents, version, "Excel", ref sourceOrder);
                TryGetFromRegistryApp(documents, version, "PowerPoint", ref sourceOrder);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "Failed to read Office MRU entries from the registry.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromRegistryApp(List<OfficeRecentDocument> documents, string version, string appName, ref int sourceOrder)
    {
        TryGetFromRegistryMruKey(documents, $@"Software\Microsoft\Office\{version}\{appName}\File MRU", ref sourceOrder);

        using var userMruRoot = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Office\{version}\{appName}\User MRU");
        if (userMruRoot == null)
        {
            return;
        }

        foreach (var identityKey in userMruRoot.GetSubKeyNames())
        {
            TryGetFromRegistryMruKey(
                documents,
                $@"Software\Microsoft\Office\{version}\{appName}\User MRU\{identityKey}\File MRU",
                ref sourceOrder);
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryGetFromRegistryMruKey(List<OfficeRecentDocument> documents, string registryPath, ref int sourceOrder)
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        if (key == null)
        {
            return;
        }

        var entries = key
            .GetValueNames()
            .Where(name => name.StartsWith("Item ", StringComparison.OrdinalIgnoreCase))
            .Select(name => new
            {
                Name = name,
                Order = ParseMruItemOrder(name),
                Value = key.GetValue(name) as string
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .OrderBy(entry => entry.Order);

        foreach (var entry in entries)
        {
            var (filePath, recentAccessTime) = ParseOfficeMruValue(entry.Value!);
            AddDocumentIfExists(documents, filePath, 1, sourceOrder++, recentAccessTime);
        }
    }

    private void TryGetFromRecentFolders(List<OfficeRecentDocument> documents)
    {
        try
        {
            var linkFiles = GetRecentFolders()
                .Where(Directory.Exists)
                .SelectMany(path => Directory.EnumerateFiles(path, "*.lnk"))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            var sourceOrder = 0;
            foreach (var linkFile in linkFiles)
            {
                var targetPath = GetShortcutTarget(linkFile.FullName);
                AddDocumentIfExists(documents, targetPath, 2, sourceOrder++, linkFile.LastWriteTime);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "Failed to read Windows Recent shortcut folders.", ex);
        }
    }

    private void TryGetFromJumpLists(List<OfficeRecentDocument> documents)
    {
        try
        {
            var jumpListFiles = GetJumpListFolders()
                .Where(Directory.Exists)
                .SelectMany(path => Directory.EnumerateFiles(path, "*.automaticDestinations-ms")
                    .Concat(Directory.EnumerateFiles(path, "*.customDestinations-ms")))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            var sourceOrder = 0;
            foreach (var jumpListFile in jumpListFiles)
            {
                TryParseJumpListFile(jumpListFile, documents, ref sourceOrder);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(LogCategory, "Failed to read Windows Jump Lists for Office documents.", ex);
        }
    }

    private void TryParseJumpListFile(FileInfo jumpListFile, List<OfficeRecentDocument> documents, ref int sourceOrder)
    {
        try
        {
            var bytes = File.ReadAllBytes(jumpListFile.FullName);
            foreach (var filePath in ExtractPossiblePaths(bytes))
            {
                AddDocumentIfExists(documents, filePath, 3, sourceOrder++, jumpListFile.LastWriteTime);
            }
        }
        catch
        {
            // Ignore a single Jump List file and keep scanning the rest.
        }
    }

    private static IEnumerable<string> ExtractPossiblePaths(byte[] bytes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in new[]
                 {
                     Encoding.Unicode.GetString(bytes),
                     Encoding.Latin1.GetString(bytes)
                 })
        {
            foreach (Match match in OfficeFilePathRegex.Matches(text))
            {
                var normalizedPath = NormalizeFilePath(match.Value);
                if (!string.IsNullOrWhiteSpace(normalizedPath))
                {
                    paths.Add(normalizedPath);
                }
            }
        }

        return paths;
    }

    private void AddDocumentIfExists(
        List<OfficeRecentDocument> documents,
        string? filePath,
        int sourcePriority,
        int sourceOrder,
        DateTime? recentAccessTime)
    {
        try
        {
            var normalizedPath = NormalizeFilePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (!IsOfficeFile(extension) || !File.Exists(normalizedPath))
            {
                return;
            }

            var fileInfo = new FileInfo(normalizedPath);
            documents.Add(new OfficeRecentDocument
            {
                FileName = Path.GetFileNameWithoutExtension(normalizedPath),
                FilePath = normalizedPath,
                Extension = extension,
                LastModifiedTime = fileInfo.LastWriteTime,
                FileSizeBytes = fileInfo.Length,
                IconGlyph = GetIconGlyph(extension),
                RecentAccessTime = recentAccessTime,
                SourcePriority = sourcePriority,
                SourceOrder = sourceOrder
            });
        }
        catch
        {
            // Ignore a single file and keep processing the rest of the MRU list.
        }
    }

    private static IEnumerable<string> GetRecentFolders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            Path.Combine(appData, "Microsoft", "Windows", "Recent"),
            Path.Combine(appData, "Microsoft", "Word", "Recent"),
            Path.Combine(appData, "Microsoft", "Excel", "Recent"),
            Path.Combine(appData, "Microsoft", "PowerPoint", "Recent"),
            Path.Combine(localAppData, "Microsoft", "Office", "Word", "Recent"),
            Path.Combine(localAppData, "Microsoft", "Office", "Excel", "Recent"),
            Path.Combine(localAppData, "Microsoft", "Office", "PowerPoint", "Recent")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetJumpListFolders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            Path.Combine(appData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"),
            Path.Combine(appData, "Microsoft", "Windows", "Recent", "CustomDestinations"),
            Path.Combine(localAppData, "Microsoft", "Windows", "Recent", "AutomaticDestinations"),
            Path.Combine(localAppData, "Microsoft", "Windows", "Recent", "CustomDestinations")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsOfficeVersionKey(string keyName)
    {
        return Version.TryParse(keyName, out _);
    }

    private static Version ParseVersionKey(string keyName)
    {
        return Version.TryParse(keyName, out var version) ? version : new Version(0, 0);
    }

    private static int ParseMruItemOrder(string valueName)
    {
        var numberText = valueName["Item ".Length..];
        return int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : int.MaxValue;
    }

    private static (string? FilePath, DateTime? RecentAccessTime) ParseOfficeMruValue(string rawValue)
    {
        var filePath = ExtractOfficeFilePath(rawValue);
        DateTime? recentAccessTime = null;

        var timestampMatch = OfficeMruTimestampRegex.Match(rawValue);
        if (timestampMatch.Success &&
            long.TryParse(timestampMatch.Groups["filetime"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fileTime) &&
            fileTime > 0)
        {
            try
            {
                recentAccessTime = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
            }
            catch
            {
                recentAccessTime = null;
            }
        }

        return (filePath, recentAccessTime);
    }

    private static string? ExtractOfficeFilePath(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var markerIndex = rawValue.LastIndexOf('*');
        var candidate = markerIndex >= 0
            ? rawValue[(markerIndex + 1)..]
            : rawValue;

        var normalizedCandidate = NormalizeFilePath(candidate);
        if (!string.IsNullOrWhiteSpace(normalizedCandidate) && IsOfficeFile(Path.GetExtension(normalizedCandidate)))
        {
            return normalizedCandidate;
        }

        var match = OfficeFilePathRegex.Match(rawValue);
        return match.Success ? NormalizeFilePath(match.Value) : null;
    }

    private static string? NormalizeFilePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var candidate = rawPath.Trim('\0', ' ', '"');
        candidate = Environment.ExpandEnvironmentVariables(candidate);

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            candidate = uri.LocalPath;
        }

        candidate = candidate.Replace('/', '\\');
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static bool IsOfficeFile(string extension)
    {
        return OfficeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               ExcelExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               PowerPointExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
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

    private static object? GetPropertyValue(object? instance, string propertyName)
    {
        return instance?.GetType().GetProperty(propertyName)?.GetValue(instance);
    }

    private static string? GetStringProperty(object? instance, string propertyName)
    {
        return GetPropertyValue(instance, propertyName) as string;
    }

    private static int GetIntProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue => (int)longValue,
            _ => 0
        };
    }

    private static void TrySetProperty(object? instance, string propertyName, object value)
    {
        var property = instance?.GetType().GetProperty(propertyName);
        if (property?.CanWrite == true)
        {
            property.SetValue(instance, value);
        }
    }

    private static void InvokeParameterlessMethod(object instance, string methodName)
    {
        instance.GetType().GetMethod(methodName, Type.EmptyTypes)?.Invoke(instance, null);
    }
}
