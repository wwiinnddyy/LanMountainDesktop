# IPluginContext 详解

`IPluginContext` 是插件与宿主应用交互的主要接口，提供对宿主服务、日志、设置等的访问。

## 接口定义

```csharp
namespace LanMountainDesktop.PluginSdk;

/// <summary>
/// 插件上下文接口
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 插件根目录
    /// 包含插件的所有文件（DLL、资源等）
    /// </summary>
    string PluginDirectory { get; }
    
    /// <summary>
    /// 插件数据目录
    /// 用于存储插件的持久化数据（缓存、数据库等）
    /// </summary>
    string DataDirectory { get; }
    
    /// <summary>
    /// 服务提供者
    /// 用于获取宿主提供的服务
    /// </summary>
    IServiceProvider Services { get; }
    
    /// <summary>
    /// 日志记录器
    /// 用于记录插件运行日志
    /// </summary>
    ILogger Logger { get; }
    
    /// <summary>
    /// 设置服务
    /// 用于读写插件配置
    /// </summary>
    ISettingsService Settings { get; }
}
```

## 属性详解

### PluginDirectory

**类型**: `string`

**说明**: 插件的根目录，包含插件的所有文件。

**典型路径**:
```
%LOCALAPPDATA%\LanMountainDesktop\plugins\{PluginId}\
```

**用途**:
- 加载插件资源文件
- 读取配置文件
- 访问插件自带的数据文件

**示例**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    // 加载插件自带的数据文件
    var dataFile = Path.Combine(context.PluginDirectory, "data", "cities.json");
    if (File.Exists(dataFile))
    {
        var json = await File.ReadAllTextAsync(dataFile);
        var cities = JsonSerializer.Deserialize<List<City>>(json);
    }
    
    // 加载图标
    var iconPath = Path.Combine(context.PluginDirectory, "Assets", "icon.png");
    
    // 加载资源文件（使用 avares 方案更好）
    // avares://MyPlugin/Assets/icon.png
}
```

**注意事项**:
- ✅ 只能读取，不要在此目录写入文件
- ✅ 使用 `Path.Combine` 构建路径
- ❌ 不要硬编码路径
- ❌ 不要依赖目录结构（可能变化）

### DataDirectory

**类型**: `string`

**说明**: 插件的数据目录，用于存储插件生成的持久化数据。

**典型路径**:
```
%LOCALAPPDATA%\LanMountainDesktop\plugin-data\{PluginId}\
```

**用途**:
- 存储缓存文件
- 存储本地数据库
- 存储临时文件
- 存储下载的文件

**示例**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    // 确保数据目录存在
    Directory.CreateDirectory(context.DataDirectory);
    
    // 缓存文件路径
    var cacheFile = Path.Combine(context.DataDirectory, "weather-cache.json");
    
    // SQLite 数据库路径
    var dbPath = Path.Combine(context.DataDirectory, "todos.db");
    
    // 下载文件路径
    var downloadPath = Path.Combine(context.DataDirectory, "downloads");
}
```

**最佳实践**:

```csharp
public class MyPlugin : IPlugin
{
    private string? _cacheDirectory;
    private string? _logsDirectory;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        // 创建子目录组织数据
        _cacheDirectory = Path.Combine(context.DataDirectory, "cache");
        _logsDirectory = Path.Combine(context.DataDirectory, "logs");
        
        Directory.CreateDirectory(_cacheDirectory);
        Directory.CreateDirectory(_logsDirectory);
    }
    
    public async Task SaveCacheAsync(string key, string data)
    {
        var cacheFile = Path.Combine(_cacheDirectory!, $"{key}.json");
        await File.WriteAllTextAsync(cacheFile, data);
    }
}
```

**清理数据**:

```csharp
public async Task ShutdownAsync()
{
    // 清理旧的缓存文件
    if (_cacheDirectory != null)
    {
        var files = Directory.GetFiles(_cacheDirectory);
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (DateTime.Now - fileInfo.LastWriteTime > TimeSpan.FromDays(7))
            {
                File.Delete(file);
            }
        }
    }
}
```

### Services

**类型**: `IServiceProvider`

**说明**: 服务提供者，用于获取宿主提供的服务。

**常用服务**:

| 服务接口 | 说明 |
|---------|------|
| `IComponentRegistry` | 组件注册表 |
| `ISettingsPageRegistry` | 设置页注册表 |
| `IEventBus` | 事件总线 |
| `INotificationService` | 通知服务 |
| `IDialogService` | 对话框服务 |
| `IThemeService` | 主题服务 |
| `ILocalizationService` | 本地化服务 |
| `IHttpClientFactory` | HTTP 客户端工厂 |

**使用方法**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    // 获取服务
    var componentRegistry = context.Services
        .GetService<IComponentRegistry>();
    
    var eventBus = context.Services
        .GetService<IEventBus>();
    
    var themeService = context.Services
        .GetService<IThemeService>();
    
    // 检查服务是否可用
    if (componentRegistry != null)
    {
        // 使用服务
        componentRegistry.RegisterComponent<MyComponent>();
    }
    else
    {
        context.Logger.LogWarning("IComponentRegistry not available");
    }
}
```

**泛型扩展方法**:

```csharp
// 使用 Microsoft.Extensions.DependencyInjection 的扩展方法
using Microsoft.Extensions.DependencyInjection;

var componentRegistry = context.Services.GetService<IComponentRegistry>();
var eventBus = context.Services.GetRequiredService<IEventBus>(); // 不存在会抛异常
```

**服务定位器模式**:

```csharp
public class MyPlugin : IPlugin
{
    private IServiceProvider? _services;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        _services = context.Services;
    }
    
    private void SomeMethod()
    {
        // 运行时获取服务
        var notificationService = _services?
            .GetService<INotificationService>();
        
        notificationService?.ShowNotification(
            "标题",
            "内容",
            NotificationType.Information
        );
    }
}
```

### Logger

**类型**: `ILogger`

**说明**: 日志记录器，用于记录插件运行日志。

**日志级别**:

| 级别 | 方法 | 用途 |
|-----|------|------|
| Trace | `LogTrace` | 最详细的信息，用于诊断 |
| Debug | `LogDebug` | 调试信息 |
| Information | `LogInformation` | 一般信息 |
| Warning | `LogWarning` | 警告信息 |
| Error | `LogError` | 错误信息 |
| Critical | `LogCritical` | 严重错误 |

**基本用法**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    var logger = context.Logger;
    
    // 信息日志
    logger.LogInformation("Plugin is initializing");
    
    // 警告日志
    logger.LogWarning("Configuration is missing, using defaults");
    
    // 错误日志
    try
    {
        await LoadDataAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to load data");
    }
    
    // 调试日志
    logger.LogDebug("Loaded {Count} items", items.Count);
}
```

**结构化日志**:

```csharp
// ✅ 好：使用参数化日志
logger.LogInformation(
    "User {UserId} requested weather for {City}",
    userId,
    city
);

// ❌ 差：字符串拼接
logger.LogInformation(
    $"User {userId} requested weather for {city}"
);
```

**异常日志**:

```csharp
try
{
    await FetchWeatherAsync();
}
catch (HttpRequestException ex)
{
    // 记录异常和上下文
    logger.LogError(
        ex,
        "Failed to fetch weather for {City}. Retry count: {RetryCount}",
        city,
        retryCount
    );
}
catch (Exception ex)
{
    // 严重错误
    logger.LogCritical(
        ex,
        "Unexpected error in weather service"
    );
}
```

**条件日志**:

```csharp
// 检查日志级别以避免不必要的计算
if (logger.IsEnabled(LogLevel.Debug))
{
    var expensiveDebugInfo = CalculateDebugInfo(); // 只在启用 Debug 时计算
    logger.LogDebug("Debug info: {Info}", expensiveDebugInfo);
}
```

**日志作用域**:

```csharp
using (logger.BeginScope("WeatherFetch-{City}", city))
{
    logger.LogInformation("Starting fetch");
    await FetchWeatherAsync(city);
    logger.LogInformation("Fetch completed");
}
// 所有日志会包含作用域信息
```

### Settings

**类型**: `ISettingsService`

**说明**: 设置服务，用于读写插件配置。详见 [设置系统](../02-核心概念/03-设置系统.md)。

**快速示例**:

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    var settings = context.Settings;
    
    // 读取设置
    var apiKey = settings.GetValue("ApiKey", "");
    var refreshRate = settings.GetValue("RefreshRate", 10);
    var cities = settings.GetValue<List<string>>(
        "FavoriteCities",
        new List<string>()
    );
    
    // 保存设置
    settings.SetValue("LastStartTime", DateTime.Now);
    
    // 监听设置变更
    settings.SettingChanged += (sender, e) =>
    {
        if (e.Key == "ApiKey")
        {
            // 响应变更
        }
    };
}
```

## 使用模式

### 保存上下文引用

```csharp
public class MyPlugin : IPlugin
{
    private IPluginContext? _context;
    private ILogger? _logger;
    private ISettingsService? _settings;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        // 保存引用供后续使用
        _context = context;
        _logger = context.Logger;
        _settings = context.Settings;
        
        // 后续可以在任何方法中使用
    }
    
    private void SomeMethod()
    {
        _logger?.LogInformation("Doing something");
        
        var value = _settings?.GetValue("Key", "Default");
    }
}
```

### 依赖注入模式

```csharp
public class MyComponent : ComponentBase
{
    private readonly INotificationService? _notificationService;
    
    public MyComponent()
    {
        // 组件构造时注入依赖
        _notificationService = Services.GetService<INotificationService>();
    }
    
    public void NotifyUser(string message)
    {
        _notificationService?.ShowNotification(
            "提醒",
            message,
            NotificationType.Information
        );
    }
}
```

### 服务包装

```csharp
public class MyPlugin : IPlugin
{
    private WeatherService? _weatherService;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        // 创建服务包装类
        _weatherService = new WeatherService(
            context.Logger,
            context.Settings,
            context.Services.GetService<IHttpClientFactory>()
        );
        
        await _weatherService.InitializeAsync();
    }
}

public class WeatherService
{
    private readonly ILogger _logger;
    private readonly ISettingsService _settings;
    private readonly HttpClient _httpClient;
    
    public WeatherService(
        ILogger logger,
        ISettingsService settings,
        IHttpClientFactory? httpFactory)
    {
        _logger = logger;
        _settings = settings;
        _httpClient = httpFactory?.CreateClient() ?? new HttpClient();
    }
    
    public async Task InitializeAsync()
    {
        var apiKey = _settings.GetValue("ApiKey", "");
        _logger.LogInformation("Weather service initialized");
    }
}
```

## 最佳实践

### ✅ 检查服务可用性

```csharp
// ✅ 好：检查服务是否存在
var notificationService = context.Services
    .GetService<INotificationService>();

if (notificationService != null)
{
    notificationService.ShowNotification(...);
}
else
{
    context.Logger.LogWarning("Notification service not available");
}

// ❌ 差：不检查直接使用
var notificationService = context.Services
    .GetRequiredService<INotificationService>(); // 可能抛异常
```

### ✅ 使用结构化日志

```csharp
// ✅ 好：参数化日志
logger.LogInformation(
    "Processed {Count} items in {Duration}ms",
    count,
    duration
);

// ❌ 差：字符串插值
logger.LogInformation(
    $"Processed {count} items in {duration}ms"
);
```

### ✅ 正确处理路径

```csharp
// ✅ 好：使用 Path.Combine
var dataFile = Path.Combine(
    context.DataDirectory,
    "cache",
    "data.json"
);

// ❌ 差：字符串拼接
var dataFile = context.DataDirectory + "\\cache\\data.json"; // Windows 专用
```

### ✅ 清理资源

```csharp
public class MyPlugin : IPlugin
{
    private ISettingsService? _settings;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        _settings = context.Settings;
        _settings.SettingChanged += OnSettingChanged;
    }
    
    public async Task ShutdownAsync()
    {
        // 取消订阅
        if (_settings != null)
        {
            _settings.SettingChanged -= OnSettingChanged;
        }
    }
}
```

## 完整示例

```csharp
using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

public class WeatherPlugin : IPlugin
{
    public string Id => "com.example.weatherplugin";
    public string Name => "天气插件";
    public string Version => "1.0.0";
    
    // 上下文引用
    private IPluginContext? _context;
    private ILogger? _logger;
    private ISettingsService? _settings;
    private string? _dataDirectory;
    
    // 服务引用
    private INotificationService? _notificationService;
    private IHttpClientFactory? _httpFactory;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        // 1. 保存上下文引用
        _context = context;
        _logger = context.Logger;
        _settings = context.Settings;
        _dataDirectory = context.DataDirectory;
        
        _logger.LogInformation(
            "{PluginName} v{Version} initializing from {Directory}",
            Name,
            Version,
            context.PluginDirectory
        );
        
        // 2. 获取宿主服务
        _notificationService = context.Services
            .GetService<INotificationService>();
        
        _httpFactory = context.Services
            .GetService<IHttpClientFactory>();
        
        // 3. 创建数据目录
        Directory.CreateDirectory(_dataDirectory);
        
        var cacheDir = Path.Combine(_dataDirectory, "cache");
        Directory.CreateDirectory(cacheDir);
        
        _logger.LogDebug("Data directory: {Directory}", _dataDirectory);
        
        // 4. 加载配置
        var apiKey = _settings.GetValue("ApiKey", "");
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("API Key not configured");
        }
        
        // 5. 注册组件和服务
        RegisterComponents(context);
        RegisterSettingsPage(context);
        
        // 6. 订阅事件
        SubscribeEvents(context);
        
        _logger.LogInformation("{PluginName} initialized successfully", Name);
    }
    
    private void RegisterComponents(IPluginContext context)
    {
        var registry = context.Services.GetService<IComponentRegistry>();
        if (registry != null)
        {
            registry.RegisterComponent<WeatherComponent>();
            _logger?.LogDebug("Components registered");
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
    
    private void SubscribeEvents(IPluginContext context)
    {
        var eventBus = context.Services.GetService<IEventBus>();
        if (eventBus != null)
        {
            eventBus.Subscribe<ThemeChangedEvent>(OnThemeChanged);
            _logger?.LogDebug("Event subscriptions created");
        }
    }
    
    private void OnThemeChanged(ThemeChangedEvent evt)
    {
        _logger?.LogInformation("Theme changed to: {Theme}", evt.NewTheme);
    }
    
    public async Task ShutdownAsync()
    {
        _logger?.LogInformation("{PluginName} shutting down", Name);
        
        // 取消订阅
        var eventBus = _context?.Services.GetService<IEventBus>();
        if (eventBus != null)
        {
            eventBus.Unsubscribe<ThemeChangedEvent>(OnThemeChanged);
        }
        
        _logger?.LogInformation("{PluginName} shutdown completed", Name);
        await Task.CompletedTask;
    }
}
```

## 相关文档

- [IPlugin 接口](01-IPlugin接口.md) - 插件接口详解
- [设置系统](../02-核心概念/03-设置系统.md) - 设置服务详解
- [插件生命周期](../02-核心概念/01-插件生命周期.md) - 生命周期详解
