# Air APP 现状与开发预览

> [!IMPORTANT]
> 当前生产版本只支持编译内置的 `world-clock`、`whiteboard` 和 `rss-reader` 窗口链路。`LanMountainDesktop.AirAppSdk`、`LanMountainDesktop.AirAppTemplate` 与 `LanMountainDesktop.AirAppDevServer` 是尚未接入生产 Host/Runtime 的原型，不能据此宣称第三方 Air APP 会被桌面自动发现、安装或加载。

## 当前已上线的内置链路

桌面轻应用入口本身是主 `LanMountainDesktop` Host 内的桌面组件，不在 Launcher、AirAppRuntime 或 AirAppSdk 中运行：

```
主 Host 内置桌面组件
  世界时钟 / 白板 / RSS 阅读器
         ↓ 点击
Host 内 AirAppLauncherService
         ↓ AirAppOpenRequest（IPC）
独立 LanMountainDesktop.AirAppRuntime
         ↓ 启动或激活
独立 LanMountainDesktop.AirAppHost 进程
         ↓
按 appId 渲染编译内置的窗口视图
```

各进程职责如下：

| 进程/模块 | 当前生产职责 |
|----------|--------------|
| `LanMountainDesktop` Host | 承载桌面入口组件；点击后构造请求并调用 Runtime IPC |
| `LanMountainDesktop.Launcher` | OOBE、Splash、版本选择、预启动 Runtime、启动 Host；执行有界的 `AttachHost(hostPid)` 交接后退出 |
| `LanMountainDesktop.AirAppRuntime` | 提供生命周期与控制 IPC、实例去重、启动/激活/关闭 AirAppHost |
| `LanMountainDesktop.AirAppHost` | 独立进程渲染一个内置 Air APP 窗口，并向 Runtime 注册/注销 |

Launcher 不承载轻应用 UI，也不需要跟随 Host 常驻。正常启动中，它尝试把存活的 Host PID 交给 Runtime，成功时由 Host 接管 Runtime 生命周期；交接失败则记录诊断并依靠 Host 的按需启动兜底。两种情况下 Launcher 都不会等待 Host 退出。稳定运行期是 `Host ↔ AirAppRuntime ↔ AirAppHost`，不存在必须保留的透明 Launcher 窗口。

### 当前内置应用与实例规则

| `appId` | 入口位置 | AirAppHost 内容 | 实例规则 |
|---------|----------|----------------|----------|
| `world-clock` | Host 内的世界时钟/模拟时钟等组件 | 编译内置的时钟视图 | 全局共用 `world-clock:clock-suite:global` |
| `whiteboard` | Host 内的白板组件 | 编译内置的白板组件视图 | 按组件 ID 与放置 ID 区分 |
| `rss-reader` | Host 内的 RSS 阅读器组件 | 编译内置的 RSS 视图 | 全局共用 `rss-reader:global` |

Host 会把 `sourceComponentId`、`sourcePlacementId`（以及 RSS 的目标条目）经 Runtime 透传给 AirAppHost。若点击时 Runtime 管道不可用，Host 会直接启动 AirAppRuntime 并重试；这条兜底不依赖 Launcher 常驻。

## 第三方 AirAppSdk：Preview，尚未接入生产

仓库中存在一组第三方 Air APP 开发原型，但它们不是上述生产链路的一部分：

| 原型项目 | 已有内容 | 当前缺口 |
|----------|----------|----------|
| `LanMountainDesktop.AirAppSdk` | API、清单、窗口与组件抽象 | 生产 Host/Runtime/AirAppHost/Launcher 没有项目引用，也没有 SDK 程序集加载器 |
| `LanMountainDesktop.AirAppTemplate` | 基于 AirAppSdk 的模板草案 | 模板输出不会被生产桌面自动发现或加载 |
| `LanMountainDesktop.AirAppDevServer` | 文件监视、构建与打包原型 | 预览宿主仍是 TODO；没有连接生产 Runtime/Host 的调试加载协议 |

当前 `LanMountainDesktop.slnx` 不包含这三个原型项目；生产 AirAppHost 直接判断三个内置 `appId` 并创建内置视图，没有扫描 `airapp.json`、加载第三方程序集、寻找 `[AirAppEntrance]` 或调用 AirAppSdk 生命周期。

### `.laapp` 包格式冲突

不要把 AirAppDevServer 生成的 `.laapp` 复制到生产插件目录，也不要交给 Launcher 的插件安装命令：

- 生产代码当前把 `.laapp` 作为插件包扩展名，并要求 ZIP 中存在 `plugin.json`。
- AirAppDevServer 原型把构建输出打成同扩展名，并由 AirAppSdk/模板使用 `airapp.json` 语义。
- 两条路径尚未统一；只有 `airapp.json` 的原型包会被现有插件安装/发现链路拒绝，也不会被 AirAppRuntime 加载。

在确定独立扩展名或兼容的清单/安装路由，并实现生产加载器以前，`.laapp + airapp.json` 只能视为设计原型，不能视为可发布格式。

## 原型文档的使用方式

当前目录只保留这份状态说明。第三方 API、模板和工具的设计草案位于仓库中的 `LanMountainDesktop.AirAppSdk`、`LanMountainDesktop.AirAppTemplate` 与 `LanMountainDesktop.AirAppDevServer` 项目；其中关于模板安装、`airapp.json` 自动发现、第三方代码加载、预览、热重载、市场安装、窗口模式或 IPC 的内容都必须按 Preview 理解。若与生产源码冲突，以当前 Host → Runtime → AirAppHost 的内置链路为准。

可以单独构建这些项目来研究 API 或验证原型代码，但这不会让输出自动进入生产桌面：

```powershell
dotnet build LanMountainDesktop.AirAppSdk/LanMountainDesktop.AirAppSdk.csproj
dotnet build LanMountainDesktop.AirAppTemplate/LanMountainDesktop.AirAppTemplate.csproj
dotnet build LanMountainDesktop.AirAppDevServer/LanMountainDesktop.AirAppDevServer.csproj
```

## 第三方能力转为生产支持的最低条件

在文档重新标记为“已支持”前，至少需要同时完成：

- 定义不与插件 `plugin.json` 路由冲突的包格式、安装位置和发现规则。
- 在生产 Host/Runtime/AirAppHost 中实现并引用受支持的 SDK 契约与程序集加载路径。
- 实现 DevServer 到真实预览/运行宿主的协议，而不是只监视并重新构建文件。
- 增加第三方包安装、清单校验、加载、窗口打开、卸载和版本兼容的端到端测试。

这些工作不属于当前内置 Air APP Runtime 容器与 Launcher 生命周期修复。

## 相关资源

- [整体架构](../04-架构与实现/01-整体架构.md) - 当前生产进程职责与启动/IPC 链路
- [插件开发指南](../01-插件开发/) - 当前已有生产加载路径的扩展方式
- [设计规范](../03-组件设计规范/) - UI 与桌面组件设计约束
