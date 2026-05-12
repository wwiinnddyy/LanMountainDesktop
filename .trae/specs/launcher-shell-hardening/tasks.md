# 任务拆解

- [x] 为 Launcher/宿主共享新增重启来源、父进程和展示模式参数。
- [x] 修复 Launcher 对 `SecondaryActivationSucceeded` 的重复 fallback 拉起。
- [x] 让 Launcher 成功判定支持 `TrayReady` 与 `BackgroundReady`。
- [x] 应用重启默认优先回到 Launcher，而不是直接回拉宿主 exe。
- [x] 抽出独立托盘服务，集中处理创建、刷新、watchdog 与状态流转。
- [x] 在进入 `TrayOnly` 前增加托盘就绪校验与回退策略。
- [x] 为运行中托盘丢失增加 watchdog 和自动恢复逻辑。
- [x] 统一公共 IPC、设置页与 Launcher 的版本读取入口。
- [x] 将仓库默认版本改为开发占位值，并在 Release 工作流中加入显式打版本步骤。
- [x] 修复主窗口入场、通知定位和 Launcher OOBE 的高分屏动画/定位问题。
- [x] 补充规格与版本同步说明文档。
- [ ] 追加针对托盘恢复和启动判定的自动化回归测试。

- [x] Remove the legacy `LanMountainDesktop_Launcher` startup progress pipe; launcher progress now uses public IPC plus host exit-code classification only.
- [x] Move normal multi-open probing into Launcher before host launch and remove Host-side single-instance prompt/listener code.
- [x] Refresh the Launcher error window with Fluent resources, InfoBar, Fluent icons, command bar actions, and copyable diagnostic details.
- [x] Add app-level `MultiInstanceLaunchBehavior` setting and expose it in General > Basic Settings.
- [x] Make Launcher apply restart/open silently/prompt only/notify and open behavior before starting a new Host.
- [x] Add a Fluent Launcher multi-instance prompt; Host public IPC stays limited to activation/status/restart/exit actions.
