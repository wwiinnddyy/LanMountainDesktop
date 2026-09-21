using System.CommandLine;
using System.Diagnostics;

namespace LanMountainDesktop.AirAppDevServer;

/// <summary>
/// AirApp 开发工具入口：dev = 改文件自动重建；preview = 报清单与构建输出；package = 打 .laapp。
/// 三个命令都不开端口、不起窗口。
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("LanMountainDesktop AirApp 开发服务器");

        // 开发模式命令
        var devCommand = new Command("dev", "启动开发服务器（支持热重载）");
        var projectPathOption = new Option<string>("--project", "-p")
        {
            Description = "AirApp 项目路径",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
            Recursive = true
        };
        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "显示详细日志"
        };

        rootCommand.Options.Add(projectPathOption);
        devCommand.Options.Add(verboseOption);

        devCommand.SetAction(async parseResult =>
        {
            await RunDevServerAsync(
                parseResult.GetValue(projectPathOption) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(verboseOption));
        });

        // 预览命令
        var previewCommand = new Command("preview", "列出 AirApp 清单声明的组件/窗口并确认构建输出（不会打开窗口）");
        var componentOption = new Option<string?>("--component", "-c")
        {
            Description = "要预览的组件 ID"
        };
        var windowOption = new Option<string?>("--window", "-w")
        {
            Description = "要预览的窗口 ID"
        };

        previewCommand.Options.Add(componentOption);
        previewCommand.Options.Add(windowOption);

        previewCommand.SetAction(async parseResult =>
        {
            await RunPreviewAsync(
                parseResult.GetValue(projectPathOption) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(componentOption),
                parseResult.GetValue(windowOption));
        });

        // 打包命令
        var packageCommand = new Command("package", "打包 AirApp 为 .laapp 文件");
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "输出路径"
        };

        packageCommand.Options.Add(outputOption);

        packageCommand.SetAction(async parseResult =>
        {
            await PackageAirAppAsync(
                parseResult.GetValue(projectPathOption) ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(outputOption));
        });

        rootCommand.Subcommands.Add(devCommand);
        rootCommand.Subcommands.Add(previewCommand);
        rootCommand.Subcommands.Add(packageCommand);

        return await rootCommand.Parse(args).InvokeAsync();
    }

    static async Task RunDevServerAsync(string projectPath, bool verbose)
    {
        Console.WriteLine("🔨 AirApp 开发监视（改文件自动重建）...");
        Console.WriteLine($"📁 项目路径: {projectPath}");
        Console.WriteLine();

        var server = new AirAppDevServer(projectPath, verbose);
        await server.StartAsync();

        Console.WriteLine();
        Console.WriteLine("✅ 监视已启动");
        Console.WriteLine("本命令只做构建与监视，不开任何端口；要看效果请把产物装进宿主（package 命令或宿主开发模式）。");
        Console.WriteLine();
        Console.WriteLine("按 Ctrl+C 停止...");
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
