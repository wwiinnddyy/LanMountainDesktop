# Tasks

- [x] Task 1: 修改 FusedDesktopComponentLibraryWindow.axaml 窗口布局
  - [x] SubTask 1.1: 重新设计标题栏，使用标准X关闭按钮，移除圆形样式，使用 DesignCornerRadiusSm
  - [x] SubTask 1.2: 调整窗口整体布局为左侧面板+右侧预览区
  - [x] SubTask 1.3: 添加底部"查找更多组件"链接区域

- [x] Task 2: 修改 FusedDesktopComponentLibraryControl.axaml 控件布局
  - [x] SubTask 2.1: 重新设计左侧面板：仅保留分类列表（移除搜索框）
  - [x] SubTask 2.2: 重新设计右侧预览区：组件标题 + 大尺寸预览 + 描述 + 添加按钮
  - [x] SubTask 2.3: 优化分类列表项样式，添加选中状态视觉反馈
  - [x] SubTask 2.4: 复用阑山桌面组件库的分类图标映射

- [x] Task 3: 更新 ViewModel 支持新交互模式
  - [x] SubTask 3.1: 在 ComponentLibraryWindowViewModel 中添加 SelectedComponent 属性
  - [x] SubTask 3.2: 添加组件描述属性支持

- [x] Task 4: 更新 FusedDesktopComponentLibraryControl.axaml.cs 代码逻辑
  - [x] SubTask 4.1: 修改分类选择逻辑，选中分类时显示该分类第一个组件
  - [x] SubTask 4.2: 添加组件选中逻辑
  - [x] SubTask 4.3: 移除搜索相关代码
  - [x] SubTask 4.4: 复用阑山桌面组件库的分类图标和本地化方法
  - [x] SubTask 4.5: 添加"查找更多组件"链接点击处理（打开设置窗口插件目录）

- [x] Task 5: 验证和测试
  - [x] SubTask 5.1: 验证关闭按钮使用动态圆角资源 DesignCornerRadiusSm
  - [x] SubTask 5.2: 验证窗口布局符合Windows 11小组件面板风格
  - [x] SubTask 5.3: 验证分类图标与阑山桌面组件库一致
  - [x] SubTask 5.4: 验证组件添加功能正常工作
  - [x] SubTask 5.5: 验证"查找更多组件"链接能打开设置窗口

# Task Dependencies
- Task 3 依赖于 Task 1 和 Task 2 的UI设计确定
- Task 4 依赖于 Task 3 的ViewModel更新
- Task 5 依赖于所有前置任务完成
