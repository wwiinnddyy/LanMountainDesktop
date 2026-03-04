# LanMontainDesktop GitHub Actions CI/CD

参考 ClassIsland 项目最佳实践，为 LanMontainDesktop 配置的 GitHub Actions 工作流。

## 📋 工作流说明

### 1. Build (`build.yml`)
**何时运行:** 每次 push、PR、手动触发

**功能:**
- Windows: Release + Debug 配置
- Linux: Release 配置
- macOS: Release 配置
- 上传编译输出 artifacts

**运行时间:** ~5-10 分钟

### 2. Quality Check (`code-quality.yml`)
**何时运行:** PR 或 push

**功能:**
- 编译检查
- 代码格式检查 (`dotnet format`)

**运行时间:** ~3-5 分钟

### 3. Release (`release.yml`)
**何时运行:** Push 标签 (`v*`) 或手动触发

**功能:**
- 并行构建所有平台 (Windows x64/x86, Linux x64, macOS x64/arm64)
- 自动创建 GitHub Release
- 上传所有平台的可执行包

**运行时间:** ~20-30 分钟

**触发方式:**

```bash
# 推送标签 - 自动触发
git tag v1.0.0
git push origin v1.0.0

# 或手动触发
# GitHub Actions > Release > Run workflow
# 输入: tag (例如 v1.0.0)
```

### 4. Issue Management (`issue-management.yml`)
**何时运行:** 每天 1:30 AM UTC

**功能:**
- 标记 30 天无活动的 Issue
- 关闭 7 天无活动的 stale Issue
- 对 PR 同样处理

---

## 🚀 快速开始

### 创建版本发布

```bash
# 1. 提交最后的更改
git add .
git commit -m "Release v1.0.0"

# 2. 创建标签
git tag v1.0.0 -m "Release version 1.0.0"

# 3. 推送
git push origin main
git push origin v1.0.0

# 4. 等待... (GitHub Actions 自动构建)
# 约 20-30 分钟后，Release 将自动创建
```

### 查看工作流状态

访问: **GitHub 项目 > Actions 标签**

---

## 📁 支持的平台与格式

| 平台 | 架构 | 输出格式 |
|------|------|---------|
| Windows | x64, x86 | `.zip` |
| Linux | x64 | `.tar.gz` |
| macOS | x64, arm64 | `.tar.gz` |

---

## 🛠️ 本地构建参考

### Windows

```bash
# 使用现有脚本
.\LanMontainDesktop\scripts\package.ps1 -RuntimeIdentifier win-x64

# 或用 dotnet 直接构建
dotnet build -c Release
dotnet publish LanMontainDesktop/LanMontainDesktop.csproj `
    -c Release -r win-x64 -o ./publish/win-x64 `
    --self-contained -p:PublishSingleFile=true
```

### Linux / macOS

```bash
# 使用 build 脚本
chmod +x scripts/build.sh
./scripts/build.sh publish --config Release --rid linux-x64
./scripts/build.sh publish --config Release --rid osx-x64
./scripts/build.sh publish --config Release --rid osx-arm64

# 或用 dotnet 直接构建
dotnet build -c Release
dotnet publish LanMontainDesktop/LanMontainDesktop.csproj \
    -c Release -r linux-x64 -o ./publish/linux-x64 \
    --self-contained -p:PublishSingleFile=true
```

---

## 📊 Actions 使用统计

**免费额度:** 2000 runner-hours/月 (对大多数项目用不完)

**预计使用:**
- Build job: ~3-5 分钟 × 3 平台
- Code quality: ~3-5 分钟
- Release: ~25-30 分钟 × 1/周

**月总计:** ~30-50 分钟 × 20+ 次 = ~600-1000 分钟 (远低于免费额度)

---

## 🐛 常见问题

### Release 工作流不运行?

检查：
1. 标签格式是否为 `v*` (例如：`v1.0.0`)
2. `.csproj` 文件是否有效
3. GitHub Actions 是否已启用

### 特定平台构建失败?

查看 Actions 日志：
1. **Windows**: 检查 libvlc 依赖
2. **Linux**: 检查系统库依赖
3. **macOS**: 检查 Xcode 工具链

### 如何跳过某个工作流?

在 commit 消息中添加：
```
[skip ci]  # 跳过所有工作流
[skip build]  # 跳过构建
```

---

## 🔗 参考

- [ClassIsland CI/CD](https://github.com/ClassIsland/ClassIsland/.github/workflows/)
- [GitHub Actions 文档](https://docs.github.com/actions)
- [.NET 发布指南](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish)
- [Avalonia 部署](https://docs.avaloniaui.net/docs/deployment)

---

**更新:** 2026-03-04  
**版本:** 2.0 (参考 ClassIsland)  
**状态:** ✅ 生产可用
