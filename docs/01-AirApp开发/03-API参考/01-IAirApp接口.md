# IAirApp 接口

轻应用的入口契约。命名空间 `LanMountainDesktop.AirAppSdk`。

## 定义

```csharp
public interface IAirApp
{
    void Initialize(HostBuilderContext context, IServiceCollection services);
    Task OnStartedAsync(IAirAppRuntimeContext context);
    Task OnStoppingAsync();
}
```

实现方式通常是继承 `AirAppBase` 并按需重写，三个方法都有空实现默认值：

```csharp
[AirAppEntrance]
public sealed class MyAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services) { }
    public override Task OnStartedAsync(IAirAppRuntimeContext context) => Task.CompletedTask;
    public override Task OnStoppingAsync() => Task.CompletedTask;
}
```

`AirAppBase` 还提供一个受保护属性 `RuntimeContext`，在 `OnStartedAsync`
被调用后可用（默认实现会赋值；如果你重写了 `OnStartedAsync` 且不调用
`base.OnStartedAsync(context)`，它会保持为 `null`）。

## AirAppEntranceAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AirAppEntranceAttribute : Attribute;
```

标注入口类。规则：

- 程序集中必须存在**恰好一个**实现 `IAirApp` 的具体类型
- 存在多个时，必须用本特性指明其中一个，否则加载失败
- 只有一个时，特性可省略，但建议始终标注

## Initialize

```csharp
void Initialize(HostBuilderContext context, IServiceCollection services)
```

**唯一的注册时机**。方法返回后宿主立即构建服务容器，之后对
`IServiceCollection` 的任何修改都不会生效。

这个方法在宿主启动路径上，所有轻应用的耗时会直接累加到桌面可见时间，
因此**不要**在这里做文件读写、网络请求或任何阻塞操作。

## 注册扩展方法

全部定义在 `AirAppServiceCollectionExtensions` 上。

### 桌面组件

```csharp
IServiceCollection AddAirAppComponent<TControl>(
    string componentId,
    string displayName,
    Action<AirAppComponentOptions>? configure = null) where TControl : Control;

IServiceCollection AddAirAppComponent<TControl>(
    AirAppComponentOptions options) where TControl : Control;
```

### 组件编辑器

```csharp
IServiceCollection AddAirAppComponentEditor<TControl>(
    string componentId,
    double preferredWidth = 720,
    double preferredHeight = 540,
    double minScale = 0.85,
    double maxScale = 1.45) where TControl : Control;
```

### 窗口轻应用

```csharp
IServiceCollection AddAirAppWindow<TWindow>(string id, string name)
    where TWindow : class, IAirAppWindow;
```

窗口由独立的 AirAppHost 进程承载。

### 设置

```csharp
IServiceCollection AddAirAppSettingsSection(
    string id,
    string titleLocalizationKey,
    Action<AirAppSettingsSectionBuilder> configure,
    string? descriptionLocalizationKey = null,
    string iconKey = "PuzzlePiece",
    int sortOrder = 0);

IServiceCollection AddAirAppSettingsSection<TView>(
    string id,
    string titleLocalizationKey,
    string? descriptionLocalizationKey = null,
    string iconKey = "PuzzlePiece",
    int sortOrder = 0) where TView : AirAppSettingsPageBase;
```

### 导出契约

向其他轻应用暴露强类型服务：

```csharp
IServiceCollection AddAirAppExport<TContract, TImplementation>()
    where TContract : class
    where TImplementation : class, TContract;
```

契约类型需要放在一个独立的共享契约程序集里，由双方共同引用，
并在两边的 `airapp.json` 的 `sharedContracts` 中声明。

### 公共 IPC

向宿主进程之外暴露服务：

```csharp
IServiceCollection AddAirAppPublicIpc<TContract, TImplementation>(...);
IServiceCollection AddAirAppPublicIpcContributor<TContributor>();
```

### 后台服务

用标准的宿主接口，不需要 SDK 专用 API：

```csharp
services.AddHostedService<MyBackgroundService>();
```

宿主会在轻应用初始化完成后启动这些 `IHostedService`，并在关闭时停止它们。

## OnStartedAsync

```csharp
Task OnStartedAsync(IAirAppRuntimeContext context)
```

服务容器就绪、后台服务启动之后调用。这里才拿得到
[IAirAppRuntimeContext](02-IAirAppRuntimeContext.md)。
适合做需要运行时服务的初始化：读取数据、订阅事件、首次拉取远程数据。

## OnStoppingAsync

```csharp
Task OnStoppingAsync()
```

宿主关闭时调用。必须尽快返回——关闭协调有时间预算，超时会被强制结束，
未落盘的数据会丢失。需要长时间清理的工作应当在运行期增量完成。

## 完整示例

```csharp
using LanMountainDesktop.AirAppSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.WeatherAirApp;

[AirAppEntrance]
public sealed class WeatherAirApp : AirAppBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddHostedService<WeatherRefreshService>();

        services.AddAirAppComponent<WeatherWidget>(
            "weather-now",
            "实时天气",
            options =>
            {
                options.Category = "信息";
                options.IconKey = "WeatherSunny";
                options.MinWidthCells = 2;
                options.MinHeightCells = 2;
            });

        services.AddAirAppWindow<WeatherDetailWindow>("weather-detail", "天气详情");

        services.AddAirAppSettingsSection(
            "weather",
            "settings.weather.title",
            section => section
                .AddToggle("auto_refresh", "settings.weather.autoRefresh", defaultValue: true)
                .AddNumber("interval", "settings.weather.interval",
                    defaultValue: 30, minimum: 5, maximum: 240));
    }

    public override Task OnStartedAsync(IAirAppRuntimeContext context)
    {
        context.Logger.Info($"Weather AirApp started, data at {context.DataDirectory}");
        return base.OnStartedAsync(context);
    }
}
```
