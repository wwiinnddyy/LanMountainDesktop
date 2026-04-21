using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Loading;

/// <summary>
/// 加载状态管理使用示例
/// </summary>
public static class LoadingStateUsageExample
{
    /// <summary>
    /// 示例：插件加载
    /// </summary>
    public static async Task LoadPluginsExample(LoadingStateManager manager)
    {
        // 注册插件加载项
        var pluginItem = manager.RegisterItem(
            "plugins.core",
            LoadingItemType.Plugin,
            "核心插件",
            "加载系统核心插件",
            new Dictionary<string, string> { { "version", "1.0.0" } });

        // 开始加载
        manager.StartItem("plugins.core", "正在下载插件...");

        try
        {
            // 模拟下载进度
            for (int i = 0; i <= 100; i += 10)
            {
                manager.UpdateProgress(
                    "plugins.core",
                    i,
                    $"正在下载... {i}%",
                    estimatedRemainingSeconds: (100 - i) / 10);

                await Task.Delay(100);
            }

            // 完成加载
            manager.CompleteItem("plugins.core", "核心插件加载完成");
        }
        catch (Exception ex)
        {
            // 标记失败
            manager.FailItem("plugins.core", "插件加载失败", ex.Message);
        }
    }

    /// <summary>
    /// 示例：组件加载
    /// </summary>
    public static async Task LoadComponentsExample(LoadingStateManager manager)
    {
        var components = new[]
        {
            ("comp.weather", "天气组件"),
            ("comp.clock", "时钟组件"),
            ("comp.calendar", "日历组件")
        };

        foreach (var (id, name) in components)
        {
            // 注册组件
            manager.RegisterItem(id, LoadingItemType.Component, name);

            // 开始加载
            manager.StartItem(id, $"正在加载 {name}...");

            // 模拟加载过程
            for (int i = 0; i <= 100; i += 20)
            {
                manager.UpdateProgress(id, i);
                await Task.Delay(50);
            }

            // 完成
            manager.CompleteItem(id, $"{name} 加载完成");
        }
    }

    /// <summary>
    /// 示例：网络资源加载
    /// </summary>
    public static async Task LoadNetworkResourcesExample(LoadingStateManager manager)
    {
        // 注册网络加载项
        manager.RegisterItem(
            "network.config",
            LoadingItemType.Network,
            "配置数据",
            "从服务器获取最新配置");

        manager.StartItem("network.config", "正在连接服务器...");

        try
        {
            // 模拟网络请求
            await Task.Delay(1000);

            manager.UpdateProgress("network.config", 50, "正在下载数据...");

            await Task.Delay(1000);

            manager.CompleteItem("network.config", "配置数据已更新");
        }
        catch (Exception ex)
        {
            manager.FailItem("network.config", "网络请求失败", ex.Message);
        }
    }

    /// <summary>
    /// 示例：带超时的加载
    /// </summary>
    public static async Task LoadWithTimeoutExample(
        LoadingStateManager manager,
        LoadingTimeoutHandler timeoutHandler)
    {
        // 设置超时时间为 10 秒
        timeoutHandler.SetItemTimeout("data.heavy", TimeSpan.FromSeconds(10));

        // 注册加载项
        manager.RegisterItem(
            "data.heavy",
            LoadingItemType.Data,
            "大数据处理",
            "处理大量数据，可能需要较长时间");

        // 订阅超时事件
        timeoutHandler.ItemTimeout += (s, e) =>
        {
            Console.WriteLine($"加载项 '{e.ItemName}' 超时！");
        };

        timeoutHandler.ItemRetry += (s, e) =>
        {
            Console.WriteLine($"正在重试 '{e.ItemName}' ({e.RetryCount}/{e.MaxRetryCount})...");
        };

        // 开始加载
        manager.StartItem("data.heavy", "正在处理数据...");

        // 模拟长时间操作
        await Task.Delay(15000);

        // 完成
        manager.CompleteItem("data.heavy", "数据处理完成");
    }

    /// <summary>
    /// 示例：完整启动流程
    /// </summary>
    public static async Task FullStartupExample(
        LoadingStateManager manager,
        LoadingStateReporter reporter,
        LoadingTimeoutHandler timeoutHandler)
    {
        // 启动超时处理器
        timeoutHandler.Start();

        // 设置阶段
        manager.SetStage(StartupStage.Initializing, "开始初始化...");

        // 1. 系统初始化
        manager.RegisterItem("system.init", LoadingItemType.System, "系统初始化");
        manager.StartItem("system.init");
        await Task.Delay(500);
        manager.CompleteItem("system.init");

        // 2. 加载设置
        manager.SetStage(StartupStage.LoadingSettings, "正在加载设置...");
        manager.RegisterItem("settings.load", LoadingItemType.Settings, "用户设置");
        manager.StartItem("settings.load");
        await Task.Delay(800);
        manager.CompleteItem("settings.load");

        // 3. 加载插件
        manager.SetStage(StartupStage.LoadingPlugins, "正在加载插件...");
        await LoadPluginsExample(manager);

        // 4. 加载组件
        await LoadComponentsExample(manager);

        // 5. 加载网络资源
        await LoadNetworkResourcesExample(manager);

        // 6. 初始化界面
        manager.SetStage(StartupStage.InitializingUI, "正在初始化界面...");
        manager.RegisterItem("ui.init", LoadingItemType.System, "界面初始化");
        manager.StartItem("ui.init");
        await Task.Delay(600);
        manager.CompleteItem("ui.init");

        // 完成
        manager.SetStage(StartupStage.Ready, "加载完成");

        // 停止超时处理器
        timeoutHandler.Stop();
    }
}
