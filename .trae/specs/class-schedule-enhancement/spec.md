# 课程表组件功能优化规格说明书

## Why

当前课程表组件存在以下问题：
1. 单双周课程解析逻辑存在缺陷，无法正确识别单周/双周/每周模式
2. 课程无法动态移动，第一列始终显示进行中的课程，但存在无法正常移动的问题
3. 缺少用户拖动交互功能
4. 缺少拖动后的自动复位机制

## What Changes

- 修复 ClassIsland 课程单双周解析逻辑
- 实现课程动态移动机制（当前课程结束自动上移）
- 实现课程表上下拖动交互功能
- 实现自动复位功能（课程结束后视图复位到最新进行中课程）

## Impact

### Affected specs
- 课程表组件功能规范

### Affected code
- `Services/ClassIslandScheduleDataService.cs` - 课表解析服务
- `Views/Components/ClassScheduleWidget.axaml.cs` - 课表组件

---

## ADDED Requirements

### Requirement: 单双周课程解析

系统 SHALL 能够正确解析包含单双周信息的课程数据。

#### Scenario: 单周课程
- **WHEN** 课程设置为单周上课
- **THEN** 课程仅在单周显示

#### Scenario: 双周课程
- **WHEN** 课程设置为双周上课
- **THEN** 课程仅在双周显示

#### Scenario: 每周课程
- **WHEN** 课程设置为每周上课
- **THEN** 课程在所有周显示

---

### Requirement: 课程动态移动

系统 SHALL 实现课程的动态移动机制。

#### Scenario: 课程结束自动上移
- **WHEN** 当前进行中的课程结束
- **THEN** 课程列表自动向上移动
- **AND THEN** 下一个进行中或即将开始的课程移至视图可见区域

#### Scenario: 新课程移入视图
- **WHEN** 新的课程即将开始
- **THEN** 该课程自动移至视图可见区域

#### Scenario: 当日课程全部结束
- **WHEN** 当日所有课程已结束
- **THEN** 自动显示次日课程表

---

### Requirement: 拖动交互功能

系统 SHALL 提供课程表的上下拖动功能。

#### Scenario: 拖动查看课程
- **WHEN** 用户在课程表区域进行上下拖动
- **THEN** 课程列表随拖动方向滚动
- **AND THEN** 拖动操作流畅、响应及时

---

### Requirement: 自动复位功能

系统 SHALL 在用户手动拖动后自动复位到当前课程。

#### Scenario: 当前课程结束触发复位
- **WHEN** 用户手动拖动课程表后，当前课程结束
- **THEN** 视图自动复位到显示最新进行中课程的位置

---

## MODIFIED Requirements

### Requirement: 课程解析逻辑

**当前**: 单双周解析可能存在缺陷

**修改后**: 正确识别 WeekCountDiv 和 WeekCountDivTotal 参数，准确判断单周/双周/每周模式

---

## REMOVED Requirements

（无）
