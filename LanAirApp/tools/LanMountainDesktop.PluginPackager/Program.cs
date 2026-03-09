using System.IO.Compression;
using LanMountainDesktop.PluginSdk;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)))
    {
        PrintUsage();
        return 0;
    }

    string? inputDirectory = null;
    string? outputPath = null;
    var overwrite = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--input":
                inputDirectory = ReadValue(args, ref i, "--input");
                break;
            case "--output":
                outputPath = ReadValue(args, ref i, "--output");
                break;
            case "--overwrite":
                overwrite = true;
                break;
            default:
                throw new InvalidOperationException($"Unknown argument '{args[i]}'.");
        }
    }

    if (string.IsNullOrWhiteSpace(inputDirectory))
    {
        throw new InvalidOperationException("Missing required argument '--input'.");
    }

    var fullInputDirectory = Path.GetFullPath(inputDirectory);
    if (!Directory.Exists(fullInputDirectory))
    {
        throw new DirectoryNotFoundException($"Plugin build directory '{fullInputDirectory}' was not found.");
    }

    var manifestPath = Path.Combine(fullInputDirectory, PluginSdkInfo.ManifestFileName);
    if (!File.Exists(manifestPath))
    {
        throw new FileNotFoundException(
            $"Plugin build directory '{fullInputDirectory}' does not contain '{PluginSdkInfo.ManifestFileName}'.",
            manifestPath);
    }

    var manifest = PluginManifest.Load(manifestPath);
    var entranceAssemblyPath = manifest.ResolveEntranceAssemblyPath(manifestPath);
    if (!File.Exists(entranceAssemblyPath))
    {
        throw new FileNotFoundException(
            $"The entrance assembly declared by '{PluginSdkInfo.ManifestFileName}' was not found.",
            entranceAssemblyPath);
    }

    outputPath ??= Path.Combine(
        Path.GetDirectoryName(fullInputDirectory) ?? fullInputDirectory,
        BuildPackageFileName(manifest.Id));

    var fullOutputPath = Path.GetFullPath(outputPath);
    var inputDirectoryWithSeparator = EnsureTrailingSeparator(fullInputDirectory);
    if (fullOutputPath.StartsWith(inputDirectoryWithSeparator, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The output .laapp path cannot be placed inside the source directory.");
    }

    var destinationDirectory = Path.GetDirectoryName(fullOutputPath);
    if (string.IsNullOrWhiteSpace(destinationDirectory))
    {
        throw new InvalidOperationException("Failed to determine the output directory for the .laapp package.");
    }

    Directory.CreateDirectory(destinationDirectory);
    if (File.Exists(fullOutputPath))
    {
        if (!overwrite)
        {
            throw new InvalidOperationException(
                $"The output package '{fullOutputPath}' already exists. Pass '--overwrite' to replace it.");
        }

        File.Delete(fullOutputPath);
    }

    await Task.Run(() => ZipFile.CreateFromDirectory(
        fullInputDirectory,
        fullOutputPath,
        CompressionLevel.Optimal,
        includeBaseDirectory: false));

    Console.WriteLine($"Packaged '{manifest.Name}' to '{fullOutputPath}'.");
    return 0;
}

static string ReadValue(IReadOnlyList<string> args, ref int index, string optionName)
{
    var nextIndex = index + 1;
    if (nextIndex >= args.Count)
    {
        throw new InvalidOperationException($"Missing value for '{optionName}'.");
    }

    index = nextIndex;
    return args[nextIndex];
}

static string BuildPackageFileName(string pluginId)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var safeName = new string(pluginId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    return safeName + PluginSdkInfo.PackageFileExtension;
}

static string EnsureTrailingSeparator(string path)
{
    return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
        ? path
        : path + Path.DirectorySeparatorChar;
}

static void PrintUsage()
{
    Console.WriteLine("LanMountainDesktop.PluginPackager");
    Console.WriteLine("Usage:");
    Console.WriteLine("  --input <plugin build directory>   Required");
    Console.WriteLine("  --output <path to .laapp>          Optional");
    Console.WriteLine("  --overwrite                        Optional");
}
