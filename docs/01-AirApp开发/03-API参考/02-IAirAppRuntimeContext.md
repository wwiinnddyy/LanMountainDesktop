# IAirAppRuntimeContext

轻应用的运行时上下文，由宿主在 `IAirApp.OnStartedAsync` 中传入，
也可以从本轻应用的服务容器解析。命名空间 `LanMountainDesktop.AirAppSdk`。

## 成员一览

| 成员 | 类型 | 说明 |
|---|---|---|
| `Manifest` | `AirAppManifest` | 本轻应用的清单 |
| `AirAppDirectory` | `string` | 包所在目录，**只读** |
| `DataDirectory` | `string` | 持久化数据目录 |
| `CacheDirectory` | `string` | 缓存目录，可被清理 |
| `Services` | `IServiceProvider` | 本轻应用的服务容器 |
| `Properties` | `IReadOnlyDictionary<string, object?>` | 宿主属性 |
| `Lifetime` | `IHostApplicationLifetime` | 宿主生命周期 |
| `MessageBus` | `IAirAppMessageBus` | 消息总线 |
| `Appearance` | `IAirAppAppearanceContext` | 主题与外观 |
| `Logger` | `IAirAppLogger` | 日志 |
| `GetService<T>()` | `T?` | 从容器解析服务 |
| `TryGetProperty<T>(key, out value)` | `bool` | 读取宿主属性 |
| `OpenWindowAsync(windowId)` | `Task<IAirAppWindow>` | 打开窗口 |
| `CloseWindow(windowId)` | `void` | 关闭窗口 |

## 目录

三个目录职责不同，不要混用：

```csharp
context.AirAppDirectory   // 包目录：程序集、资源。升级时整体覆盖，不要写入
context.DataDirectory     // 用户数据：配置、数据库。升级保留
context.CacheDirectory    // 缓存：可随时被删除，必须能重建
```

写文件前自行确保目录存在：

```csharp
Directory.CreateDirectory(context.DataDirectory);
var path = Path.Combine(context.DataDirectory, "state.json");
```

## 宿主属性

键定义在 `AirAppHostPropertyKeys`：

| 常量 | 键值 | 含义 |
|---|---|---|
| `HostApplicationName` | `HostApplicationName` | 宿主应用名 |
| `HostVersion` | `HostVersion` | 宿主版本 |
| `AirAppSdkApiVersion` | `AirAppSdkApiVersion` | 宿主提供的 SDK API 版本 |
| `HostLanguageCode` | `HostLanguageCode` | 当前界面语言 |

```csharp
if (context.TryGetProperty<string>(AirAppHostPropertyKeys.HostVersion, out var hostVersion))
{
    context.Logger.Info($"Running on host {hostVersion}");
}
```

> 从 PluginSdk 迁移时注意：旧的键名是 `PluginSdkApiVersion`，
> 新键名是 `AirAppSdkApiVersion`。只改常量名而不改字面量的代码会静默读不到值。

## 日志

```csharp
public interface IAirAppLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Warn(string message, Exception exception);
    void Error(string message);
    void Error(string message, Exception exception);
}
```

日志写入宿主的日志系统，自动带上轻应用 id 前缀，便于排查是哪个轻应用出的问题。

## 消息总线

```csharp
public interface IAirAppMessageBus
{
    IDisposable Subscribe<TMessage>(Action<TMessage> handler);
    void Publish<TMessage>(TMessage message);

    void Publish(string topic, object? payload = null);
    IDisposable Subscribe(string topic, Action<object?> handler);
    IDisposable Subscribe<T>(string topic, Action<T?> handler);
}
```

两种用法：

```csharp
// 强类型：双方需引用同一个消息类型（共享契约程序集）
context.MessageBus.Publish(new WeatherUpdated("杭州", 21.5));
var sub = context.MessageBus.Subscribe<WeatherUpdated>(e => { /* ... */ });

// 主题式：不需要共享类型，适合松耦合场景
context.MessageBus.Publish("weather.updated", new { City = "杭州" });
var sub2 = context.MessageBus.Subscribe<WeatherPayload>("weather.updated", p => { /* ... */ });
```

订阅返回 `IDisposable`，**必须在不再需要时释放**，否则会持有对象引用，
导致轻应用卸载时 `AssemblyLoadContext` 无法回收。

## 外观

```csharp
public interface IAirAppAppearanceContext
{
    AirAppAppearanceSnapshot Snapshot { get; }
    event EventHandler<AppearanceChangedEvent>? Changed;
    double ResolveScaledCornerRadius(double baseRadius, double? minimum = null, double? maximum = null);
    double ResolveCornerRadius(AirAppCornerRadiusPreset preset, double? minimum = null, double? maximum = null);
}
```

`Snapshot` 是只读快照，包含主题变体、强调色、种子色、色角色、材质表面、
圆角令牌等。主题变化时先更新快照再触发 `Changed`，所以在事件处理里
直接读 `Snapshot` 拿到的就是新值。

圆角一律通过 `ResolveCornerRadius` / `CornerRadiusTokens` 获取，
不要硬编码——全局圆角缩放设置需要作用到轻应用的界面上。

## 窗口

```csharp
var window = await context.OpenWindowAsync("weather-detail");
context.CloseWindow("weather-detail");
```

窗口由独立进程承载。如果宿主不支持在当前上下文打开窗口，
`OpenWindowAsync` 会抛 `NotSupportedException`。

组件里应当用 `AirAppComponentContext.OpenWindowAsync`，语义相同。

## 生命周期

```csharp
context.Lifetime.ApplicationStopping.Register(() =>
{
    // 宿主开始关闭
});
```

需要主动请求宿主退出或重启时，从容器解析 `IHostApplicationLifecycle`：

```csharp
var lifecycle = context.GetService<IHostApplicationLifecycle>();
lifecycle?.TryRestart();
```

## 从容器获取上下文

组件构造函数之外的地方（比如后台服务）可以直接注入：

```csharp
public sealed class WeatherRefreshService : BackgroundService
{
    private readonly IAirAppRuntimeContext _context;

    public WeatherRefreshService(IAirAppRuntimeContext context) => _context = context;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _context.Logger.Debug("refreshing weather");
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
```
