# Tasks

## 1. 课表单双周解析修复

- [x] Task 1.1: 分析 ClassIsland 课表单双周数据结构
  - [x] 分析 ClassIsland Schedule.json 和 Profile.json 中的周数规则字段
  - [x] 确认 WeekCountDiv 和 WeekCountDivTotal 的含义和取值范围

- [x] Task 1.2: 修复 GetCyclePositionsByDate 方法
  - [x] 检查单周开始日期的计算逻辑
  - [x] 修复周期位置计算公式

- [x] Task 1.3: 修复 CheckRegularClassPlan 方法
  - [x] 验证 weekCountDiv 和 weekCountDivTotal 的匹配逻辑
  - [x] 确保单周=1、双周=2、每周=0 的正确处理

## 2. 课程动态移动功能

- [x] Task 2.1: 分析当前课程状态检测逻辑
  - [x] 查看如何判断课程是否为"当前进行中"

- [x] Task 2.2: 实现定时刷新机制
  - [x] 增加更频繁的刷新定时器（每分钟检查一次）
  - [x] 实现课程状态变化检测

- [x] Task 2.3: 实现动态移动逻辑
  - [x] 课程结束后自动上移
  - [x] 新课程自动移入视图

- [x] Task 2.4: 实现次日课程切换
  - [x] 当日所有课程结束后自动切换到次日

## 3. 拖动交互功能

- [x] Task 3.1: 实现 ScrollViewer 包裹
  - [x] 修改 XAML 使用 ScrollViewer 包裹课程列表

- [x] Task 3.2: 实现拖动手势处理
  - [x] 添加 PointerPressed/PointerMoved/PointerReleased 处理
  - [x] 实现平滑滚动逻辑

## 4. 自动复位功能

- [x] Task 4.1: 记录用户拖动状态
  - [x] 添加用户是否手动拖动的标志位

- [x] Task 4.2: 实现自动复位逻辑
  - [x] 检测当前课程变化
  - [x] 当用户手动拖动且当前课程变化时自动复位

# Task Dependencies

- Task 1.1 -> Task 1.2 -> Task 1.3
- Task 2.1 -> Task 2.2 -> Task 2.3 -> Task 2.4
- Task 3.1 -> Task 3.2
- Task 4.1 -> Task 4.2

# Parallelizable Tasks

- Task 1.x (解析修复) 与 Task 3.x (拖动) 可以并行开发
- Task 2.x (动态移动) 可以在 Task 1 完成后进行
