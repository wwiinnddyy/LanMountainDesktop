# 插件开发完整指南

欢迎来到阑山桌面插件开发指南！本章节将带你从零开始，掌握插件开发的完整流程。

## 📚 学习路径

### 初学者路径

如果你是第一次开发阑山桌面插件，请按以下顺序学习：

1. **[环境准备](01-快速开始/01-环境准备.md)** - 配置开发环境和工具
2. **[创建第一个插件](01-快速开始/02-创建第一个插件.md)** - 快速上手
3. **[插件生命周期](02-核心概念/01-插件生命周期.md)** - 理解插件运行机制
4. **[组件系统](02-核心概念/02-组件系统.md)** - 创建桌面组件

### 进阶路径

已经了解基础，想要深入学习？

1. **[设置系统](02-核心概念/03-设置系统.md)** - 管理插件配置
2. **[主题与外观](02-核心概念/04-主题外观.md)** - 适配暗色/亮色模式
3. **[插件通信](02-核心概念/05-插件通信.md)** - 插件间数据交互
4. **[IPC 公共服务](03-API参考/05-IPC公共服务.md)** - 对外提供服务

### 实战路径

通过完整案例学习：

1. **[天气组件插件](04-实战案例/01-天气组件.md)** - 完整的组件开发
2. **[待办事项插件](04-实战案例/02-待办事项.md)** - 数据持久化
3. **[RSS 阅读器](04-实战案例/03-RSS阅读器.md)** - 网络请求和列表展示
4. **[系统监控插件](04-实战案例/04-系统监控.md)** - 系统信息获取

## 🎯 核心概念

### 什么是插件？

插件是扩展阑山桌面功能的独立模块，可以：

- ✅ 添加新的桌面组件（Widget）
- ✅ 注册设置页面
- ✅ 提供后台服务
- ✅ 与其他插件通信
- ✅ 对外提供 IPC 服务

### 插件架构

```
┌─────────────────────────────────────┐
│      LanMountainDesktop Host        │
│  (桌面宿主 - 主程序)                  │
├─────────────────────────────────────┤
│     Plugin Runtime (插件运行时)      │
│  ┌────────────┐  ┌────────────┐    │
│  │  Plugin A  │  │  Plugin B  │    │
│  ├────────────┤  ├────────────┤    │
│  │ Components │  │ Components │    │
│  │  Settings  │  │  Settings  │    │
│  │  Services  │  │  Services  │    │
│  └────────────┘  └────────────┘    │
└─────────────────────────────────────┘
```

### 插件 SDK 版本

| SDK 版本 | 发布时间 | 主要特性 |
|---------|---------|---------|
| **5.0.0** | 2025.05 | 当前版本 - 进程隔离准备、IPC 公共服务 |
| 4.0.0 | 2025.03 | 组件系统重构、设置域管理 |
| 3.0.0 | 2025.01 | Avalonia 12 升级 |
| 2.0.0 | 2024.11 | 稳定 API，插件市场支持 |
| 1.0.0 | 2024.09 | 初始版本 |

## 📖 文档结构

### [01-快速开始](01-快速开始/)

快速上手，从零到一创建插件

- [环境准备](01-快速开始/01-环境准备.md) - 安装工具和模板
- [创建第一个插件](01-快速开始/02-创建第一个插件.md) - 实现基本功能
- [调试与测试](01-快速开始/03-调试测试.md) - 调试技巧
- [打包插件](01-快速开始/04-打包插件.md) - 生成 .laapp 文件

### [02-核心概念](02-核心概念/)

深入理解插件系统的工作原理

- [插件生命周期](02-核心概念/01-插件生命周期.md) - 加载、初始化、卸载
- [组件系统](02-核心概念/02-组件系统.md) - 桌面组件的创建和管理
- [设置系统](02-核心概念/03-设置系统.md) - 配置持久化
- [主题与外观](02-核心概念/04-主题外观.md) - 适配主题和圆角系统
- [插件通信](02-核心概念/05-插件通信.md) - 插件间协作

### [03-API参考](03-API参考/)

完整的 API 文档和使用示例

- [IPlugin 接口](03-API参考/01-IPlugin接口.md) - 插件入口
- [IPluginContext](03-API参考/02-IPluginContext.md) - 插件上下文
- [组件 API](03-API参考/03-组件API.md) - 组件开发接口
- [设置 API](03-API参考/04-设置API.md) - 设置管理接口
- [IPC 公共服务](03-API参考/05-IPC公共服务.md) - 对外服务接口
- [日志 API](03-API参考/06-日志API.md) - 日志记录

### [04-实战案例](04-实战案例/)

通过完整示例学习插件开发

- [天气组件](04-实战案例/01-天气组件.md) - API 调用、数据展示
- [待办事项](04-实战案例/02-待办事项.md) - 数据持久化、CRUD
- [RSS 阅读器](04-实战案例/03-RSS阅读器.md) - 网络请求、列表
- [系统监控](04-实战案例/04-系统监控.md) - 系统信息、实时更新

### [05-发布维护](05-发布维护/)

插件的发布、更新和维护

- [版本管理](05-发布维护/01-版本管理.md) - 语义化版本
- [CI/CD 配置](05-发布维护/02-CICD配置.md) - 自动构建
- [发布到市场](05-发布维护/03-发布市场.md) - 插件市场发布
- [用户反馈](05-发布维护/04-用户反馈.md) - 收集和处理反馈
- [迁移指南](05-发布维护/05-迁移指南.md) - SDK 版本升级

## 🚀 快速参考

### 创建插件

```powershell
# 安装模板
dotnet new install LanMountainDesktop.PluginTemplate

# 创建项目
dotnet new lmd-plugin -n MyPlugin

# 构建
dotnet build
```

### 插件入口

```csharp
public class Plugin : IPlugin
{
    public string Id => "com.example.myplugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";
    
    public async Task InitializeAsync(IPluginContext context)
    {
        // 注册组件
        var registry = context.Services.GetService<IComponentRegistry>();
        registry?.RegisterComponent<MyComponent>();
    }
    
    public Task ShutdownAsync() => Task.CompletedTask;
}
```

### 创建组件

```csharp
[Component(
    Id = "com.example.myplugin.mycomponent",
    Name = "我的组件",
    Category = "工具"
)]
public class MyComponent : ComponentBase
{
    public override string Id => "com.example.myplugin.mycomponent";
    public override string Name => "我的组件";
}
```

## 💡 最佳实践

### 代码规范

- ✅ 使用异步编程（`async/await`）
- ✅ 启用可空引用类型（`nullable enable`）
- ✅ 编写 XML 文档注释
- ✅ 遵循 C# 命名约定
- ✅ 使用依赖注入模式

### 性能优化

- ✅ 避免阻塞 UI 线程
- ✅ 使用延迟加载
- ✅ 缓存数据避免重复计算
- ✅ 及时释放资源（实现 `IDisposable`）
- ✅ 使用弱事件模式避免内存泄漏

### 用户体验

- ✅ 适配亮色/暗色主题
- ✅ 支持多语言本地化
- ✅ 提供友好的错误提示
- ✅ 响应式设计适配不同分辨率
- ✅ 提供设置页让用户自定义

## 🔗 相关资源

### 官方资源

- [GitHub 仓库](https://github.com/HelloWRC/LanMountainDesktop)
- [插件示例](https://github.com/HelloWRC/LanMountainDesktop.SamplePlugin)
- [SDK 源码](https://github.com/HelloWRC/LanMountainDesktop/tree/main/LanMountainDesktop.PluginSdk)
- [问题反馈](https://github.com/HelloWRC/LanMountainDesktop/issues)

### 技术文档

- [Avalonia UI 文档](https://docs.avaloniaui.net/)
- [FluentAvalonia 文档](https://github.com/amwx/FluentAvalonia/wiki)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [.NET API 浏览器](https://learn.microsoft.com/dotnet/api/)

### 社区

- [GitHub Discussions](https://github.com/HelloWRC/LanMountainDesktop/discussions) - 技术讨论
- [插件市场](https://github.com/HelloWRC/LanMountainDesktop/wiki/Plugins) - 浏览现有插件

## ❓ 常见问题

### 我需要什么基础？

- **必需**: C# 基础语法、面向对象编程
- **推荐**: XAML/Avalonia UI 基础、MVVM 模式
- **加分**: 异步编程、依赖注入

### 插件可以做什么？

插件可以：
- ✅ 添加桌面组件（显示天气、时钟、待办等）
- ✅ 添加设置页面
- ✅ 提供后台服务（定时任务、数据同步等）
- ✅ 与其他插件通信
- ✅ 通过 IPC 对外提供服务

插件不能：
- ❌ 修改宿主核心代码
- ❌ 直接访问其他插件的私有数据
- ❌ 绕过权限系统访问敏感资源

### 如何调试插件？

1. 将插件构建到宿主的插件目录
2. 启动宿主应用
3. 使用 IDE 附加到宿主进程
4. 在插件代码中设置断点

详见 [调试与测试](01-快速开始/03-调试测试.md)

### 插件会被隔离运行吗？

当前插件运行在宿主进程内（in-process 模式），未来将支持进程隔离模式：

- **当前**: 进程内插件，共享内存空间
- **未来**: 进程隔离插件，独立进程运行（计划中）

## 🎯 下一步

准备开始了吗？

- [环境准备](01-快速开始/01-环境准备.md) - 配置开发环境
- [创建第一个插件](01-快速开始/02-创建第一个插件.md) - 动手实践
- [插件生命周期](02-核心概念/01-插件生命周期.md) - 理解原理
