# Launcher 外壳托管、托盘兜底与高分屏动画修复

## 背景

当前桌面应用在以下场景存在明显不稳定性：

- 设置页或升级后的“重启”没有统一回到 Launcher。
- 已有实例处于托盘时，再次启动容易误报“窗口未显示”，甚至重复拉起。
- 托盘初始化失败或运行中丢失时，应用可能进入无恢复入口状态。
- Launcher 和宿主的版本来源不一致，发布后容易出现 UI 版本错乱。
- 高分屏和混合缩放环境下，Launcher OOBE、主窗口入场和通知动画存在像素/DIP 混用问题。

## 目标

- Launcher 成为正式环境唯一的启动与重启入口。
- 进入 `TrayOnly` 前必须先确认托盘可恢复。
- Launcher UI 显示的版本号等于应用版本号。
- 发布工作流显式同步主程序、Launcher、manifest 和产物版本。
- 动画和定位统一按 DIP 与缩放计算。

## 行为要求

### 1. 重启接管

- 应用内重启、插件升级后的重启都必须优先回到 Launcher。
- Launcher 对 `SecondaryActivationSucceeded` 只认定为一次成功重定向，不允许再做 fallback 二次拉起。
- Launcher 启动成功判定区分三类场景：
  - 前台启动：`DesktopVisible` 或 `ActivationRedirected`
  - 重启到最小化：`BackgroundReady`
  - 重启到托盘：`TrayReady + BackgroundReady`

### 2. 托盘硬约束

- 托盘状态机必须至少覆盖：
  - `Unavailable`
  - `Initializing`
  - `Ready`
  - `Recovering`
  - `Failed`
- `HideMainWindowToTray`、关闭到托盘、重启恢复到托盘前都必须先执行托盘就绪检查。
- 如果托盘不可用：
  - 优先回退到任务栏最小化
  - 若任务栏入口也不可用，则强制恢复前台可见
- 托盘处于隐藏态期间必须运行 watchdog；连续恢复失败时自动恢复主窗口。

### 3. 版本来源

- Launcher 只能显示应用版本，不能显示 Launcher 自身硬编码版本。
- 版本解析优先顺序：
  - `version.json`
  - 主程序文件版本 / 信息版本
  - `app-<version>` 部署目录
- Release 工作流必须显式打版本补丁，避免仓库默认占位值被误当成正式版本。

### 4. 高分屏动画

- 主窗口、通知、Launcher OOBE 的动画位移必须使用 DIP 或基于缩放换算后的尺寸。
- 不允许直接把 `PixelRect` 宽高当作 `TranslateTransform` 或 `DesiredSize` 的输入。
- 淡入和位移动画应并行执行，避免先淡入后滑动造成观感异常。

## 验收

- 已在托盘中的实例再次通过 Launcher 启动时，只激活已有实例。
- 设置页重启和插件升级重启后，不再出现“窗口未显示但后台已有多个进程”。
- 托盘失败时应用仍保持可恢复。
- Launcher 与应用设置页显示相同版本。
- 100% / 150% / 200% / 250% 缩放下，Launcher OOBE、主窗口入场、通知位置与动画正常。
