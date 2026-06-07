# Air APP 开发完整指南

欢迎来到阑山桌面 Air APP 开发指南！Air APP 是运行在阑山桌面环境中的独立窗口应用。

## 什么是 Air APP？

**Air APP** 是阑山桌面生态中的独立应用形态，与桌面组件（Widget）不同：

### 对比：Air APP vs 桌面组件

| 特性 | Air APP | 桌面组件 |
|------|---------|---------|
| **窗口形式** | 独立窗口，可移动、缩放 | 固定在桌面上 |
| **生命周期** | 独立进程，按需启动 | 随宿主启动 |
| **UI 复杂度** | 适合复杂界面 | 适合简单信息展示 |
| **资源占用** | 按需运行，不用时退出 | 始终运行 |
| **典型案例** | 白板、世界时钟、计算器 | 天气组件、时钟组件 |

### Air APP 架构

```
┌──────────────────────────────────────┐
│   LanMountainDesktop (桌面宿主)       │
│                                      │
│  ┌────────────────────────────────┐ │
│  │ LanMountainDesktop.AirAppRuntime│ │
│  │    (Air APP 运行时容器)          │ │
│  │                                  │ │
│  │  管理所有 Air APP 进程           │ │
│  │  - 启动/停止                     │ │
│  │  - 实例去重                      │ │
│  │  - 生命周期跟踪                  │ │
│  └─────────┬────────────────────────┘ │
└────────────┼───────────────────────────┘
             │ IPC 通信
             │
    ┌────────▼──────────┐
    │  Air APP Process  │
    │                   │
    │  ┌─────────────┐  │
    │  │ AirAppHost  │  │
    │  │  (渲染容器) │  │
    │  └─────────────┘  │
    │                   │
    │  你的 Air APP     │
    │  - UI            │
    │  - 业务逻辑      │
    │  - 数据管理      │
    └──────────────────┘
```

## 📚 学习路径

### 快速上手

1. **[Air APP 介绍](01-Air-APP介绍.md)** - 理解 Air APP 是什么
2. **[创建第一个 Air APP](02-创建第一个AirApp.md)** - Hello World
3. **[架构与生命周期](03-架构与生命周期.md)** - 理解运行机制

### 深入学习

4. **[IPC 通信](04-IPC通信.md)** - 与宿主和其他 APP 通信
5. **[窗口管理](05-窗口管理.md)** - 窗口模式、大小、位置
6. **[数据持久化](06-数据持久化.md)** - 保存应用数据
7. **[主题适配](07-主题适配.md)** - 适配亮色/暗色模式

### 实战案例

8. **[世界时钟 APP](08-实战-世界时钟.md)** - 完整示例
9. **[白板 APP](09-实战-白板.md)** - 全屏交互应用
10. **[打包与发布](10-打包与发布.md)** - 发布到市场

## 🎯 快速开始

### 创建 Air APP 项目

```powershell
# 安装模板
dotnet new install LanMountainDesktop.AirAppTemplate

# 创建项目
dotnet new lmd-airapp -n MyAirApp

# 构建
cd MyAirApp
dotnet build
```

### 项目结构

```
MyAirApp/
├── MyAirApp.csproj                 # 项目文件
├── Program.cs                      # 程序入口
├── App.axaml                       # 应用定义
├── App.axaml.cs                    # 应用代码
├── Views/                          # 视图目录
│   └── MainWindow.axaml            # 主窗口
├── ViewModels/                     # 视图模型
│   └── MainWindowViewModel.cs
├── Models/                         # 数据模型
├── Services/                       # 业务服务
├── Assets/                         # 资源文件
│   └── icon.png
└── airapp.json                     # Air APP 清单
```

### Air APP 清单 (airapp.json)

```json
{
  "Id": "com.example.myairapp",
  "Name": "My Air APP",
  "Version": "1.0.0",
  "Author": "Your Name",
  "Description": "My first Air APP",
  "MinHostVersion": "1.0.0",
  "Icon": "Assets/icon.png",
  "WindowMode": "Standard",
  "DefaultSize": {
    "Width": 800,
    "Height": 600
  },
  "AllowMultipleInstances": false
}
```

### 窗口模式

| 模式 | 说明 | 适用场景 |
|------|------|---------|
| `Standard` | 标准窗口，带标题栏和边框 | 大多数应用 |
| `Borderless` | 无边框窗口，自定义标题栏 | 自定义 UI |
| `FullScreen` | 全屏窗口 | 白板、游戏 |
| `Tool` | 工具窗口，始终置顶 | 小工具 |

## 核心概念

### 生命周期

```
用户点击启动
    ↓
AirAppRuntime 检查是否已运行
    ↓
否 → 启动新进程
是 → 激活现有窗口（如果 AllowMultipleInstances=false）
    ↓
AirAppHost 初始化
    ↓
加载 Air APP 代码
    ↓
显示主窗口
    ↓
应用运行中...
    ↓
用户关闭窗口
    ↓
AirAppHost 清理资源
    ↓
进程退出
    ↓
AirAppRuntime 清理注册
```

### IPC 通信

Air APP 可以通过 IPC 与桌面宿主通信：

```csharp
// 获取宿主设置
var theme = await ipcClient.InvokeAsync<string>(
    "LanMountainDesktop.Host.v1",
    "GetCurrentTheme"
);

// 订阅宿主事件
ipcClient.OnNotify("lanmountain.theme.changed", (themeData) =>
{
    // 主题变更，更新 UI
    ApplyTheme(themeData);
});
```

## 📖 章节目录

### [01-Air-APP介绍.md](01-Air-APP介绍.md)
什么是 Air APP，与桌面组件的区别，应用场景

### [02-创建第一个AirApp.md](02-创建第一个AirApp.md)
从零创建一个简单的 Air APP，运行和调试

### [03-架构与生命周期.md](03-架构与生命周期.md)
Air APP 架构、运行时、生命周期管理

### [04-IPC通信.md](04-IPC通信.md)
与桌面宿主通信、调用服务、订阅事件

### [05-窗口管理.md](05-窗口管理.md)
窗口模式、大小调整、位置记忆

### [06-数据持久化.md](06-数据持久化.md)
保存应用状态和用户数据

### [07-主题适配.md](07-主题适配.md)
适配亮色/暗色主题、圆角系统

### [08-实战-世界时钟.md](08-实战-世界时钟.md)
完整案例：世界时钟应用

### [09-实战-白板.md](09-实战-白板.md)
完整案例：全屏白板应用

### [10-打包与发布.md](10-打包与发布.md)
打包、签名、发布到市场

## 💡 最佳实践

### 性能优化

- ✅ 使用虚拟化列表处理大量数据
- ✅ 图片和资源延迟加载
- ✅ 避免复杂的布局嵌套
- ✅ 使用 `RenderTransform` 而非 `Margin` 做动画
- ✅ 及时取消不需要的异步操作

### 用户体验

- ✅ 记住窗口位置和大小
- ✅ 提供键盘快捷键
- ✅ 优雅处理错误和异常
- ✅ 适配不同屏幕分辨率和 DPI
- ✅ 响应主题变更

### 安全性

- ✅ 验证用户输入
- ✅ 使用 HTTPS 进行网络请求
- ✅ 敏感数据加密存储
- ✅ 避免路径遍历漏洞
- ✅ 遵循最小权限原则

## 🔗 相关资源

- [插件开发指南](../01-插件开发/) - 如果需要桌面组件
- [整体架构](../04-架构与实现/01-整体架构.md) - 系统架构
- [设计规范](../03-组件设计规范/) - UI 设计指南

## 🎯 下一步

- [Air APP 介绍](01-Air-APP介绍.md) - 了解 Air APP
- [创建第一个 Air APP](02-创建第一个AirApp.md) - 动手实践
- [架构与生命周期](03-架构与生命周期.md) - 理解原理
