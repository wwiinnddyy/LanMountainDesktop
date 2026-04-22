# Tasks

- [x] 梳理现有插件运行时、组件注册、设置页和共享对象边界
- [x] 形成插件进程隔离架构文档
- [x] 在 `.trae/specs/plugin-process-isolation/` 下补齐 spec、tasks、checklist
- [x] 在 `PluginSdk` 中增加 `runtime.mode`、Worker 入口接口和运行模式枚举
- [x] 新建 `LanMountainDesktop.PluginIsolation.Contracts`，沉淀纯 DTO、路由常量、错误码与 JSON context
- [x] 新建 `LanMountainDesktop.PluginIsolation.Ipc`，沉淀 ClassIsland 风格的 IPC 包装外壳
- [x] 更新插件模板 `plugin.json`，让新插件默认显式声明 `in-proc`
- [ ] 在 Host 侧接入真实 Worker 进程拉起与 dotnetCampus.Ipc 传输绑定
- [ ] 为 `isolated-background` 构建 Host UI 壳层适配器
- [ ] 为故障、心跳、降级与恢复补齐端到端测试
