# CI/CD 打包工作流修复报告

修复日期：2026年3月4日

## 问题概述

GitHub Actions `release.yml` 工作流中的打包步骤存在多个bug，导致无法正常生成发布包。

## 🔴 已发现并修复的问题

### 1. **macOS Info.plist 变量展开问题** (关键)
📍 位置：`release.yml` - macOS Package as DMG 步骤

**问题**：
```bash
cat > "${app_name}.app/Contents/Info.plist" << 'EOF'
  ...
  <string>${version}</string>  # 此处无法展开变量
  ...
EOF
```

使用了 `'EOF'`（带引号），导致heredoc中的shell变量无法展开。

**修复**：
```bash
cat > "${app_name}.app/Contents/Info.plist" << EOF
  ...
  <string>$version</string>  # 现在可以正确展开
  ...
EOF
```

### 2. **Linux DEB 控制文件缩进错误**
📍 位置：`release.yml` - Linux Package as DEB 步骤

**问题**：
```bash
cat > "build-deb/DEBIAN/control" << EOF
  Package: $package_name        # 错误的缩进导致无效的DEB control文件
  Version: $package_version
```

DEB control文件不允许在字段前有缩进。

**修复**：
```bash
cat > "build-deb/DEBIAN/control" << EOF
Package: $package_name          # 移除所有缩进
Version: $package_version
```

### 3. **Windows 打包路径和错误处理缺失**
📍 位置：`release.yml` - Windows Package 步骤

**问题**：
- 使用 `Copy-Item -Path "$source/*"` 可能无法正确处理通配符
- 缺少目录存在性检查
- 缺少打包内容验证

**修复**：
```powershell
# 1. 添加源目录验证
if (-not (Test-Path -Path $source)) {
  Write-Error "Source directory not found: $source"
  exit 1
}

# 2. 改进复制（使用反斜杠）
Copy-Item -Path "$source\*" -Destination $package -Recurse -Force

# 3. 验证打包内容
$itemCount = @(Get-ChildItem $package -Recurse).Count
if ($itemCount -eq 0) {
  Write-Error "Package directory is empty after copy"
  exit 1
}
```

### 4. **Linux DEB 打包缺少错误检查**
📍 位置：`release.yml` - Linux Package as DEB 步骤

**问题**：
- 未验证源目录是否存在
- 未验证复制是否成功
- `dpkg-deb` 命令缺少错误检查

**修复**：
```bash
# 1. 验证源目录
if [ ! -d "$source" ]; then
  echo "Error: Source directory not found: $source"
  exit 1
fi

# 2. 验证复制成功
item_count=$(find build-deb/usr/local/bin -type f 2>/dev/null | wc -l)
if [ "$item_count" -eq 0 ]; then
  echo "Error: DEB package is empty after copy"
  exit 1
fi

# 3. 验证dpkg-deb成功
if dpkg-deb --build "build-deb" "${package_name}_${package_version}_${arch}.deb"; then
  echo "Successfully created..."
else
  echo "Error: Failed to build DEB package"
  exit 1
fi
```

### 5. **macOS DMG 打包缺少错误检查**
📍 位置：`release.yml` - macOS Package as DMG 步骤

**问题**：
- 未验证source目录是否存在
- 未验证app bundle复制是否成功
- `hdiutil` 命令缺少错误检查

**修复**：
```bash
# 1. 验证源目录
if [ ! -d "$source" ]; then
  echo "Error: Source directory not found: $source"
  exit 1
fi

# 2. 验证复制成功
item_count=$(find "${app_name}.app/Contents/MacOS" -type f | wc -l)
if [ "$item_count" -eq 0 ]; then
  echo "Error: App bundle is empty after copy"
  exit 1
fi

# 3. 验证hdiutil成功
if hdiutil create -volname "${app_name}" -srcfolder dmg-temp -ov -format UDZO "${package_name}.dmg" 2>&1; then
  echo "Successfully created: ${package_name}.dmg"
else
  echo "Error: Failed to create DMG"
  exit 1
fi
```

## 📝 修改文件

- `.github/workflows/release.yml`
  - Windows Package 步骤：完整重写，添加验证和错误处理
  - Linux Package as DEB 步骤：修复缩进，添加验证和错误处理
  - macOS Package as DMG 步骤：修复heredoc变量展开，添加验证和错误处理

## ✅ 测试建议

1. **本地测试** (可选)：
   ```bash
   # 手动运行打包步骤以测试逻辑
   ```

2. **GitHub Actions 测试**：
   - 推送一个测试标签：`git tag v1.0.0-test && git push origin v1.0.0-test`
   - 查看Actions日志验证打包步骤是否成功
   - 检查发布页面是否包含所有平台的包

3. **包验证**：
   - Windows: 检查 `.zip` 文件是否包含可执行文件
   - Linux: 检查 `.deb` 文件是否可安装 `dpkg` 验证
   - macOS: 检查 `.dmg` 文件是否包含应用和有效的Info.plist

## 🔧 后续改进建议

1. **添加签名步骤**：
   - Windows: Code签名 (需证书)
   - macOS: 代码签名和公证 (需开发者账户)

2. **添加完整性检查**：
   - SHA256 校验和生成和验证
   - 添加版本信息验证

3. **优化包大小**：
   - 使用 `--self-contained false` 依赖系统运行时
   - 剥离调试符号 (已使用 `-p:DebugType=none`)

4. **改进发布说明**：
   - 添加更详细的更新日志
   - 链接到提交日志和问题跟踪
