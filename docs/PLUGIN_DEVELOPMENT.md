# 插件开发指南

> 为 LanMountainDesktop 开发自定义插件

## 目录

- [快速开始](#快速开始)
- [插件架构](#插件架构)
- [创建插件](#创建插件)
- [插件生命周期](#插件生命周期)
- [添加组件](#添加组件)
- [添加设置页](#添加设置页)
- [使用服务](#使用服务)
- [打包和发布](#打包和发布)
- [最佳实践](#最佳实践)

## 快速开始

### 安装插件模板

```bash
# 安装官方插件模板
dotnet new install LanMountainDesktop.PluginTemplate

# 查看可用模板
dotnet new list | findstr lmd
```

### 创建新插件

```bash
# 创建插件项目
dotnet new lmd-plugin -n MyAwesomePlugin

# 进入项目目录
cd MyAwesomePlugin

# 还原依赖
dotnet restore

# 构建插件
dotnet build
```

### 项目结构

```
MyAwesomePlugin/
├── MyAwesomePlugin.csproj          # 项目文件
├── Plugin.cs                        # 插件入口
├── Components/                      # 组件目录
│   └── MyComponent.cs
├── Views/                           # 视图目录
│   └── MyComponentView.axaml
├── ViewModels/                      # 视图模型
│   └── MyComponentViewModel.cs
├── Settings/                        # 设置页
│   └── MySettingsPage.axaml
└── plugin.json                      # 插件清单
```

## 插件架构

### 插件 SDK 版本

当前 SDK 版本: **4.0.1**

```xml
<PackageReference Include="LanMountainDesktop.PluginSdk" Version="4.0.1" />
<PackageReference Include="LanMountainDesktop.Shared.Contracts" Version="4.0.1" />
```

### 插件清单 (plugin.json)

```json
{
  "Id": "com.example.myawesomeplugin",
  "Name": "My Awesome Plugin",
  "Version": "1.0.0",
  "Author": "Your Name",
  "Description": "A plugin that does awesome things",
  "MinHostVersion": "1.0.0",
  "Dependencies": [],
  "Permissions": [
    "FileSystem.Read",
    "Network.Access"
  ]
}
```

### 核心接口

**IPlugin** - 插件入口接口:
```csharp
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    
    Task InitializeAsync(IPluginContext context);
    Task ShutdownAsync();
}
```

**IPluginContext** - 插件上下文:
```csharp
public interface IPluginContext
{
    string PluginDirectory { get; }
    IServiceProvider Services { get; }
    ILogger Logger { get; }
    ISettingsService Settings { get; }
}
```

## 创建插件

### 1. 实现插件入口

```csharp
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Shared.Contracts;

namespace MyAwesomePlugin;

public class Plugin : IPlugin
{
    public string Id => "com.example.myawesomeplugin";
    public string Name => "My Awesome Plugin";
    public string Version => "1.0.0";
    
    private IPluginContext? _context;
    
    public async Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        
        // 注册组件
        var componentRegistry = context.Services.GetService<IComponentRegistry>();
        componentRegistry?.RegisterComponent<MyComponent>();
        
        // 注册设置页
        var settingsRegistry = context.Services.GetService<ISettingsPageRegistry>();
        settingsRegistry?.RegisterPage<MySettingsPage>("我的插件设置");
        
        // 初始化逻辑
        context.Logger.LogInformation("Plugin initialized");
        
        await Task.CompletedTask;
    }
    
    public async Task ShutdownAsync()
    {
        // 清理资源
        _context?.Logger.LogInformation("Plugin shutting down");
        await Task.CompletedTask;
    }
}
```

### 2. 配置项目文件

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    
    <!-- 插件元数据 -->
    <PluginId>com.example.myawesomeplugin</PluginId>
    <PluginName>My Awesome Plugin</PluginName>
    <PluginVersion>1.0.0</PluginVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LanMountainDesktop.PluginSdk" Version="4.0.1" />
    <PackageReference Include="LanMountainDesktop.Shared.Contracts" Version="4.0.1" />
    <PackageReference Include="Avalonia" Version="11.3.12" />
  </ItemGroup>

  <!-- 复制 plugin.json 到输出目录 -->
  <ItemGroup>
    <None Update="plugin.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

## 插件生命周期

### 生命周期阶段

```
1. 发现 (Discovery)
   ↓
2. 加载 (Load)
   ├─ 加载程序集
   ├─ 验证依赖
   └─ 创建插件实例
   ↓
3. 初始化 (Initialize)
   ├─ 调用 InitializeAsync()
   ├─ 注册组件
   ├─ 注册设置页
   └─ 初始化服务
   ↓
4. 运行 (Running)
   ├─ 组件渲染
   ├─ 事件处理
   └─ 服务调用
   ↓
5. 关闭 (Shutdown)
   ├─ 调用 ShutdownAsync()
   ├─ 清理资源
   └─ 卸载程序集
```

### 生命周期钩子

```csharp
public class Plugin : IPlugin
{
    // 插件加载后立即调用
    public async Task InitializeAsync(IPluginContext context)
    {
        // 注册组件、服务、设置页
        // 初始化资源
    }
    
    // 插件卸载前调用
    public async Task ShutdownAsync()
    {
        // 保存状态
        // 释放资源
        // 取消订阅
    }
}
```

## 添加组件

### 1. 定义组件类

```csharp
using LanMountainDesktop.PluginSdk.Components;
using LanMountainDesktop.Shared.Contracts;

namespace MyAwesomePlugin.Components;

[Component(
    Id = "com.example.myawesomeplugin.mycomponent",
    Name = "我的组件",
    Description = "一个很棒的组件",
    Category = "工具",
    Icon = "avares://MyAwesomePlugin/Assets/icon.png"
)]
public class MyComponent : ComponentBase
{
    public override string Id => "com.example.myawesomeplugin.mycomponent";
    public override string Name => "我的组件";
    
    // 组件设置
    private string _message = "Hello, World!";
    
    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
    
    // 组件初始化
    public override Task InitializeAsync()
    {
        // 加载设置
        Message = Settings.GetValue("Message", "Hello, World!");
        return Task.CompletedTask;
    }
    
    // 组件更新 (定时调用)
    public override Task UpdateAsync()
    {
        // 更新组件数据
        return Task.CompletedTask;
    }
}
```

### 2. 创建组件视图

**MyComponentView.axaml:**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MyAwesomePlugin.ViewModels"
             x:Class="MyAwesomePlugin.Views.MyComponentView"
             x:DataType="vm:MyComponentViewModel">
  <Border Background="{DynamicResource CardBackgroundBrush}"
          CornerRadius="{DynamicResource DesignCornerRadiusComponent}"
          Padding="16">
    <StackPanel Spacing="8">
      <TextBlock Text="{Binding Component.Name}"
                 FontSize="18"
                 FontWeight="Bold" />
      
      <TextBlock Text="{Binding Component.Message}"
                 TextWrapping="Wrap" />
      
      <Button Content="点击我"
              Command="{Binding ClickCommand}" />
    </StackPanel>
  </Border>
</UserControl>
```

**MyComponentView.axaml.cs:**
```csharp
using Avalonia.Controls;

namespace MyAwesomePlugin.Views;

public partial class MyComponentView : UserControl
{
    public MyComponentView()
    {
        InitializeComponent();
    }
}
```

### 3. 创建视图模型

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MyAwesomePlugin.ViewModels;

public partial class MyComponentViewModel : ObservableObject
{
    [ObservableProperty]
    private MyComponent _component;
    
    public MyComponentViewModel(MyComponent component)
    {
        _component = component;
    }
    
    [RelayCommand]
    private void Click()
    {
        Component.Message = "按钮被点击了!";
    }
}
```

### 4. 注册组件

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    var componentRegistry = context.Services.GetService<IComponentRegistry>();
    
    // 注册组件
    componentRegistry?.RegisterComponent<MyComponent>(
        componentFactory: () => new MyComponent(),
        viewFactory: (component) => new MyComponentView
        {
            DataContext = new MyComponentViewModel((MyComponent)component)
        }
    );
}
```

## 添加设置页

### 1. 创建设置页视图

**MySettingsPage.axaml:**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyAwesomePlugin.Settings.MySettingsPage">
  <StackPanel Spacing="16" Margin="24">
    <TextBlock Text="我的插件设置"
               FontSize="24"
               FontWeight="Bold" />
    
    <StackPanel Spacing="8">
      <TextBlock Text="消息内容:" />
      <TextBox x:Name="MessageTextBox"
               Watermark="输入消息..." />
    </StackPanel>
    
    <Button Content="保存"
            Click="SaveButton_Click" />
  </StackPanel>
</UserControl>
```

**MySettingsPage.axaml.cs:**
```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.PluginSdk;

namespace MyAwesomePlugin.Settings;

public partial class MySettingsPage : UserControl
{
    private readonly ISettingsService _settings;
    
    public MySettingsPage(ISettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        
        // 加载设置
        MessageTextBox.Text = _settings.GetValue("Message", "Hello, World!");
    }
    
    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        // 保存设置
        _settings.SetValue("Message", MessageTextBox.Text);
        
        // 显示提示
        // TODO: 显示保存成功提示
    }
}
```

### 2. 注册设置页

```csharp
public async Task InitializeAsync(IPluginContext context)
{
    var settingsRegistry = context.Services.GetService<ISettingsPageRegistry>();
    
    settingsRegistry?.RegisterPage(
        title: "我的插件",
        category: "插件",
        pageFactory: () => new MySettingsPage(context.Settings)
    );
}
```

## 使用服务

### 可用服务

**ILogger** - 日志服务:
```csharp
context.Logger.LogInformation("信息日志");
context.Logger.LogWarning("警告日志");
context.Logger.LogError("错误日志");
```

**ISettingsService** - 设置服务:
```csharp
// 读取设置
var value = context.Settings.GetValue("Key", "DefaultValue");

// 写入设置
context.Settings.SetValue("Key", "NewValue");

// 监听设置变化
context.Settings.SettingChanged += (sender, e) =>
{
    if (e.Key == "Key")
    {
        // 设置已变更
    }
};
```

**INotificationService** - 通知服务:
```csharp
var notificationService = context.Services.GetService<INotificationService>();

notificationService?.ShowNotification(
    title: "通知标题",
    message: "通知内容",
    type: NotificationType.Information
);
```

**IHttpClientFactory** - HTTP 客户端:
```csharp
var httpFactory = context.Services.GetService<IHttpClientFactory>();
var httpClient = httpFactory?.CreateClient();

var response = await httpClient.GetStringAsync("https://api.example.com/data");
```

## 打包和发布

### 1. 构建插件

```bash
dotnet build -c Release
```

### 2. 打包为 .laapp

```bash
# 使用官方打包脚本
pwsh ./scripts/Pack-PluginPackages.ps1 -PluginProject ./MyAwesomePlugin/MyAwesomePlugin.csproj

# 或手动打包
cd MyAwesomePlugin/bin/Release/net10.0
zip -r MyAwesomePlugin-1.0.0.laapp *
```

### 3. 测试插件

```bash
# 安装插件
LanMountainDesktop.Launcher.exe plugin install MyAwesomePlugin-1.0.0.laapp

# 启动应用测试
LanMountainDesktop.Launcher.exe launch
```

### 4. 发布插件

**选项 1: GitHub Release**
1. 创建 GitHub 仓库
2. 上传 `.laapp` 文件到 Release
3. 用户可以手动下载安装

**选项 2: 插件市场** (如果可用)
1. 提交插件到官方市场
2. 等待审核
3. 用户可以在应用内浏览和安装

## 最佳实践

### 性能优化

1. **避免阻塞 UI 线程:**
```csharp
// 错误
public override Task UpdateAsync()
{
    Thread.Sleep(1000); // 阻塞!
    return Task.CompletedTask;
}

// 正确
public override async Task UpdateAsync()
{
    await Task.Delay(1000);
}
```

2. **使用异步 API:**
```csharp
// 使用 async/await
var data = await httpClient.GetStringAsync(url);
```

3. **缓存数据:**
```csharp
private string? _cachedData;
private DateTime _cacheTime;

public async Task<string> GetDataAsync()
{
    if (_cachedData != null && DateTime.Now - _cacheTime < TimeSpan.FromMinutes(5))
        return _cachedData;
    
    _cachedData = await FetchDataAsync();
    _cacheTime = DateTime.Now;
    return _cachedData;
}
```

### 资源管理

1. **实现 IDisposable:**
```csharp
public class MyComponent : ComponentBase, IDisposable
{
    private HttpClient? _httpClient;
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
```

2. **取消订阅事件:**
```csharp
public override Task ShutdownAsync()
{
    context.Settings.SettingChanged -= OnSettingChanged;
    return Task.CompletedTask;
}
```

### 错误处理

1. **捕获异常:**
```csharp
public override async Task UpdateAsync()
{
    try
    {
        await FetchDataAsync();
    }
    catch (HttpRequestException ex)
    {
        Logger.LogError(ex, "Failed to fetch data");
        // 显示错误提示给用户
    }
}
```

2. **验证输入:**
```csharp
public void SetUrl(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        throw new ArgumentException("URL cannot be empty", nameof(url));
    
    if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        throw new ArgumentException("Invalid URL format", nameof(url));
    
    _url = url;
}
```

### 本地化

1. **使用资源文件:**
```csharp
// Resources/Strings.resx
// Name: ComponentName, Value: My Component

public override string Name => Resources.Strings.ComponentName;
```

2. **支持多语言:**
```xml
<!-- Resources/Strings.zh-CN.resx -->
<data name="ComponentName" xml:space="preserve">
  <value>我的组件</value>
</data>
```

### 安全性

1. **验证用户输入:**
```csharp
// 防止路径遍历
var safePath = Path.GetFullPath(Path.Combine(pluginDirectory, userInput));
if (!safePath.StartsWith(pluginDirectory))
    throw new SecurityException("Invalid path");
```

2. **使用 HTTPS:**
```csharp
// 强制使用 HTTPS
if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    throw new SecurityException("Only HTTPS URLs are allowed");
```

## 示例插件

查看官方示例插件:
- **天气组件** - 显示天气信息
- **倒计时组件** - 倒计时功能
- **RSS 阅读器** - 订阅和显示 RSS 源

仓库: https://github.com/YourOrg/LanMountainDesktop.SamplePlugin

## 相关文档

- [Plugin SDK v4 迁移指南](PLUGIN_SDK_V4_MIGRATION.md)
- [组件开发指南](COMPONENT_DEVELOPMENT.md)
- [API 参考](API_REFERENCE.md)
- [架构文档](ARCHITECTURE.md)
