# 01-GitHub Actions入门

GitHub Actions 是自动化构建、测试和发布插件的强大工具。本文介绍如何为插件项目配置 CI/CD 流程。

---

## 🎯 什么是 GitHub Actions

GitHub Actions 是 GitHub 提供的持续集成/持续部署（CI/CD）服务，可以：

- ✅ 自动构建插件
- ✅ 运行单元测试
- ✅ 打包 .laapp 文件
- ✅ 自动发布到 GitHub Releases

---

## 📁 工作流文件位置

```
.github/workflows/
├── build.yml           # 构建工作流
├── release.yml         # 发布工作流
└── code-quality.yml    # 代码质量检查
```

---

## 🚀 基础工作流示例

### 最简单的构建工作流

```yaml
# .github/workflows/build.yml
name: Build Plugin

on:
  push:
    branches: [main, master]
  pull_request:
    branches: [main, master]

jobs:
  build:
    runs-on: windows-latest
    
    steps:
      # 1. 检出代码
      - name: Checkout
        uses: actions/checkout@v4
      
      # 2. 设置 .NET
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      # 3. 还原依赖
      - name: Restore
        run: dotnet restore
      
      # 4. 构建
      - name: Build
        run: dotnet build --configuration Release --no-restore
      
      # 5. 上传构建产物
      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: plugin-package
          path: bin/Release/net10.0/*.laapp
```

---

## 📋 工作流详解

### 触发条件（on）

```yaml
on:
  # 推送到指定分支时触发
  push:
    branches: [main, master, develop]
    
  # 创建 Pull Request 时触发
  pull_request:
    branches: [main, master]
    
  # 手动触发
  workflow_dispatch:
    
  # 定时触发（每天凌晨2点）
  schedule:
    - cron: '0 2 * * *'
    
  # 创建标签时触发（用于发布）
  push:
    tags:
      - 'v*'
```

### 运行环境（runs-on）

```yaml
jobs:
  build:
    runs-on: windows-latest    # Windows 环境
    # 或
    runs-on: ubuntu-latest     # Linux 环境
    # 或
    runs-on: macos-latest      # macOS 环境
```

### 矩阵构建（多平台）

```yaml
jobs:
  build:
    strategy:
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
        dotnet: ['10.0.x']
    
    runs-on: ${{ matrix.os }}
    
    steps:
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ matrix.dotnet }}
```

---

## 🔧 常用 Actions

### 检出代码

```yaml
- uses: actions/checkout@v4
  with:
    fetch-depth: 0    # 获取完整历史（用于生成版本号）
```

### 设置 .NET

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
```

### 上传产物

```yaml
- uses: actions/upload-artifact@v4
  with:
    name: plugin-package
    path: bin/Release/net10.0/*.laapp
    retention-days: 30    # 保留30天
```

### 下载产物

```yaml
- uses: actions/download-artifact@v4
  with:
    name: plugin-package
    path: ./artifacts
```

---

## 💡 最佳实践

### 1. 缓存依赖

```yaml
- uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

### 2. 使用语义化版本

```yaml
- name: Get Version
  id: version
  run: |
    VERSION=$(echo ${GITHUB_REF#refs/tags/} | sed 's/^v//')
    echo "VERSION=$VERSION" >> $GITHUB_OUTPUT
```

### 3. 条件执行

```yaml
- name: Deploy
  if: github.ref == 'refs/heads/main'    # 只在 main 分支执行
  run: echo "Deploying..."
```

---

## 🎯 下一步

学习自动打包配置：

👉 **[02-配置自动构建](02-配置自动构建.md)**

---

*最后更新：2026年4月*
