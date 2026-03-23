# 规格文档说明 / Specs

## 中文

### 目的

`.trae/specs/` 用来存放“一个需求从意图到落地”的协作文档，而不是长期产品说明。它适合记录功能变更、交互改造、重要修复和跨模块调整。

### 目录结构

每个功能目录建议使用：

```text
.trae/specs/<feature-name>/
  spec.md
  tasks.md
  checklist.md
```

### 每个文件的职责

#### `spec.md`

用于描述这次变更的意图和行为要求，建议包含：

- `Why`：为什么要做
- `What Changes`：会改什么
- `Impact`：影响哪些规范或代码区域
- Requirements / Scenarios：可验证的行为要求

#### `tasks.md`

用于把实现拆成可执行任务，建议包含：

- 分阶段任务或模块任务
- 依赖关系
- 可并行项
- 完成状态

#### `checklist.md`

用于验收与回归检查，建议包含：

- 关键 UI 或行为检查点
- 构建、运行、测试检查点
- 手工验证项

### 什么时候新建 spec

- 新增功能
- 已有功能行为发生变化
- 设置页、主界面、组件系统出现结构性调整
- 插件系统、共享契约、SDK 接入方式发生变化

### 什么时候只更新现有 spec

- 同一 feature 的后续迭代仍属于原目标范围
- 原 spec 仍是当前实现的权威描述
- 只是补充场景、任务拆解或验收项

### 什么时候可以不写 spec

- 纯拼写修复
- 纯内部重构且不改变行为
- 只改注释、日志、文档索引等非行为项

### 与其他文档的关系

- 长期产品说明看 `docs/PRODUCT.md`
- 长期架构说明看 `docs/ARCHITECTURE.md`
- 开发运行方式看 `docs/DEVELOPMENT.md`
- feature 级变更过程看 `.trae/specs/`

## English

Use `.trae/specs/` for feature-level change tracking, not for long-lived product or architecture documentation. `spec.md` defines intent and requirements, `tasks.md` breaks implementation into actionable work, and `checklist.md` captures validation and regression checks.
