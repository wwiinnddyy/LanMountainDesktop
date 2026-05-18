# 启动器 RESX 多语言适配实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 LanMountainDesktop.Launcher 引入 RESX 资源文件，实现启动器 UI 的多语言适配，消除所有硬编码中英文字符串。

**Architecture:** 在 Launcher 项目中创建 RESX 资源文件体系（默认 zh-CN + en-US/ja-JP/ko-KR），通过 .NET 内置 `ResourceManager` 机制实现本地化。启动时从主应用 `settings.json` 读取 `LanguageCode` 字段设置 `CultureInfo.CurrentUICulture`，AXAML 中使用 `x:Static` 引用资源，C# 代码中通过 `Strings.ResourceName` 强类型访问。

**Tech Stack:** .NET RESX 资源文件、Avalonia `x:Static` 标记扩展、`System.Globalization.CultureInfo`

---

## 现状分析

### 问题概述

1. **启动器完全没有本地化支持**：所有 UI 字符串硬编码，中英文混杂严重
2. **纯英文窗口**：SplashWindow、ErrorWindow、MultiInstancePromptWindow、DataLocationPromptWindow、LoadingDetailsWindow
3. **纯中文窗口**：OobeWindow、MigrationPromptWindow、UpdateWindow、ErrorDebugWindow、DevDebugWindow、PrivacyPolicyWindow
4. **启动器不读取主应用语言设置**：没有 `LanguageCode` 相关代码
5. **硬编码字符串总量约 180+ 条**，分布在 11 个 AXAML 视图和 11 个 C# code-behind 文件中

### 方案选择：RESX vs JSON

| 维度 | RESX（本方案） | JSON（主项目模式） |
|------|---------------|-------------------|
| 编译时安全 | ✅ 强类型 `Strings.KeyName` | ❌ 字符串键值 `L("key", "fallback")` |
| AXAML 集成 | ✅ `x:Static` 直接引用 | ❌ 需 code-behind 赋值 |
| 回退机制 | ✅ 内置（默认资源 → 特定文化） | ✅ 自定义 `fallback` 参数 |
| 新增语言 | 需添加 RESX 文件并重新编译 | 仅添加 JSON 文件 |
| AOT 兼容性 | ⚠️ 需额外配置 | ✅ 已验证 |
| 与主项目一致性 | ❌ 不同模式 | ✅ 一致 |

**选择 RESX 的理由**：启动器是独立轻量进程，不需要运行时语言切换；强类型访问减少拼写错误；`x:Static` 比 code-behind 赋值更清晰；RESX 的内置回退机制足够满足启动器需求。

### AOT 兼容性说明

Launcher 项目支持 Native AOT 发布。RESX 的 `ResourceManager` 依赖反射，需要：
1. 在 `.csproj` 中添加 `<EmbeddedResource>` 确保资源不被修剪
2. 在 AOT props 中添加 `TrimmerRootAssembly` 保留资源程序集
3. 发布后进行 AOT 冒烟测试验证

---

## 文件结构规划

### 新增文件

| 文件 | 职责 |
|------|------|
| `Resources/Strings.resx` | 默认资源文件（zh-CN，回退资源） |
| `Resources/Strings.en-US.resx` | 英语资源 |
| `Resources/Strings.ja-JP.resx` | 日语资源 |
| `Resources/Strings.ko-KR.resx` | 韩语资源 |
| `Services/LanguagePreferenceService.cs` | 从 settings.json 读取 LanguageCode 并设置 CultureInfo |

### 修改文件

| 文件 | 改动内容 |
|------|---------|
| `LanMountainDesktop.Launcher.csproj` | 添加 RESX 嵌入资源配置 |
| `LanMountainDesktop.Launcher.AOT.props` | 添加资源程序集修剪保留 |
| `Program.cs` | 启动时调用语言偏好初始化 |
| `Views/SplashWindow.axaml` | 替换硬编码字符串为 `x:Static` |
| `Views/SplashWindow.axaml.cs` | 替换 C# 硬编码字符串为 `Strings.XXX` |
| `Views/ErrorWindow.axaml` | 同上 |
| `Views/ErrorWindow.axaml.cs` | 同上 |
| `Views/MultiInstancePromptWindow.axaml` | 同上 |
| `Views/MultiInstancePromptWindow.axaml.cs` | 同上 |
| `Views/DataLocationPromptWindow.axaml` | 同上 |
| `Views/DataLocationPromptWindow.axaml.cs` | 同上 |
| `Views/LoadingDetailsWindow.axaml` | 同上 |
| `Views/LoadingDetailsWindow.axaml.cs` | 同上 |
| `Views/UpdateWindow.axaml` | 同上 |
| `Views/UpdateWindow.axaml.cs` | 同上 |
| `Views/ErrorDebugWindow.axaml` | 同上 |
| `Views/ErrorDebugWindow.axaml.cs` | 同上 |
| `Views/OobeWindow.axaml` | 同上 |
| `Views/OobeWindow.axaml.cs` | 同上 |
| `Views/MigrationPromptWindow.axaml` | 同上 |
| `Views/MigrationPromptWindow.axaml.cs` | 同上 |
| `Views/PrivacyPolicyWindow.axaml` | 同上 |
| `Views/PrivacyPolicyWindow.axaml.cs` | 同上 |
| `Views/DevDebugWindow.axaml` | 同上 |
| `Views/DevDebugWindow.axaml.cs` | 同上 |
| `Services/LauncherFlowCoordinator.cs` | 替换硬编码字符串 |
| `App.axaml.cs` | 替换预览模式硬编码字符串 |

---

## RESX 键命名规范

采用 `ViewName_ElementDescription` 模式，PascalCase 分隔：

- 窗口标题：`Splash_Title`、`Error_Title`、`MultiInstance_Title`
- 按钮文本：`Error_ButtonOpenLogs`、`Error_ButtonCopy`、`Error_ButtonRetry`
- 状态文本：`Splash_StatusInitializing`、`Loading_StatusPreparing`
- 描述文本：`DataLocation_DescSystemProfile`、`DataLocation_DescPortable`
- OOBE 步骤：`Oobe_StepWelcomeTitle`、`Oobe_StepAppearanceTitle`

---

## 实施任务

### Task 1: 创建 RESX 基础设施

**Files:**
- Create: `LanMountainDesktop.Launcher/Resources/Strings.resx`
- Create: `LanMountainDesktop.Launcher/Resources/Strings.en-US.resx`
- Create: `LanMountainDesktop.Launcher/Resources/Strings.ja-JP.resx`
- Create: `LanMountainDesktop.Launcher/Resources/Strings.ko-KR.resx`
- Modify: `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj`
- Modify: `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.AOT.props`

- [ ] **Step 1: 创建默认 RESX 文件（zh-CN 回退资源）**

创建 `Resources/Strings.resx`，包含所有 180+ 条字符串的中文翻译。此文件同时作为回退资源和中文资源。

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
              <xsd:attribute name="type" type="xsd:string" use="optional" />
              <xsd:attribute name="mimetype" type="xsd:string" use="optional" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms</value></resheader>

  <!-- SplashWindow -->
  <data name="Splash_Title" xml:space="preserve"><value>阑山桌面</value></data>
  <data name="Splash_AppName" xml:space="preserve"><value>阑山桌面</value></data>
  <data name="Splash_StatusInitializing" xml:space="preserve"><value>正在初始化...</value></data>
  <data name="Splash_DebugPreview" xml:space="preserve"><value>[调试模式] 启动画面预览</value></data>

  <!-- ErrorWindow -->
  <data name="Error_Title" xml:space="preserve"><value>阑山桌面</value></data>
  <data name="Error_TitleCannotConfirm" xml:space="preserve"><value>启动器无法确认启动状态</value></data>
  <data name="Error_MessageNotReached" xml:space="preserve"><value>阑山桌面未达到预期的启动状态。</value></data>
  <data name="Error_SuggestionTitle" xml:space="preserve"><value>启动恢复</value></data>
  <data name="Error_SuggestionMessage" xml:space="preserve"><value>您可以检查日志、等待当前进程或激活正在运行的桌面实例。</value></data>
  <data name="Error_DiagnosticHeader" xml:space="preserve"><value>诊断详情</value></data>
  <data name="Error_ButtonOpenLogs" xml:space="preserve"><value>打开日志</value></data>
  <data name="Error_ButtonCopy" xml:space="preserve"><value>复制</value></data>
  <data name="Error_ButtonWait" xml:space="preserve"><value>等待</value></data>
  <data name="Error_ButtonExit" xml:space="preserve"><value>退出</value></data>
  <data name="Error_ButtonRetry" xml:space="preserve"><value>重试</value></data>
  <data name="Error_ButtonActivate" xml:space="preserve"><value>激活</value></data>
  <data name="Error_DebugTitle" xml:space="preserve"><value>[调试] 启动器错误</value></data>
  <data name="Error_HostNotFoundTitle" xml:space="preserve"><value>启动器找不到桌面可执行文件</value></data>
  <data name="Error_HostNotFoundMessage" xml:space="preserve"><value>在调试模式下选择另一个可执行文件、检查日志，或在修复部署路径后重试。</value></data>
  <data name="Error_GenericMessage" xml:space="preserve"><value>检查日志后重试，等待上一次启动尝试完全结束。</value></data>
  <data name="Error_RunningHostMessage" xml:space="preserve"><value>检查日志或退出。旧进程仍在运行时，启动器不会创建新的桌面进程。</value></data>
  <data name="Error_PendingTitle" xml:space="preserve"><value>启动仍在进行中</value></data>
  <data name="Error_PendingMessage" xml:space="preserve"><value>桌面进程仍在运行，启动器不会启动第二个实例。</value></data>

  <!-- MultiInstancePromptWindow -->
  <data name="MultiInstance_Title" xml:space="preserve"><value>阑山桌面</value></data>
  <data name="MultiInstance_AlreadyRunning" xml:space="preserve"><value>阑山桌面已在运行</value></data>
  <data name="MultiInstance_AlreadyRunningMessage" xml:space="preserve"><value>启动器检测到已存在的桌面实例，未启动新进程。</value></data>
  <data name="MultiInstance_RepeatedLaunchTitle" xml:space="preserve"><value>重复启动</value></data>
  <data name="MultiInstance_RepeatedLaunchMessage" xml:space="preserve"><value>您当前的设置为显示此提示而不自动打开桌面。</value></data>
  <data name="MultiInstance_NoSecondProcess" xml:space="preserve"><value>未创建第二个主进程。</value></data>
  <data name="MultiInstance_ButtonCopy" xml:space="preserve"><value>复制</value></data>
  <data name="MultiInstance_ButtonClose" xml:space="preserve"><value>关闭</value></data>
  <data name="MultiInstance_ButtonOpenDesktop" xml:space="preserve"><value>打开桌面</value></data>
  <data name="MultiInstance_DetailsFormat" xml:space="preserve"><value>现有主进程 PID: {0}\nShell 状态: {1}\n未创建第二个主进程。</value></data>

  <!-- DataLocationPromptWindow -->
  <data name="DataLocation_Title" xml:space="preserve"><value>选择数据保存位置</value></data>
  <data name="DataLocation_ChooseLocation" xml:space="preserve"><value>选择数据保存位置</value></data>
  <data name="DataLocation_ChooseLocationDesc" xml:space="preserve"><value>选择启动器和桌面数据的存储位置。您可以稍后在设置中更改。</value></data>
  <data name="DataLocation_NotWritable" xml:space="preserve"><value>应用目录不可写入</value></data>
  <data name="DataLocation_NotWritableDesc" xml:space="preserve"><value>当前安装目录需要管理员权限才能写入。数据将存储在系统用户目录中。</value></data>
  <data name="DataLocation_SystemProfile" xml:space="preserve"><value>保存在系统用户目录（推荐）</value></data>
  <data name="DataLocation_SystemProfileDesc" xml:space="preserve"><value>数据与当前 Windows 用户绑定，在应用重新安装和更新后保持完整。</value></data>
  <data name="DataLocation_Portable" xml:space="preserve"><value>保存在应用安装目录（便携模式）</value></data>
  <data name="DataLocation_PortableDesc" xml:space="preserve"><value>适用于便携安装。整个应用文件夹可以连同数据一起移动到另一台机器。</value></data>
  <data name="DataLocation_ButtonCancel" xml:space="preserve"><value>取消</value></data>
  <data name="DataLocation_ButtonConfirm" xml:space="preserve"><value>确认</value></data>
  <data name="DataLocation_MigrateWarning" xml:space="preserve"><value>检测到已有的系统数据。选择便携模式将自动迁移当前数据。</value></data>

  <!-- LoadingDetailsWindow -->
  <data name="Loading_Title" xml:space="preserve"><value>阑山桌面 - 加载详情</value></data>
  <data name="Loading_StartingDesktop" xml:space="preserve"><value>正在启动阑山桌面</value></data>
  <data name="Loading_StatusInitializing" xml:space="preserve"><value>正在初始化...</value></data>
  <data name="Loading_StatusPreparing" xml:space="preserve"><value>正在准备组件</value></data>
  <data name="Loading_LoadingItems" xml:space="preserve"><value>加载项目</value></data>
  <data name="Loading_Done" xml:space="preserve"><value>完成</value></data>
  <data name="Loading_ErrorOccurred" xml:space="preserve"><value>加载时发生错误。</value></data>
  <data name="Loading_ButtonDetails" xml:space="preserve"><value>详情</value></data>
  <data name="Loading_ButtonCancel" xml:space="preserve"><value>取消</value></data>
  <data name="Loading_StageReady" xml:space="preserve"><value>准备就绪</value></data>
  <data name="Loading_ItemPlugin" xml:space="preserve"><value>正在加载插件...</value></data>
  <data name="Loading_ItemComponent" xml:space="preserve"><value>正在加载组件...</value></data>
  <data name="Loading_ItemResource" xml:space="preserve"><value>正在加载资源...</value></data>
  <data name="Loading_ItemData" xml:space="preserve"><value>正在加载数据...</value></data>
  <data name="Loading_ItemDownload" xml:space="preserve"><value>正在下载...</value></data>
  <data name="Loading_ItemProcess" xml:space="preserve"><value>正在处理...</value></data>
  <data name="Loading_ItemComplete" xml:space="preserve"><value>完成</value></data>
  <data name="Loading_TypePlugin" xml:space="preserve"><value>插件</value></data>
  <data name="Loading_TypeComponent" xml:space="preserve"><value>组件</value></data>
  <data name="Loading_TypeResource" xml:space="preserve"><value>资源</value></data>
  <data name="Loading_TypeData" xml:space="preserve"><value>数据</value></data>
  <data name="Loading_TypeNetwork" xml:space="preserve"><value>网络</value></data>
  <data name="Loading_TypeSettings" xml:space="preserve"><value>设置</value></data>
  <data name="Loading_TypeSystem" xml:space="preserve"><value>系统</value></data>
  <data name="Loading_TypeOther" xml:space="preserve"><value>其他</value></data>

  <!-- UpdateWindow -->
  <data name="Update_Title" xml:space="preserve"><value>阑山桌面 - 更新</value></data>
  <data name="Update_AppName" xml:space="preserve"><value>阑山桌面</value></data>
  <data name="Update_StatusUpdate" xml:space="preserve"><value>更新</value></data>
  <data name="Update_StatusUpdating" xml:space="preserve"><value>正在更新，请稍候...</value></data>
  <data name="Update_Complete" xml:space="preserve"><value>更新完成</value></data>
  <data name="Update_Failed" xml:space="preserve"><value>更新失败</value></data>
  <data name="Update_FailedMessage" xml:space="preserve"><value>更新过程中发生错误</value></data>
  <data name="Update_DebugTitle" xml:space="preserve"><value>[调试模式] 更新页面</value></data>
  <data name="Update_DebugMessage" xml:space="preserve"><value>预览更新进度界面</value></data>

  <!-- ErrorDebugWindow -->
  <data name="DebugDebug_Title" xml:space="preserve"><value>调试模式</value></data>
  <data name="DebugDebug_SettingsTitle" xml:space="preserve"><value>调试设置</value></data>
  <data name="DebugDebug_DevMode" xml:space="preserve"><value>开发模式</value></data>
  <data name="DebugDebug_DevModeDesc" xml:space="preserve"><value>启用后自动扫描开发目录</value></data>
  <data name="DebugDebug_On" xml:space="preserve"><value>开</value></data>
  <data name="DebugDebug_Off" xml:space="preserve"><value>关</value></data>
  <data name="DebugDebug_AppPath" xml:space="preserve"><value>应用路径</value></data>
  <data name="DebugDebug_NotSelected" xml:space="preserve"><value>未选择</value></data>
  <data name="DebugDebug_Browse" xml:space="preserve"><value>浏览...</value></data>
  <data name="DebugDebug_Warning" xml:space="preserve"><value>此功能仅供开发人员使用</value></data>
  <data name="DebugDebug_ButtonCancel" xml:space="preserve"><value>取消</value></data>
  <data name="DebugDebug_ButtonOk" xml:space="preserve"><value>确定</value></data>
  <data name="DebugDebug_SelectExeDialog" xml:space="preserve"><value>选择阑山桌面主程序可执行文件</value></data>

  <!-- OobeWindow -->
  <data name="Oobe_Title" xml:space="preserve"><value>欢迎使用阑山桌面</value></data>
  <data name="Oobe_WelcomeTitle" xml:space="preserve"><value>欢迎使用阑山桌面</value></data>
  <data name="Oobe_WelcomeSubtitle" xml:space="preserve"><value>你的桌面，不止一面</value></data>
  <data name="Oobe_ButtonGetStarted" xml:space="preserve"><value>开始使用</value></data>
  <data name="Oobe_AppearanceTitle" xml:space="preserve"><value>个性化你的桌面</value></data>
  <data name="Oobe_AppearanceDesc" xml:space="preserve"><value>选择你喜欢的主题样式，可随时在设置中更改</value></data>
  <data name="Oobe_AppearanceMode" xml:space="preserve"><value>外观模式</value></data>
  <data name="Oobe_LightMode" xml:space="preserve"><value>浅色模式</value></data>
  <data name="Oobe_DarkMode" xml:space="preserve"><value>深色模式</value></data>
  <data name="Oobe_ThemeColor" xml:space="preserve"><value>主题色</value></data>
  <data name="Oobe_MonetSource" xml:space="preserve"><value>莫奈取色来源</value></data>
  <data name="Oobe_MonetFromWallpaper" xml:space="preserve"><value>从桌面壁纸取色</value></data>
  <data name="Oobe_MonetFromCustomImage" xml:space="preserve"><value>自定义图片取色</value></data>
  <data name="Oobe_MonetDisabled" xml:space="preserve"><value>不使用莫奈取色</value></data>
  <data name="Oobe_DataLocationTitle" xml:space="preserve"><value>选择数据保存位置</value></data>
  <data name="Oobe_SystemProfile" xml:space="preserve"><value>保存在系统用户目录（推荐）</value></data>
  <data name="Oobe_SystemProfileDesc" xml:space="preserve"><value>数据与当前 Windows 用户绑定，在应用重新安装和更新后保持完整。</value></data>
  <data name="Oobe_Portable" xml:space="preserve"><value>保存在应用安装目录（便携模式）</value></data>
  <data name="Oobe_PortableDesc" xml:space="preserve"><value>适用于便携安装。整个应用文件夹可以连同数据一起移动到另一台机器。</value></data>
  <data name="Oobe_NotWritable" xml:space="preserve"><value>无法保存到应用目录</value></data>
  <data name="Oobe_NotWritableDesc" xml:space="preserve"><value>当前安装目录需要管理员权限才能写入。数据将存储在系统用户目录中。</value></data>
  <data name="Oobe_StartupTitle" xml:space="preserve"><value>启动与展示</value></data>
  <data name="Oobe_ShowInTaskbar" xml:space="preserve"><value>在任务栏显示主桌面窗口</value></data>
  <data name="Oobe_SlideTransition" xml:space="preserve"><value>以滑动方式显示主窗口</value></data>
  <data name="Oobe_FadeTransition" xml:space="preserve"><value>启动时使用淡入过渡</value></data>
  <data name="Oobe_FusedDesktop" xml:space="preserve"><value>融合桌面与弹入手势</value></data>
  <data name="Oobe_AutoStart" xml:space="preserve"><value>登录 Windows 时自动启动阑山桌面</value></data>
  <data name="Oobe_PrivacyTitle" xml:space="preserve"><value>信息与隐私</value></data>
  <data name="Oobe_CrashReports" xml:space="preserve"><value>发送匿名崩溃报告</value></data>
  <data name="Oobe_UsageStats" xml:space="preserve"><value>发送匿名使用统计</value></data>
  <data name="Oobe_PrivacyTrackingId" xml:space="preserve"><value>隐私追踪 ID</value></data>
  <data name="Oobe_Agree" xml:space="preserve"><value>同意</value></data>
  <data name="Oobe_PrivacyPolicyLink" xml:space="preserve"><value>《阑山桌面遥测隐私数据收集协议》</value></data>
  <data name="Oobe_ButtonBack" xml:space="preserve"><value>返回</value></data>
  <data name="Oobe_ButtonNext" xml:space="preserve"><value>下一步</value></data>
  <data name="Oobe_CompleteTitle" xml:space="preserve"><value>欢迎使用阑山桌面</value></data>
  <data name="Oobe_CompleteSubtitle" xml:space="preserve"><value>你的桌面，不止一面</value></data>

  <!-- MigrationPromptWindow -->
  <data name="Migration_Title" xml:space="preserve"><value>阑山桌面 - 版本迁移</value></data>
  <data name="Migration_DetectedOldVersion" xml:space="preserve"><value>检测到旧版本</value></data>
  <data name="Migration_DetectedDesc" xml:space="preserve"><value>检测到您的系统中安装了旧版本的阑山桌面（0.8.4）...</value></data>
  <data name="Migration_Version" xml:space="preserve"><value>版本：</value></data>
  <data name="Migration_Location" xml:space="preserve"><value>位置：</value></data>
  <data name="Migration_Type" xml:space="preserve"><value>类型：</value></data>
  <data name="Migration_Installed" xml:space="preserve"><value>安装版</value></data>
  <data name="Migration_UninstallNote" xml:space="preserve"><value>卸载旧版本不会影响新版本的使用，您的个人数据将保留。</value></data>
  <data name="Migration_ButtonViewLocation" xml:space="preserve"><value>查看位置</value></data>
  <data name="Migration_ButtonSkip" xml:space="preserve"><value>暂不处理</value></data>
  <data name="Migration_ButtonUninstall" xml:space="preserve"><value>卸载旧版本</value></data>

  <!-- PrivacyPolicyWindow -->
  <data name="Privacy_Title" xml:space="preserve"><value>阑山桌面遥测隐私数据收集协议</value></data>
  <data name="Privacy_Header" xml:space="preserve"><value>阑山桌面遥测隐私数据收集协议</value></data>
  <data name="Privacy_Description" xml:space="preserve"><value>请仔细阅读以下协议内容，了解我们如何收集、使用和保护您的数据</value></data>
  <data name="Privacy_ButtonClose" xml:space="preserve"><value>关闭</value></data>

  <!-- DevDebugWindow -->
  <data name="DevDebug_Title" xml:space="preserve"><value>开发调试窗口</value></data>
  <data name="DevDebug_Splash" xml:space="preserve"><value>启动画面</value></data>
  <data name="DevDebug_Error" xml:space="preserve"><value>错误页面</value></data>
  <data name="DevDebug_Update" xml:space="preserve"><value>更新页面</value></data>
  <data name="DevDebug_Oobe" xml:space="preserve"><value>OOBE页面</value></data>
  <data name="DevDebug_DataLocation" xml:space="preserve"><value>数据位置选择</value></data>
  <data name="DevDebug_EnableFeature" xml:space="preserve"><value>启用功能</value></data>
  <data name="DevDebug_Open" xml:space="preserve"><value>打开</value></data>
  <data name="DevDebug_SetAllViewMode" xml:space="preserve"><value>全部设为查看模式</value></data>
  <data name="DevDebug_SetAllFunctionMode" xml:space="preserve"><value>全部设为功能模式</value></data>
  <data name="DevDebug_Close" xml:space="preserve"><value>关闭</value></data>

  <!-- LauncherFlowCoordinator -->
  <data name="Coordinator_SlowDeviceMessage" xml:space="preserve"><value>设备较慢，仍在启动，请稍候。</value></data>
  <data name="Coordinator_RunningHostMessage" xml:space="preserve"><value>桌面主进程仍在运行，Launcher 会继续等待，不会重复启动。</value></data>

  <!-- App.axaml.cs preview strings -->
  <data name="Preview_SplashInitializing" xml:space="preserve"><value>正在初始化...</value></data>
  <data name="Preview_SplashCheckingUpdates" xml:space="preserve"><value>正在检查更新...</value></data>
  <data name="Preview_SplashCheckingPlugins" xml:space="preserve"><value>正在检查插件...</value></data>
  <data name="Preview_SplashLaunchingHost" xml:space="preserve"><value>正在启动主程序...</value></data>
  <data name="Preview_SplashReady" xml:space="preserve"><value>准备就绪</value></data>
  <data name="Preview_ErrorMessage" xml:space="preserve"><value>[预览] 这是启动器错误窗口预览。</value></data>
  <data name="Preview_UpdateProcessing" xml:space="preserve"><value>正在处理 {0}...</value></data>
  <data name="Preview_ActivationConnecting" xml:space="preserve"><value>正在连接到活跃的启动器...</value></data>
</root>
```

- [ ] **Step 2: 创建 en-US RESX 文件**

创建 `Resources/Strings.en-US.resx`，包含所有字符串的英文翻译。结构与默认文件相同，仅 `<value>` 内容为英文。

```xml
<!-- 示例条目 -->
<data name="Splash_Title" xml:space="preserve"><value>LanMountain Desktop</value></data>
<data name="Splash_AppName" xml:space="preserve"><value>LanMountain Desktop</value></data>
<data name="Splash_StatusInitializing" xml:space="preserve"><value>Initializing...</value></data>
<data name="Error_TitleCannotConfirm" xml:space="preserve"><value>Launcher could not confirm startup</value></data>
<data name="Error_MessageNotReached" xml:space="preserve"><value>LanMountain Desktop did not reach the expected startup state.</value></data>
<!-- ... 所有键的英文翻译 ... -->
```

- [ ] **Step 3: 创建 ja-JP RESX 文件**

创建 `Resources/Strings.ja-JP.resx`，包含所有字符串的日语翻译。

- [ ] **Step 4: 创建 ko-KR RESX 文件**

创建 `Resources/Strings.ko-KR.resx`，包含所有字符串的韩语翻译。

- [ ] **Step 5: 修改 .csproj 添加 RESX 配置**

在 `LanMountainDesktop.Launcher.csproj` 的 `<ItemGroup>` 中添加：

```xml
<ItemGroup>
  <EmbeddedResource Update="Resources\Strings.resx">
    <Generator>PublicResXFileCodeGenerator</Generator>
    <LastGenOutput>Strings.Designer.cs</LastGenOutput>
  </EmbeddedResource>
</ItemGroup>
```

注意：使用 `PublicResXFileCodeGenerator` 而非 `ResXFileCodeGenerator`，生成 `public` 类以便 AXAML 的 `x:Static` 可以访问。

- [ ] **Step 6: 修改 AOT props 添加资源程序集保留**

在 `LanMountainDesktop.Launcher.AOT.props` 的 AOT 修剪配置 `<ItemGroup>` 中添加：

```xml
<TrimmerRootAssembly Include="LanMountainDesktop.Launcher" />
```

- [ ] **Step 7: 运行构建验证 RESX 生成**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功，`Resources/Strings.Designer.cs` 自动生成

---

### Task 2: 创建语言偏好服务

**Files:**
- Create: `LanMountainDesktop.Launcher/Services/LanguagePreferenceService.cs`
- Modify: `LanMountainDesktop.Launcher/Program.cs`

- [ ] **Step 1: 创建 LanguagePreferenceService**

```csharp
using System.Globalization;
using System.Text.Json.Nodes;

namespace LanMountainDesktop.Launcher.Services;

internal static class LanguagePreferenceService
{
    public static string ResolveLanguageCode(string appRoot)
    {
        try
        {
            var dataLocationResolver = new DataLocationResolver(appRoot);
            var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(dataLocationResolver.ResolveDataRoot());
            if (!File.Exists(settingsPath))
            {
                return "zh-CN";
            }

            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is not null &&
                root.TryGetPropertyValue("LanguageCode", out var node) &&
                node is JsonValue value &&
                value.TryGetValue<string>(out var code) &&
                !string.IsNullOrWhiteSpace(code))
            {
                return NormalizeLanguageCode(code);
            }
        }
        catch
        {
        }

        return "zh-CN";
    }

    public static void ApplyLanguage(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private static string NormalizeLanguageCode(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en-us" or "en" => "en-US",
            "ja-jp" or "ja" => "ja-JP",
            "ko-kr" or "ko" => "ko-KR",
            _ => "zh-CN"
        };
    }
}
```

- [ ] **Step 2: 在 Program.cs 中调用语言初始化**

在 `Program.Main` 方法中，`BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` 之前添加语言初始化：

```csharp
var appRoot = Commands.ResolveAppRoot(commandContext);
var languageCode = LanguagePreferenceService.ResolveLanguageCode(appRoot);
LanguagePreferenceService.ApplyLanguage(languageCode);
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 3: 替换 SplashWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/SplashWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/SplashWindow.axaml.cs`

- [ ] **Step 1: 在 SplashWindow.axaml 中添加 RESX 命名空间并替换字符串**

在 `<Window>` 标签添加命名空间：
```xml
xmlns:res="clr-namespace:LanMountainDesktop.Launcher.Resources"
```

替换硬编码字符串：
- `Title="LanMountain Desktop"` → `Title="{x:Static res:Strings.Splash_Title}"`
- `Text="LanMountain Desktop"` (AppNameText) → `Text="{x:Static res:Strings.Splash_AppName}"`
- `Text="Initializing..."` (StatusText) → `Text="{x:Static res:Strings.Splash_StatusInitializing}"`

注意：`VersionText` 的 `Text="0.0.0-dev (Administrate)"` 是动态设置的占位文本，保留原样（由 code-behind `SetVersionInfo` 方法设置）。

- [ ] **Step 2: 在 SplashWindow.axaml.cs 中替换 C# 硬编码字符串**

将 `"[Debug Mode] Splash Preview"` 替换为 `Strings.Splash_DebugPreview`。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 4: 替换 ErrorWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/ErrorWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/ErrorWindow.axaml.cs`

- [ ] **Step 1: 在 ErrorWindow.axaml 中添加 RESX 命名空间并替换字符串**

添加命名空间 `xmlns:res="clr-namespace:LanMountainDesktop.Launcher.Resources"`

AXAML 替换：
- `Title="LanMountain Desktop"` → `Title="{x:Static res:Strings.Error_Title}"`
- `Text="Launcher could not confirm startup"` → `Text="{x:Static res:Strings.Error_TitleCannotConfirm}"`
- `Text="LanMountain Desktop did not reach..."` → `Text="{x:Static res:Strings.Error_MessageNotReached}"`
- `Title="Startup recovery"` → `Title="{x:Static res:Strings.Error_SuggestionTitle}"`
- `Message="You can inspect logs..."` → `Message="{x:Static res:Strings.Error_SuggestionMessage}"`
- `Header="Diagnostic details"` → `Header="{x:Static res:Strings.Error_DiagnosticHeader}"`
- `Text="Open Logs"` → `Text="{x:Static res:Strings.Error_ButtonOpenLogs}"`
- `Text="Copy"` → `Text="{x:Static res:Strings.Error_ButtonCopy}"`
- `Content="Wait"` → `Content="{x:Static res:Strings.Error_ButtonWait}"`
- `Text="Exit"` → `Text="{x:Static res:Strings.Error_ButtonExit}"`
- `Content="Retry"` → `Content="{x:Static res:Strings.Error_ButtonRetry}"`

- [ ] **Step 2: 在 ErrorWindow.axaml.cs 中替换 C# 硬编码字符串**

将所有硬编码字符串替换为 `Strings.XXX` 调用：
- `"LanMountain Desktop did not reach..."` → `Strings.Error_MessageNotReached`
- `"[Debug] Launcher error"` → `Strings.Error_DebugTitle`
- `"Launcher could not find the desktop executable"` → `Strings.Error_HostNotFoundTitle`
- `"Pick another executable..."` → `Strings.Error_HostNotFoundMessage`
- `"Launcher could not confirm startup"` → `Strings.Error_TitleCannotConfirm`
- `"Inspect logs, then retry..."` → `Strings.Error_GenericMessage`
- `"Inspect logs or exit..."` → `Strings.Error_RunningHostMessage`
- `"Retry"` → `Strings.Error_ButtonRetry`
- `"Activate"` → `Strings.Error_ButtonActivate`
- `"Wait"` → `Strings.Error_ButtonWait`
- `"Startup is still pending"` → `Strings.Error_PendingTitle`
- `"The desktop process is still running..."` → `Strings.Error_PendingMessage`

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 5: 替换 MultiInstancePromptWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/MultiInstancePromptWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/MultiInstancePromptWindow.axaml.cs`

- [ ] **Step 1: 在 MultiInstancePromptWindow.axaml 中替换字符串**

添加命名空间，替换：
- `Title="LanMountain Desktop"` → `Title="{x:Static res:Strings.MultiInstance_Title}"`
- `Text="LanMountain Desktop is already running"` → `Text="{x:Static res:Strings.MultiInstance_AlreadyRunning}"`
- `Text="Launcher found an existing..."` → `Text="{x:Static res:Strings.MultiInstance_AlreadyRunningMessage}"`
- `Title="Repeated launch"` → `Title="{x:Static res:Strings.MultiInstance_RepeatedLaunchTitle}"`
- `Message="Your current setting..."` → `Message="{x:Static res:Strings.MultiInstance_RepeatedLaunchMessage}"`
- `Text="No second Host process..."` → `Text="{x:Static res:Strings.MultiInstance_NoSecondProcess}"`
- `Text="Copy"` → `Text="{x:Static res:Strings.MultiInstance_ButtonCopy}"`
- `Text="Close"` → `Text="{x:Static res:Strings.MultiInstance_ButtonClose}"`
- `Text="Open desktop"` → `Text="{x:Static res:Strings.MultiInstance_ButtonOpenDesktop}"`

- [ ] **Step 2: 在 MultiInstancePromptWindow.axaml.cs 中替换 C# 硬编码字符串**

将格式化字符串替换为 `string.Format(Strings.MultiInstance_DetailsFormat, processId, shellState)` 等。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 6: 替换 DataLocationPromptWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/DataLocationPromptWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/DataLocationPromptWindow.axaml.cs`

- [ ] **Step 1: 在 DataLocationPromptWindow.axaml 中替换字符串**

替换所有 12 个硬编码字符串为 `x:Static` 引用。

- [ ] **Step 2: 在 DataLocationPromptWindow.axaml.cs 中替换 C# 硬编码字符串**

将 `"Existing system data was detected..."` 替换为 `Strings.DataLocation_MigrateWarning`。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 7: 替换 LoadingDetailsWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/LoadingDetailsWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/LoadingDetailsWindow.axaml.cs`

- [ ] **Step 1: 在 LoadingDetailsWindow.axaml 中替换字符串**

替换所有硬编码字符串为 `x:Static` 引用。

- [ ] **Step 2: 在 LoadingDetailsWindow.axaml.cs 中替换 C# 硬编码字符串**

替换 `GetStageDescription`、`GetItemDescription`、`GetTypeLabel` 方法中的硬编码字符串为 `Strings.XXX` 调用。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 8: 替换 UpdateWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/UpdateWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/UpdateWindow.axaml.cs`

- [ ] **Step 1: 在 UpdateWindow.axaml 中替换字符串**

替换 `"Update"` 为 `x:Static res:Strings.Update_StatusUpdate`。

- [ ] **Step 2: 在 UpdateWindow.axaml.cs 中替换 C# 硬编码字符串**

替换 `"更新完成"`、`"更新失败"`、`"更新过程中发生错误"`、`"[调试模式] 更新页面"`、`"预览更新进度界面"` 为 `Strings.XXX` 调用。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 9: 替换 ErrorDebugWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/ErrorDebugWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/ErrorDebugWindow.axaml.cs`

- [ ] **Step 1: 在 ErrorDebugWindow.axaml 中替换字符串**

该窗口已使用中文，替换所有硬编码中文字符串为 `x:Static` 引用。

- [ ] **Step 2: 在 ErrorDebugWindow.axaml.cs 中替换 C# 硬编码字符串**

替换 `"Select LanMountainDesktop host executable"` 和 `"Not selected"` 为 `Strings.DebugDebug_SelectExeDialog` 和 `Strings.DebugDebug_NotSelected`。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 10: 替换 OobeWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/OobeWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/OobeWindow.axaml.cs`

这是最大的单个任务，OobeWindow 有约 42 个硬编码字符串。

- [ ] **Step 1: 在 OobeWindow.axaml 中替换字符串**

添加命名空间，逐个替换所有硬编码中文字符串为 `x:Static` 引用。包括：
- 窗口标题、欢迎页文本
- 外观设置页文本
- 数据位置页文本
- 启动展示页文本
- 隐私页文本
- 完成页文本
- 导航按钮文本

- [ ] **Step 2: 在 OobeWindow.axaml.cs 中替换 C# 硬编码字符串（如有）**

检查 code-behind 中是否有动态设置的硬编码字符串并替换。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 11: 替换 MigrationPromptWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/MigrationPromptWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/MigrationPromptWindow.axaml.cs`

- [ ] **Step 1: 在 MigrationPromptWindow.axaml 中替换字符串**

替换所有硬编码中文字符串为 `x:Static` 引用。

- [ ] **Step 2: 在 MigrationPromptWindow.axaml.cs 中替换 C# 硬编码字符串（如有）**

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 12: 替换 PrivacyPolicyWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/PrivacyPolicyWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/PrivacyPolicyWindow.axaml.cs`

- [ ] **Step 1: 在 PrivacyPolicyWindow.axaml 中替换字符串**

替换标题、描述、关闭按钮等硬编码字符串。

- [ ] **Step 2: 在 PrivacyPolicyWindow.axaml.cs 中处理隐私政策正文**

隐私政策正文（约 80 行 Markdown）目前硬编码在 C# 中。考虑：
- 方案 A：将 Markdown 正文也放入 RESX（支持多语言隐私政策）
- 方案 B：保留 Markdown 正文在 C# 中，仅替换窗口标题和按钮

推荐方案 A，将隐私政策 Markdown 正文放入 RESX 的 `Privacy_PolicyContent` 键中。

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 13: 替换 DevDebugWindow 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Views/DevDebugWindow.axaml`
- Modify: `LanMountainDesktop.Launcher/Views/DevDebugWindow.axaml.cs`

- [ ] **Step 1: 在 DevDebugWindow.axaml 中替换字符串**

替换所有硬编码中文字符串为 `x:Static` 引用。

- [ ] **Step 2: 在 DevDebugWindow.axaml.cs 中替换 C# 硬编码字符串（如有）**

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 14: 替换 LauncherFlowCoordinator 和 App.axaml.cs 硬编码字符串

**Files:**
- Modify: `LanMountainDesktop.Launcher/Services/LauncherFlowCoordinator.cs`
- Modify: `LanMountainDesktop.Launcher/App.axaml.cs`

- [ ] **Step 1: 在 LauncherFlowCoordinator.cs 中替换字符串**

替换：
- `"设备较慢，仍在启动，请稍候。"` → `Strings.Coordinator_SlowDeviceMessage`
- `"桌面主进程仍在运行..."` → `Strings.Coordinator_RunningHostMessage`

- [ ] **Step 2: 在 App.axaml.cs 中替换预览模式字符串**

替换 `SimulateSplashPreviewAsync` 中的硬编码消息数组：
```csharp
var messages = new[] { Strings.Preview_SplashInitializing, Strings.Preview_SplashCheckingUpdates, Strings.Preview_SplashCheckingPlugins, Strings.Preview_SplashLaunchingHost, Strings.Preview_SplashReady };
```

替换 `HandlePreviewCommand` 中的 `"[Preview] This is the launcher error window preview."` → `Strings.Preview_ErrorMessage`

替换 `RunApplyUpdateWithWindowAsync` 中的硬编码字符串：
- `"Verifying update..."` → 使用 RESX 键
- `"Applying plugin upgrades..."` → 使用 RESX 键
- `"Cleaning up old deployments..."` → 使用 RESX 键

替换 `SimulateUpdatePreviewAsync` 中的 `$"Processing {stages[i]}..."` → `string.Format(Strings.Preview_UpdateProcessing, stages[i])`

替换 `AttachToExistingCoordinatorAsync` 中的 `"Connecting to the active launcher..."` → `Strings.Preview_ActivationConnecting`

- [ ] **Step 3: 构建验证**

Run: `dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug`
Expected: 构建成功

---

### Task 15: 完整构建和运行验证

**Files:** 无新增/修改

- [ ] **Step 1: 完整解决方案构建**

Run: `dotnet build LanMountainDesktop.slnx -c Debug`
Expected: 构建成功，无错误

- [ ] **Step 2: 运行启动器预览命令验证中文**

Run: `dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- preview-splash`
Expected: 启动画面显示中文

- [ ] **Step 3: 验证英文模式**

临时将 `LanguagePreferenceService.ResolveLanguageCode` 返回 `"en-US"` 后运行预览命令，验证英文显示。

- [ ] **Step 4: 运行测试**

Run: `dotnet test LanMountainDesktop.slnx -c Debug`
Expected: 所有测试通过

---

### Task 16: AOT 发布冒烟测试

**Files:** 无新增/修改

- [ ] **Step 1: AOT 发布测试**

Run: `dotnet publish LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Release -r win-x64 /p:PublishAot=true`
Expected: 发布成功

- [ ] **Step 2: 运行 AOT 发布产物验证**

运行发布后的可执行文件，验证 RESX 资源正确加载。

---

## 实施顺序建议

1. **Task 1** (RESX 基础设施) → **Task 2** (语言偏好服务) — 必须首先完成
2. **Task 3-9** (英文窗口) — 优先处理，解决用户提出的"只有英文"问题
3. **Task 10-13** (中文窗口) — 次优先，完成完整 i18n 覆盖
4. **Task 14** (服务层和 App) — 与 Task 3-13 并行或随后
5. **Task 15-16** (验证) — 最后执行

## 风险与注意事项

1. **AOT 兼容性**：`ResourceManager` 在 Native AOT 下可能需要额外配置。如果 AOT 发布失败，需要添加 `DynamicDependency` 属性或使用 `System.Resources.Extensions` 包的源生成器。
2. **OOBE 首次运行**：OOBE 在首次运行时 `settings.json` 不存在，此时 `LanguagePreferenceService` 会回退到 `zh-CN`。这是合理的行为。
3. **`x:Static` 与 Avalonia CompiledBindings**：项目启用了 `AvaloniaUseCompiledBindingsByDefault`，需要确认 `x:Static` 在编译绑定模式下正常工作。如有问题，可在特定 AXAML 文件中添加 `x:CompileBindings="False"`。
4. **RESX Designer.cs 生成**：确保 `.csproj` 中使用 `PublicResXFileCodeGenerator` 生成 `public` 类，否则 `x:Static` 无法访问。
5. **隐私政策多语言**：隐私政策 Markdown 正文较长，放入 RESX 可能影响可读性。可考虑保留在 C# 中或使用独立资源文件。
