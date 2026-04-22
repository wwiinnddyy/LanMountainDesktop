# Tasks

- [x] Task 1: 简化设置窗口打开契约
  - [x] 将 `SettingsWindowOpenRequest` 从 owner / anchor 语义改为目标页 + 参考屏幕语义
  - [x] 移除 `ISettingsWindowService.Toggle`

- [x] Task 2: 重做设置窗口服务行为
  - [x] 设置窗口始终使用 `Show()` 打开
  - [x] 设置窗口始终 `ShowInTaskbar = true`
  - [x] 已打开时只聚焦并在需要时切页
  - [x] 关闭后销毁实例，下次打开重新创建并居中

- [x] Task 3: 统一设置入口并解耦桌面壳
  - [x] 桌面底栏设置按钮改为 open-or-focus
  - [x] 组件库入口改为复用 `OpenIndependentSettingsModule`
  - [x] 移除 `MainWindow` 上的设置窗口锚点逻辑

- [x] Task 4: 明确产品边界
  - [x] 调整“在任务栏显示图标”文案，限定为桌面主窗口
  - [x] 新增独立设置窗口 feature spec
  - [x] 在窗口过渡动画 spec 中补充“设置窗口不参与动画”

- [x] Task 5: 验证
  - [x] 运行 `dotnet build LanMountainDesktop.slnx -c Debug`
  - [x] 运行与新 helper 相关的测试
