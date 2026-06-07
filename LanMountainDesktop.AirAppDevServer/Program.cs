using System.CommandLine;
using System.Diagnostics;

namespace LanMountainDesktop.AirAppDevServer;

/// <summary>
/// AirApp 开发服务器主程序
/// 提供热重载、实时预览等开发功能
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("LanMountainDesktop AirApp 开发服务器");

        // 开发模式命令
        var devCommand = new Command("dev", "启动开发服务器（支持热重载）");
        var projectPathOption = new Option<string>(
            aliases: new[] { "--project", "-p" },
            description: "AirApp 项目路径",
            getDefaultValue: () => Directory.GetCurrentDirectory());
        var portOption = new Option<int>(
            aliases: new[] { "--port" },
            description: "开发服务器端口",
            getDefaultValue: () => 5000);
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "显示详细日志");

        devCommand.AddOption(projectPathOption);
        devCommand.AddOption(portOption);
        devCommand.AddOption(verboseOption);

        devCommand.SetHandler(async (projectPath, port, verbose) =>
        {
            await RunDevServerAsync(projectPath, port, verbose);
        }, projectPathOption, portOption, verboseOption);

        // 预览命令
        var previewCommand = new Command("preview", "预览 AirApp（无需安装到宿主）");
        var componentOption = new Option<string?>(
            aliases: new[] { "--component", "-c" },
            description: "要预览的组件 ID");
        var windowOption = new Option<string?>(
            aliases: new[] { "--window", "-w" },
            description: "要预览的窗口 ID");

        previewCommand.AddOption(projectPathOption);
        previewCommand.AddOption(componentOption);
        previewCommand.AddOption(windowOption);

        previewCommand.SetHandler(async (projectPath, component, window) =>
        {
            await RunPreviewAsync(projectPath, component, window);
        }, projectPathOption, componentOption, windowOption);

        // 打包命令
        var packageCommand = new Command("package", "打包 AirApp 为 .laapp 文件");
        var outputOption = new Option<string?>(
            aliases: new[] { "--output", "-o" },
            description: "输出路径");

        packageCommand.AddOption(projectPathOption);
        packageCommand.AddOption(outputOption);

        packageCommand.SetHandler(async (projectPath, output) =>
        {
            await PackageAirAppAsync(projectPath, output);
        }, projectPathOption, outputOption);

        rootCommand.AddCommand(devCommand);
        rootCommand.AddCommand(previewCommand);
        rootCommand.AddCommand(packageCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunDevServerAsync(string projectPath, int port, bool verbose)
    {
        Console.WriteLine("🚀 启动 AirApp 开发服务器...");
        Console.WriteLine($"📁 项目路径: {projectPath}");
        Console.WriteLine($"🔌 端口: {port}");
        Console.WriteLine();

        var server = new AirAppDevServer(projectPath, port, verbose);
        await server.StartAsync();

        Console.WriteLine();
        Console.WriteLine("✅ 开发服务器已启动");
        Console.WriteLine($"🌐 预览地址: http://localhost:{port}");
        Console.WriteLine();
        Console.WriteLine("按 Ctrl+C 停止服务器...");
        Console.WriteLine();

        // 等待取消信号
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("🛑 正在停止服务器...");
        }

        await server.StopAsync();
        Console.WriteLine("✅ 服务器已停止");
    }

    static async Task RunPreviewAsync(string projectPath, string? component, string? window)
    {
        Console.WriteLine("👁️ 启动 AirApp 预览...");
        Console.WriteLine($"📁 项目路径: {projectPath}");

        var previewer = new AirAppPreviewer(projectPath);

        if (!string.IsNullOrEmpty(component))
        {
            await previewer.PreviewComponentAsync(component);
        }
        else if (!string.IsNullOrEmpty(window))
        {
            await previewer.PreviewWindowAsync(window);
        }
        else
        {
            await previewer.PreviewAllAsync();
        }
    }

    static async Task PackageAirAppAsync(string projectPath, string? output)
    {
        Console.WriteLine("📦 打包 AirApp...");
        Console.WriteLine($"📁 项目路径: {projectPath}");

        var packager = new AirAppPackager(projectPath);
        var outputPath = await packager.PackageAsync(output);

        Console.WriteLine();
        Console.WriteLine($"✅ 打包完成: {outputPath}");
    }
}
