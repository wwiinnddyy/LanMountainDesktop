using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LanDesktopPLONDS.Installer;

internal static class NativeDependencyBootstrapper
{
    private const string CacheRootEnvironmentVariable = "LANDESKTOPPLONDS_INSTALLER_NATIVE_CACHE";
    private const string ResourcePrefix = "LanDesktopPLONDS.Installer.NativeLibraries.";

    private static readonly string[] NativeLibraryNames =
    [
        "av_libglesv2.dll",
        "libHarfBuzzSharp.dll",
        "libSkiaSharp.dll"
    ];

    public static void Prepare()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var nativeDirectory = GetNativeDirectory();
        Directory.CreateDirectory(nativeDirectory);

        var extractedLibraries = new List<string>(NativeLibraryNames.Length);
        foreach (var libraryName in NativeLibraryNames)
        {
            extractedLibraries.Add(ExtractLibrary(nativeDirectory, libraryName));
        }

        AddToProcessDllSearchPath(nativeDirectory);

        foreach (var libraryPath in extractedLibraries)
        {
            NativeLibrary.Load(libraryPath);
        }
    }

    private static string GetNativeDirectory()
    {
        var configuredCacheRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheRoot = !string.IsNullOrWhiteSpace(configuredCacheRoot)
            ? configuredCacheRoot
            : string.IsNullOrWhiteSpace(localAppData)
                ? Path.GetTempPath()
                : localAppData;

        string? versionStamp = null;
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            versionStamp = FileVersionInfo.GetVersionInfo(Environment.ProcessPath).ProductVersion;
        }

        if (string.IsNullOrWhiteSpace(versionStamp))
        {
            versionStamp = "dev";
        }

        return Path.Combine(
            cacheRoot,
            "LanDesktopPLONDS",
            "Installer",
            "native",
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            SanitizePathSegment(versionStamp));
    }

    private static string ExtractLibrary(string nativeDirectory, string libraryName)
    {
        var resourceName = ResourcePrefix + libraryName + ".gz";
        var assembly = Assembly.GetExecutingAssembly();
        using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource is null)
        {
            var availableResources = string.Join(", ", assembly.GetManifestResourceNames());
            throw new FileNotFoundException(
                $"Missing embedded native installer library resource '{resourceName}'. Available resources: {availableResources}");
        }

        var destinationPath = Path.Combine(nativeDirectory, libraryName);
        var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
        using (var output = File.Create(temporaryPath))
        {
            gzip.CopyTo(output);
        }

        if (File.Exists(destinationPath) && FilesEqual(destinationPath, temporaryPath))
        {
            File.Delete(temporaryPath);
            return destinationPath;
        }

        File.Move(temporaryPath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private static void AddToProcessDllSearchPath(string nativeDirectory)
    {
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!currentPath.Contains(nativeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", nativeDirectory + Path.PathSeparator + currentPath);
        }

        if (!SetDllDirectory(nativeDirectory))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to update the process native DLL search path.");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value;
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        var left = new FileInfo(leftPath);
        var right = new FileInfo(rightPath);
        if (left.Length != right.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(leftPath);
        using var rightStream = File.OpenRead(rightPath);
        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[81920];

        while (true)
        {
            var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            for (var i = 0; i < leftRead; i++)
            {
                if (leftBuffer[i] != rightBuffer[i])
                {
                    return false;
                }
            }
        }
    }

    [DllImport("kernel32", EntryPoint = "SetDllDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string pathName);
}
