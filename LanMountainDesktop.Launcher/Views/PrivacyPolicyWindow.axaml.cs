using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LanMountainDesktop.Launcher.Views;

public partial class PrivacyPolicyWindow : Window
{
    private readonly PrivacyPolicyViewModel _viewModel;

    public PrivacyPolicyWindow()
    {
        InitializeComponent();
        _viewModel = new PrivacyPolicyViewModel();
        DataContext = _viewModel;

        // 加载隐私政策内容
        LoadPrivacyPolicy();

        // 绑定关闭按钮事件
        if (this.FindControl<Button>("CloseButton") is { } closeButton)
        {
            closeButton.Click += OnCloseClick;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadPrivacyPolicy()
    {
        // 默认隐私政策内容（Markdown 格式）
        _viewModel.PrivacyPolicyMarkdown = @"# 阑山桌面遥测隐私数据收集协议

## 1. 概述

欢迎使用阑山桌面！本协议旨在向您说明我们在应用运行过程中收集哪些数据、如何使用这些数据以及如何保护您的隐私。

## 2. 我们收集的数据

### 2.1 崩溃报告（可选）

当应用发生崩溃时，我们可能会收集以下信息：

- **崩溃类型**：应用程序崩溃、无响应等异常情况的类型
- **错误堆栈**：导致崩溃的代码路径（不包含文件内容或个人数据）
- **设备信息**：操作系统版本、应用版本、.NET 运行时版本
- **匿名设备标识符**：一个随机生成的唯一标识符，用于统计崩溃频率

**注意**：崩溃报告不包含您的个人文件、桌面组件内容、浏览历史或任何可识别个人身份的信息。

### 2.2 使用统计（可选）

如果您启用了使用统计，我们可能会收集：

- **功能使用频率**：各功能模块的使用次数（如设置打开次数、组件添加次数）
- **性能指标**：应用启动时间、内存占用范围等性能数据
- **匿名设备标识符**：用于统计独立用户数量

**注意**：使用统计不包含您的组件配置、个人设置或任何敏感信息。

## 3. 我们不收集的数据

我们明确**不会**收集以下信息：

- ❌ 您的姓名、邮箱、电话号码等个人身份信息
- ❌ 您的桌面截图或壁纸内容
- ❌ 您添加的组件的具体内容或配置详情
- ❌ 您的文件系统浏览记录
- ❌ 您的网络活动或浏览历史
- ❌ 您的精确地理位置信息

## 4. 数据用途

我们收集的数据仅用于以下目的：

1. **改进应用稳定性**：通过分析崩溃报告，修复程序缺陷
2. **优化产品体验**：了解功能使用情况，优先改进常用功能
3. **性能优化**：识别性能瓶颈，提升应用运行效率

## 5. 数据存储与保护

- 所有数据通过**加密传输**（HTTPS）发送到我们的服务器
- 数据存储在安全的服务器环境中，访问受到严格控制
- 匿名设备标识符仅用于统计目的，无法关联到您的真实身份
- 我们**不会**将数据出售或共享给任何第三方用于商业目的

## 6. 您的控制权

您拥有以下权利：

- **随时开启或关闭**：您可以在 OOBE 向导或设置中随时更改遥测选项
- **数据删除**：如果您希望删除已收集的数据，请联系我们的支持团队
- **知情权**：您有权了解我们收集了哪些数据（通过本协议）

## 7. 协议更新

我们可能会不时更新本协议。重大变更时，我们会在应用内通知您。继续使用本应用即表示您同意修订后的协议。

## 8. 联系我们

如果您对本协议有任何疑问，请通过以下方式联系我们：

- 项目主页：https://github.com/LanMountain/LanMountainDesktop
- 问题反馈：在 GitHub 仓库提交 Issue

---

**最后更新日期**：2026年4月26日

感谢您信任并使用阑山桌面！";
    }
}

public partial class PrivacyPolicyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _privacyPolicyMarkdown = string.Empty;
}
