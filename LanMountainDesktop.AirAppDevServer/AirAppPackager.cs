using System.Diagnostics;
using System.IO.Compression;

namespace LanMountainDesktop.AirAppDevServer;

/// <summary>
/// AirApp 打包工具
/// 将 AirApp 项目打包为 .laapp 文件
/// </summary>
public sealed class AirAppPackager
{
    private readonly string _projectPath;

    public AirAppPackager(string projectPath)
    {
        _projectPath = Path.GetFullPath(projectPath);
    }

    public async Task<string> PackageAsync(string? outputPath)
    {
        Console.WriteLine("🔨 构建项目...");
        if (!await BuildProjectAsync())
        {
            throw new InvalidOperationException("构建失败");
        }

        var binPath = Path.Combine(_projectPath, "bin", "Release", "net10.0");
        if (!Directory.Exists(binPath))
        {
            binPath = Path.Combine(_projectPath, "bin", "Debug", "net10.0");
            if (!Directory.Exists(binPath))
            {
                throw new InvalidOperationException("未找到构建输出");
            }
        }

        Console.WriteLine($"📁 输出目录: {binPath}");

        // 确定输出文件名
        var projectName = Path.GetFileNameWithoutExtension(
            Directory.GetFiles(_projectPath, "*.csproj").FirstOrDefault() ?? "AirApp");

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = Path.Combine(binPath, $"{projectName}.laapp");
        }
        else
        {
            outputPath = Path.GetFullPath(outputPath);
            if (Directory.Exists(outputPath))
            {
                outputPath = Path.Combine(outputPath, $"{projectName}.laapp");
            }
        }

        // 删除旧的包
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        Console.WriteLine($"📦 打包到: {outputPath}");

        // 创建 ZIP 包
        using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
        {
            var filesToPackage = Directory.GetFiles(binPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".pdb") && !f.EndsWith(".laapp"))
                .ToList();

            Console.WriteLine($"📄 打包 {filesToPackage.Count} 个文件...");

            foreach (var file in filesToPackage)
            {
                var relativePath = Path.GetRelativePath(binPath, file);
                archive.CreateEntryFromFile(file, relativePath);
            }
        }

        Console.WriteLine($"✅ 包大小: {new FileInfo(outputPath).Length / 1024} KB");

        return outputPath;
    }

    private async Task<bool> BuildProjectAsync()
    {
        var projectFile = Directory.GetFiles(_projectPath, "*.csproj").FirstOrDefault();
        if (projectFile == null)
        {
            Console.WriteLine("❌ 未找到项目文件");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectFile}\" -c Release --nologo",
            WorkingDirectory = _projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) return false;

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            Console.WriteLine($"❌ 构建错误:\n{error}");
            return false;
        }

        return true;
    }
}
