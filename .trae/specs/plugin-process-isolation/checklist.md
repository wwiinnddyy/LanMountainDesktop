# Checklist

- [x] `plugin.json` 缺省时仍默认为 `in-proc`
- [x] 非法 `runtime.mode` 会给出清晰错误
- [x] SDK 中已有 Worker 入口和隔离运行模式的公共接口
- [x] IPC 契约已拆到独立工程，且不引用 Avalonia
- [x] IPC 封装层已集中环境变量、启动参数和通知路由常量
- [x] 架构文档已写明一期 `isolated-background`、二期 `isolated-window`
- [x] 架构文档已写明 `IPluginExportRegistry` / `IPluginMessageBus` 不再作为隔离插件主边界
- [x] 文档已写明 ClassIsland 的借鉴点与取舍
- [ ] Host 在 Worker 崩溃时仅降级插件且不中断主程序
- [ ] `isolated-background` 的组件、编辑器、设置页完成真实 IPC 回路
