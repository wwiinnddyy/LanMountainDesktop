using System.Diagnostics;
using System.Text.Json;

namespace LanMountainDesktop.AirAppDevServer;

/// <summary>
/// AirApp 预览工具
/// 在独立窗口中预览组件或窗口，无需安装到宿主
/// </summary>
public sealed class AirAppPreviewer
{
    private readonly string _projectPath;

    public AirAppPreviewer(string projectPath)
    {
        _projectPath = Path.GetFullPath(projectPath);
    }

    public async Task PreviewComponentAsync(string componentId)
    {
        Console.WriteLine($"🎨 预览组件: {componentId}");
        await LaunchPreviewAsync("component", componentId);
    }

    public async Task PreviewWindowAsync(string windowId)
    {
        Console.WriteLine($"🪟 预览窗口: {windowId}");
        await LaunchPreviewAsync("window", windowId);
    }

    public async Task PreviewAllAsync()
    {
        Console.WriteLine("📋 加载 AirApp 清单...");

        var manifest = await LoadManifestAsync();
        if (manifest == null)
        {
            Console.WriteLine("❌ 未找到 airapp.json");
            return;
        }

        Console.WriteLine($"✅ AirApp: {manifest.Name}");
        Console.WriteLine();

        // 显示可用的组件和窗口
        if (manifest.Components?.Count > 0)
        {
            Console.WriteLine("📦 可用组件:");
            foreach (var comp in manifest.Components)
            {
                Console.WriteLine($"  - {comp.Id}: {comp.Name}");
            }
            Console.WriteLine();
        }

        if (manifest.Windows?.Count > 0)
        {
            Console.WriteLine("🪟 可用窗口:");
            foreach (var win in manifest.Windows)
            {
                Console.WriteLine($"  - {win.Id}: {win.Name}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("使用以下命令预览:");
        Console.WriteLine("  airapp-dev preview --component <component-id>");
        Console.WriteLine("  airapp-dev preview --window <window-id>");
    }

    private async Task LaunchPreviewAsync(string type, string id)
    {
        // 确保项目已构建
        var binPath = Path.Combine(_projectPath, "bin", "Debug", "net10.0");
        if (!Directory.Exists(binPath))
        {
            Console.WriteLine("❌ 未找到构建输出，请先运行: dotnet build");
            return;
        }

        Console.WriteLine($"📁 输出路径: {binPath}");
        Console.WriteLine("🚀 启动预览窗口...");
        Console.WriteLine();
        Console.WriteLine("💡 提示: 关闭预览窗口以退出");
        Console.WriteLine();

        // TODO: 这里需要启动一个预览宿主应用
        // 预览宿主会加载 AirApp 并显示指定的组件或窗口
        Console.WriteLine("⚠️ 预览功能需要配合 LanMountainDesktop 宿主运行");
        Console.WriteLine("   暂时请使用: dotnet run --project LanMountainDesktop.csproj -- --debug-airapp <path>");

        await Task.CompletedTask;
    }

    private async Task<ManifestModel?> LoadManifestAsync()
    {
        var manifestPath = Path.Combine(_projectPath, "airapp.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        return JsonSerializer.Deserialize<ManifestModel>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private sealed class ManifestModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ComponentModel>? Components { get; set; }
        public List<WindowModel>? Windows { get; set; }
    }

    private sealed class ComponentModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class WindowModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
