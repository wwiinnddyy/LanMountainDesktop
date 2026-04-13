# 01-PluginBase详解

`PluginBase` 是插件的基类，提供基础功能和生命周期管理。本文详细讲解其用法和扩展点。

---

## 🎯 PluginBase 概述

```csharp
public abstract class PluginBase : IPlugin
{
    // 日志记录器
    protected ILogger? Logger { get; }
    
    // 初始化方法（必须实现）
    public abstract void Initialize(HostBuilderContext context, IServiceCollection services);
}
```

---

## 📝 基本用法

### 最小实现

```csharp
using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyPlugin;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 插件初始化逻辑
    }
}
```

---

## 🔧 Initialize 方法详解

### 方法签名

```csharp
public abstract void Initialize(
    HostBuilderContext context,      // 宿主构建上下文
    IServiceCollection services      // 服务注册集合
);
```

### 参数说明

| 参数 | 类型 | 用途 |
|-----|------|------|
| `context` | `HostBuilderContext` | 访问宿主配置、环境信息 |
| `services` | `IServiceCollection` | 注册服务、组件、设置页面 |

### context 使用示例

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    // 访问配置
    var configValue = context.Configuration["MySetting"];
    
    // 判断运行环境
    var isDevelopment = context.HostingEnvironment.IsDevelopment();
    
    // 获取应用名称
    var appName = context.HostingEnvironment.ApplicationName;
}
```

---

## 📝 日志记录

### 使用 Logger 属性

```csharp
[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 记录日志
        Logger?.LogInformation("插件初始化开始");
        
        try
        {
            // 初始化逻辑
            services.AddSingleton<IMyService, MyService>();
            
            Logger?.LogInformation("插件初始化完成");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "插件初始化失败");
            throw;
        }
    }
}
```

### 日志级别

```csharp
Logger?.LogTrace("详细跟踪信息");
Logger?.LogDebug("调试信息");
Logger?.LogInformation("一般信息");
Logger?.LogWarning("警告信息");
Logger?.LogError("错误信息");
Logger?.LogCritical("严重错误");
```

---

## 🔌 服务注册

### 注册单例服务

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    // 单例 - 整个应用生命周期只有一个实例
    services.AddSingleton<IWeatherService, WeatherService>();
}
```

### 注册作用域服务

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    // 作用域 - 每个作用域一个实例
    services.AddScoped<IDataContext, DataContext>();
}
```

### 注册瞬态服务

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    // 瞬态 - 每次请求都创建新实例
    services.AddTransient<IValidator, Validator>();
}
```

### 带配置的服务注册

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    services.AddSingleton<IWeatherService>(provider =>
    {
        var httpClient = provider.GetRequiredService<HttpClient>();
        var logger = provider.GetRequiredService<ILogger<WeatherService>>();
        var apiKey = context.Configuration["WeatherApiKey"];
        
        return new WeatherService(httpClient, logger, apiKey);
    });
}
```

---

## 🧩 完整示例

```csharp
using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WeatherPlugin;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        Logger?.LogInformation("天气插件初始化开始");
        
        try
        {
            // 1. 注册 HTTP 客户端
            services.AddHttpClient("weather", client =>
            {
                client.BaseAddress = new Uri("https://api.weather.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });
            
            // 2. 注册服务
            services.AddSingleton<IWeatherService, WeatherService>();
            services.AddSingleton<ILocationService, LocationService>();
            
            // 3. 注册桌面组件
            services.AddPluginDesktopComponent<WeatherWidget>(
                new PluginDesktopComponentOptions
                {
                    ComponentId = "WeatherPlugin.Widget",
                    DisplayName = "天气",
                    IconKey = "Weather",
                    Category = "信息",
                    MinWidthCells = 4,
                    MinHeightCells = 3
                });
            
            // 4. 注册设置页面
            services.AddPluginSettingsSection(
                "weather-settings",
                "天气设置",
                section => section
                    .AddText("api_key", "API密钥", isPassword: true)
                    .AddText("default_city", "默认城市", defaultValue: "北京")
                    .AddToggle("auto_refresh", "自动刷新", defaultValue: true)
                    .AddNumber("refresh_interval", "刷新间隔(分钟)", 
                        defaultValue: 30, minimum: 5, maximum: 120),
                iconKey: "Settings");
            
            Logger?.LogInformation("天气插件初始化完成");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "天气插件初始化失败");
            throw;
        }
    }
}
```

---

## 💡 最佳实践

### 1. 使用 try-catch 包装初始化逻辑

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    try
    {
        // 初始化逻辑
    }
    catch (Exception ex)
    {
        Logger?.LogError(ex, "初始化失败");
        throw;  // 重新抛出，让宿主知道初始化失败
    }
}
```

### 2. 按依赖顺序注册服务

```csharp
// ✅ 先注册被依赖的服务
services.AddSingleton<IDataService, DataService>();

// 再注册依赖它们的服务
services.AddSingleton<IWeatherService, WeatherService>();  // 依赖 IDataService

// 最后注册组件
services.AddPluginDesktopComponent<WeatherWidget>(options);
```

### 3. 记录初始化过程

```csharp
public override void Initialize(HostBuilderContext context, IServiceCollection services)
{
    Logger?.LogInformation("开始初始化...");
    
    Logger?.LogDebug("注册服务...");
    services.AddSingleton<IMyService, MyService>();
    
    Logger?.LogDebug("注册组件...");
    services.AddPluginDesktopComponent<MyWidget>(options);
    
    Logger?.LogInformation("初始化完成");
}
```

---

## 📚 参考资源

- [PluginBase 源码](../../LanMountainDesktop.PluginSdk/PluginBase.cs)
- [IPlugin 接口](../../LanMountainDesktop.PluginSdk/IPlugin.cs)
- [Microsoft.Extensions.DependencyInjection 文档](https://docs.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection)

---

## 🎯 下一步

学习组件注册 API：

👉 **[02-组件注册与配置](02-组件注册与配置.md)**

---

*最后更新：2026年4月*
