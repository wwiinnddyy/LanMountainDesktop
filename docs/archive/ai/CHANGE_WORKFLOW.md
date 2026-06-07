# Change Workflow

## 目标

给 AI 一个稳定的执行顺序，避免直接跳到编码而漏掉规格、文档和回归验证。

## 推荐流程

1. 读取 `AGENTS.md`
2. 读取 `docs/ai/DOC_SOURCES.md`，确认这次需求涉及哪些权威文档
3. 按需读取 `docs/ARCHITECTURE.md`、专题规范和相关目录内 README
4. 检查 `.trae/specs/` 是否已有对应 feature
5. 如果是新功能或行为变化，先补或更新 `spec.md / tasks.md / checklist.md`
6. 再改代码
7. 补测试或复用已有测试文件
8. 运行最小必要验证
9. 回写文档入口和迁移说明

## 什么时候必须先更新 `.trae/specs/`

- 用户可见行为变化
- 设置页或主界面结构变化
- 组件系统规则变化
- 插件宿主集成、共享契约、SDK 使用模式变化

## 什么时候可以直接改代码

- 纯文档修复
- 不改变行为的内部重构
- 小范围 bugfix 且现有 spec 已完整覆盖该功能意图

## 最小验证清单

默认优先：

```bash
dotnet build LanMountainDesktop.slnx -c Debug
dotnet test LanMountainDesktop.slnx -c Debug
```

按需增加：

- 运行桌面宿主验证 UI 或启动行为
- 检查插件打包或 market 调试路径
- 手工验证设置页、主题切换、组件布局等高风险交互

## 回写要求

出现以下变化时，AI 应同步回写文档：

- 命令变化：更新 `docs/DEVELOPMENT.md`
- 模块职责变化：更新 `docs/ARCHITECTURE.md`
- 产品定位或阶段变化：更新 `docs/PRODUCT.md`
- AI 入口或权威来源变化：更新 `AGENTS.md` 或 `docs/ai/DOC_SOURCES.md`

## 不要做的事

- 不要把根目录 `README.md` 写成 feature 详细设计文档
- 不要在多份文档里重复维护同一条事实
- 不要把 `LanAirApp` 的资料误写成本仓库权威来源
