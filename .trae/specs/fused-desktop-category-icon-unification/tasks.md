# Tasks

- [x] Task 1: 创建共享分类图标映射工具
  - [x] SubTask 1.1: 在 `LanMountainDesktop.ComponentSystem` 命名空间下创建 `ComponentCategoryIconResolver` 静态类
  - [x] SubTask 1.2: 实现 `ResolveCategoryIcon(string categoryId, IEnumerable<DesktopComponentDefinition> categoryComponents)` 方法，基于 IconKey 解析为 `FluentIcons.Common.Icon`
  - [x] SubTask 1.3: 添加单元测试验证图标解析逻辑（TDD：先写失败测试，再实现）

- [x] Task 2: 修改 ViewModel 的 Icon 属性类型
  - [x] SubTask 2.1: 将 `ComponentLibraryCategoryViewModel.Icon` 属性类型从 `Symbol` 改为 `Icon`
  - [x] SubTask 2.2: 更新构造函数参数类型

- [x] Task 3: 更新 FusedDesktopComponentLibraryControl.axaml.cs
  - [x] SubTask 3.1: 移除 `ResolveCategoryIcon` 硬编码方法
  - [x] SubTask 3.2: 在 `LoadCategories` 中使用 `ComponentCategoryIconResolver.ResolveCategoryIcon`
  - [x] SubTask 3.3: 更新 "all" 分类图标从 `Symbol.Apps` 改为 `Icon.Apps`

- [x] Task 4: 更新 ComponentLibraryWindow.axaml.cs
  - [x] SubTask 4.1: 移除 `ResolveCategoryIcon` 硬编码方法
  - [x] SubTask 4.2: 使用 `ComponentCategoryIconResolver.ResolveCategoryIcon`

- [x] Task 5: 更新 MainWindow.ComponentSystem.cs
  - [x] SubTask 5.1: 移除 `ResolveComponentLibraryCategoryIcon` 硬编码方法
  - [x] SubTask 5.2: 使用 `ComponentCategoryIconResolver.ResolveCategoryIcon`
  - [x] SubTask 5.3: 更新 `ComponentLibraryCategory` 记录的 `Icon` 字段类型从 `Symbol` 改为 `Icon`
  - [x] SubTask 5.4: 更新 `GetComponentLibraryCategories` 方法中的图标解析调用

- [x] Task 6: 更新 XAML 绑定
  - [x] SubTask 6.1: 验证 `FusedDesktopComponentLibraryControl.axaml` 中 `fi:FluentIcon Icon="{Binding Icon}"` 绑定在新类型下正常工作

- [x] Task 7: 构建验证
  - [x] SubTask 7.1: 运行 `dotnet build` 确保无编译错误
  - [x] SubTask 7.2: 运行 `dotnet test` 确保所有测试通过

# Task Dependencies
- Task 2 依赖于 Task 1（共享映射工具）
- Task 3、4、5 依赖于 Task 1 和 Task 2
- Task 6 依赖于 Task 2（类型变更后验证绑定）
- Task 7 依赖于所有前置任务
