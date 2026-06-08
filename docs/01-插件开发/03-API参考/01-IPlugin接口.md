# IPlugin 接口详解

`IPlugin` 是所有插件的入口接口，定义了插件的基本信息和生命周期方法。

## 接口定义

```csharp
namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 插件接口
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// 插件唯一标识符
    /// 建议使用反向域名格式，如：com.example.myplugin
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// 插件显示名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 插件版本号
    /// 应遵循语义化版本规范（如：1.2.3）
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// 插件初始化
    /// 在插件加载后调用，用于注册组件、服务和事件
    /// </summary>
    /// <param name="context">插件上下文</param>
    /// <returns>异步任务</returns>
    Task InitializeAsync(IPluginContext context);
    
    /// <summary>
    /// 插件关闭
    /// 在插件卸载前调用，用于清理资源和保存状态
    /// </summary>
    /// <returns>异步任务</returns>
    Task ShutdownAsync();
}
```

## 属性详解

### Id

**类型**: `string`

**说明**: 插件的全局唯一标识符，必须在所有插件中唯一。

**命名规范**:
- 使用反向域名格式：`com.company.pluginname`
- 只包含小写字母、数字、点号和连字符
- 不能以数字或连字符开头

**示例**:
```csharp
public string Id => "com.example.weatherplugin";
```

**最佳实践**:
```csharp
// ✅ 好的示例
"com.example.weatherplugin"
"io.github.username.todoplugin"
"org.myorganization.monitorplugin"

// ❌ 不好的示例
"WeatherPlugin"              // 不是反向域名格式
"com.example.Weather Plugin" // 包含空格
"123.example.plugin"         // 以数字开头
```

### Name

**类型**: `string`

**说明**: 插件的显示名称，会在 UI 中展示给用户。

**要求**:
- 简洁明了，不超过 20 个字符
- 可以包含中文、英文、空格
- 不要包含版本号

**示例**:
```csharp
public string Name => "天气插件";
```

**最佳实践**:
```csharp
// ✅ 好的示例
"天气插件"
"待办事项"
"系统监控"

// ❌ 不好的示例
"天气插件 v1.0"              // 包含版本号
"The Best Weather Plugin"   // 过长且夸张
```

### Version

**类型**: `string`

**说明**: 插件的版本号，应遵循[语义化版本](https://semver.org/lang/zh-CN/)规范。

**格式**: `主版本号.次版本号.修订号`

**规则**:
- **主版本号**: 不兼容的 API 修改
- **次版本号**: 向下兼容的功能性新增
- **修订号**: 向下兼容的问题修正

**示例**:
```csharp
public string Version => "1.2.3";
```

**版本示例**:
```csharp
"1.0.0"  // 首个稳定版本
"1.1.0"  // 添加新功能，兼容 1.0.0
"1.1.1"  // 修复 Bug，兼容 1.1.0
"2.0.0"  // 不兼容的 API 变更
```

## 方法详解

### InitializeAsync

**签名**:
```csharp
Task InitializeAsync(IPluginContext context);
```

**说明**: 插件加载后立即调用，用于初始化插件、注册组件和服务。

**参数**:
- `context`: 插件上下文，提供对宿主服务的访问

**返回值**: 异步任务

**调用时机**: 
- 宿主启动时，所有插件发现后
- 插件热重载时

**执行要求**:
- ✅ 应该快速完成（< 5 秒）
- ✅ 耗时操作应放在后台线程
- ✅ 应该处理所有可能的异常
- ❌ 不要阻塞 UI 线程

**典型实现**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    try
    {
        // 1. 保存上下文引用
        _context = context;
        _logger = context.Logger;
        _settings = context.Settings;
        
        // 2. 记录日志
        _logger.LogInformation($"{Name} v{Version} is initializing...");
        
        // 3. 注册组件
        RegisterComponents(context);
        
        // 4. 注册设置页
        RegisterSettingsPage(context);
        
        // 5. 注册服务
        RegisterServices(context);
        
        // 6. 订阅事件
        SubscribeEvents(context);
        
        // 7. 耗时初始化（后台执行）
        _ = Task.Run(async () =>
        {
            await InitializeDataAsync();
        });
        
        _logger.LogInformation($"{Name} initialized successfully");
    }
    catch (Exception ex)
    {
        context.Logger.LogError(ex, $"Failed to initialize {Name}");
        throw; // 让宿主知道初始化失败
    }
}

private void RegisterComponents(IPluginContext context)
{
    var registry = context.Services.GetService<IComponentRegistry>();
    if (registry != null)
    {
        registry.RegisterComponent<WeatherComponent>();
        registry.RegisterComponent<ClockComponent>();
    }
}

private void RegisterSettingsPage(IPluginContext context)
{
    var settingsRegistry = context.Services
        .GetService<ISettingsPageRegistry>();
    
    if (settingsRegistry != null)
    {
        settingsRegistry.RegisterPage(
            title: "天气插件",
            category: "插件",
            pageFactory: () => new WeatherSettingsPage()
        );
    }
}

private void RegisterServices(IPluginContext context)
{
    // 注册插件内部服务
    _weatherService = new WeatherService(_settings, _logger);
}

private void SubscribeEvents(IPluginContext context)
{
    var eventBus = context.Services.GetService<IEventBus>();
    if (eventBus != null)
    {
        eventBus.Subscribe<ThemeChangedEvent>(OnThemeChanged);
    }
}

private async Task InitializeDataAsync()
{
    // 加载缓存数据
    await LoadCachedDataAsync();
    
    // 预加载资源
    await PreloadResourcesAsync();
}
```

**错误处理**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    try
    {
        // 初始化代码
    }
    catch (FileNotFoundException ex)
    {
        context.Logger.LogError(ex, "Required file not found");
        throw new PluginInitializationException(
            "插件初始化失败：缺少必需文件",
            ex
        );
    }
    catch (UnauthorizedAccessException ex)
    {
        context.Logger.LogError(ex, "Permission denied");
        throw new PluginInitializationException(
            "插件初始化失败：权限不足",
            ex
        );
    }
    catch (Exception ex)
    {
        context.Logger.LogError(ex, "Unexpected error during initialization");
        throw;
    }
}
```

### ShutdownAsync

**签名**:
```csharp
Task ShutdownAsync();
```

**说明**: 插件卸载前调用，用于清理资源、保存状态和取消订阅。

**返回值**: 异步任务

**调用时机**:
- 宿主应用关闭时
- 插件被禁用时
- 插件热重载前

**执行要求**:
- ✅ 必须快速完成（< 3 秒）
- ✅ 必须捕获所有异常，不能抛出
- ✅ 应该取消所有异步操作
- ✅ 应该释放所有资源
- ❌ 不要执行耗时操作

**典型实现**:

```csharp
public async Task ShutdownAsync()
{
    try
    {
        _logger?.LogInformation($"{Name} is shutting down...");
        
        // 1. 取消正在进行的操作
        _cancellationTokenSource?.Cancel();
        
        // 2. 取消事件订阅
        UnsubscribeEvents();
        
        // 3. 保存关键状态
        SaveState();
        
        // 4. 停止后台服务
        await StopBackgroundServicesAsync();
        
        // 5. 释放资源
        DisposeResources();
        
        _logger?.LogInformation($"{Name} shutdown completed");
    }
    catch (Exception ex)
    {
        // 记录但不抛出异常
        _logger?.LogError(ex, $"Error during {Name} shutdown");
    }
}

private void UnsubscribeEvents()
{
    var eventBus = _context?.Services.GetService<IEventBus>();
    if (eventBus != null)
    {
        eventBus.Unsubscribe<ThemeChangedEvent>(OnThemeChanged);
    }
}

private void SaveState()
{
    try
    {
        // 保存关键状态到设置
        _settings?.SetValue("LastShutdownTime", DateTime.Now);
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to save state");
    }
}

private async Task StopBackgroundServicesAsync()
{
    try
    {
        if (_weatherService != null)
        {
            await _weatherService.StopAsync();
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to stop services");
    }
}

private void DisposeResources()
{
    try
    {
        _cancellationTokenSource?.Dispose();
        _weatherService?.Dispose();
        _httpClient?.Dispose();
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to dispose resources");
    }
}
```

**超时处理**:

宿主会监控 `ShutdownAsync` 的执行时间：

```csharp
// 宿主代码（伪代码）
var shutdownTask = plugin.ShutdownAsync();
var completedTask = await Task.WhenAny(
    shutdownTask,
    Task.Delay(TimeSpan.FromSeconds(5))
);

if (completedTask != shutdownTask)
{
    _logger.LogWarning($"Plugin {plugin.Name} shutdown timeout");
    // 强制终止
}
```

所以插件应该确保快速完成：

```csharp
public async Task ShutdownAsync()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    
    try
    {
        await ShutdownInternalAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        _logger?.LogWarning("Shutdown cancelled due to timeout");
    }
}
```

## 完整示例

### 最小实现

```csharp
using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

public class Plugin : IPlugin
{
    public string Id => "com.example.minimalplugin";
    public string Name => "Minimal Plugin";
    public string Version => "1.0.0";
    
    public Task InitializeAsync(IPluginContext context)
    {
        context.Logger.LogInformation($"{Name} initialized");
        return Task.CompletedTask;
    }
    
    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }
}
```

### 完整实现

```csharp
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

/// <summary>
/// 天气插件
/// </summary>
public class WeatherPlugin : IPlugin
{
    // === 插件信息 ===
    
    public string Id => "com.example.weatherplugin";
    public string Name => "天气插件";
    public string Version => "1.2.3";
    
    // === 私有字段 ===
    
    private IPluginContext? _context;
    private ILogger? _logger;
    private ISettingsService? _settings;
    private CancellationTokenSource? _cancellationTokenSource;
    private WeatherService? _weatherService;
    
    // === 生命周期方法 ===
    
    /// <summary>
    /// 插件初始化
    /// </summary>
    public async Task InitializeAsync(IPluginContext context)
    {
        try
        {
            // 保存引用
            _context = context;
            _logger = context.Logger;
            _settings = context.Settings;
            _cancellationTokenSource = new CancellationTokenSource();
            
            _logger.LogInformation(
                "{PluginName} v{Version} is initializing...",
                Name,
                Version
            );
            
            // 注册组件
            RegisterComponents(context);
            
            // 注册设置页
            RegisterSettingsPage(context);
            
            // 初始化服务
            _weatherService = new WeatherService(
                _settings,
                _logger,
                _cancellationTokenSource.Token
            );
            
            // 订阅事件
            SubscribeToHostEvents(context);
            
            // 后台初始化
            _ = Task.Run(async () =>
            {
                await InitializeBackgroundAsync();
            });
            
            _logger.LogInformation(
                "{PluginName} initialized successfully",
                Name
            );
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(
                ex,
                "Failed to initialize {PluginName}",
                Name
            );
            throw;
        }
    }
    
    /// <summary>
    /// 插件关闭
    /// </summary>
    public async Task ShutdownAsync()
    {
        try
        {
            _logger?.LogInformation(
                "{PluginName} is shutting down...",
                Name
            );
            
            // 取消异步操作
            _cancellationTokenSource?.Cancel();
            
            // 取消订阅
            UnsubscribeFromHostEvents();
            
            // 保存状态
            SaveState();
            
            // 停止服务
            if (_weatherService != null)
            {
                await _weatherService.StopAsync();
                _weatherService.Dispose();
            }
            
            // 释放资源
            _cancellationTokenSource?.Dispose();
            
            _logger?.LogInformation(
                "{PluginName} shutdown completed",
                Name
            );
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Error during {PluginName} shutdown",
                Name
            );
            // 不抛出异常
        }
    }
    
    // === 私有方法 ===
    
    private void RegisterComponents(IPluginContext context)
    {
        var registry = context.Services
            .GetService<IComponentRegistry>();
        
        if (registry != null)
        {
            registry.RegisterComponent<WeatherComponent>();
            _logger?.LogDebug("WeatherComponent registered");
        }
    }
    
    private void RegisterSettingsPage(IPluginContext context)
    {
        var settingsRegistry = context.Services
            .GetService<ISettingsPageRegistry>();
        
        if (settingsRegistry != null)
        {
            settingsRegistry.RegisterPage(
                title: Name,
                category: "插件",
                pageFactory: () => new WeatherSettingsPage(
                    _settings!,
                    _logger!
                )
            );
            
            _logger?.LogDebug("Settings page registered");
        }
    }
    
    private void SubscribeToHostEvents(IPluginContext context)
    {
        var eventBus = context.Services.GetService<IEventBus>();
        if (eventBus != null)
        {
            eventBus.Subscribe<ThemeChangedEvent>(OnThemeChanged);
            _logger?.LogDebug("Subscribed to host events");
        }
    }
    
    private void UnsubscribeFromHostEvents()
    {
        var eventBus = _context?.Services.GetService<IEventBus>();
        if (eventBus != null)
        {
            eventBus.Unsubscribe<ThemeChangedEvent>(OnThemeChanged);
            _logger?.LogDebug("Unsubscribed from host events");
        }
    }
    
    private async Task InitializeBackgroundAsync()
    {
        try
        {
            // 加载缓存数据
            await _weatherService!.LoadCacheAsync();
            
            // 预加载天气数据
            var defaultCity = _settings!.GetValue("DefaultCity", "北京");
            await _weatherService.FetchWeatherAsync(defaultCity);
            
            _logger?.LogInformation("Background initialization completed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Background initialization failed");
        }
    }
    
    private void OnThemeChanged(ThemeChangedEvent evt)
    {
        _logger?.LogInformation(
            "Theme changed to: {Theme}",
            evt.NewTheme
        );
        // 响应主题变更
    }
    
    private void SaveState()
    {
        try
        {
            _settings?.SetValue("LastShutdownTime", DateTime.Now);
            _logger?.LogDebug("State saved");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save state");
        }
    }
}
```

## 常见问题

### Q: InitializeAsync 可以执行多久？

**A**: 建议在 5 秒内完成。超时可能导致宿主启动缓慢。耗时操作应放在后台线程。

### Q: 可以在构造函数中初始化吗？

**A**: 不建议。构造函数应该非常轻量，只初始化字段。所有初始化逻辑应在 `InitializeAsync` 中。

### Q: ShutdownAsync 可以不实现吗？

**A**: 必须实现，但可以是空实现。如果有资源需要清理，必须在此方法中处理。

### Q: 如果 InitializeAsync 失败会怎样？

**A**: 插件会被标记为"加载失败"，不会被激活，但不影响其他插件。

### Q: 可以访问其他插件的服务吗？

**A**: 不建议在 `InitializeAsync` 中访问，因为加载顺序不确定。应该在运行时通过服务定位器获取。

## 相关文档

- [IPluginContext 详解](02-IPluginContext.md) - 插件上下文
- [插件生命周期](../02-核心概念/01-插件生命周期.md) - 生命周期详解
- [创建第一个插件](../01-快速开始/02-创建第一个插件.md) - 实战教程
