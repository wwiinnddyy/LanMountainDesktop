# 测试 Launcher 在发布版环境下的行为
$ErrorActionPreference = "Stop"

$testDir = "C:\Temp\LanMountainDesktop-Test"
$launcherSource = "C:\Users\USER154971\Documents\GitHub\LanMountainDesktop\LanMountainDesktop.Launcher\bin\Release\net10.0"
$appSource = "C:\Users\USER154971\Documents\GitHub\LanMountainDesktop\LanMountainDesktop\bin\Release\net10.0"

Write-Host "=== Launcher 发布版环境测试 ===" -ForegroundColor Cyan

# 清理并创建测试目录
if (Test-Path $testDir) {
    Remove-Item -Path $testDir -Recurse -Force
}
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
New-Item -ItemType Directory -Path "$testDir\app-1.0.0" -Force | Out-Null

Write-Host "测试目录: $testDir" -ForegroundColor Yellow

# 复制 Launcher 文件
Write-Host "复制 Launcher 文件..." -ForegroundColor Yellow
Copy-Item -Path "$launcherSource\*" -Destination $testDir -Recurse -Force

# 复制主程序文件到 app-1.0.0 目录
Write-Host "复制主程序文件到 app-1.0.0..." -ForegroundColor Yellow
$appFiles = @(
    "LanMountainDesktop.exe",
    "LanMountainDesktop.dll",
    "LanMountainDesktop.deps.json",
    "LanMountainDesktop.runtimeconfig.json"
)
foreach ($file in $appFiles) {
    $sourcePath = "$appSource\$file"
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination "$testDir\app-1.0.0" -Force
        Write-Host "  复制: $file" -ForegroundColor Gray
    } else {
        Write-Host "  跳过: $file (不存在)" -ForegroundColor DarkGray
    }
}

# 创建 .current 标记文件
New-Item -ItemType File -Path "$testDir\app-1.0.0\.current" -Force | Out-Null

# 列出目录结构
Write-Host "`n目录结构:" -ForegroundColor Cyan
Get-ChildItem -Path $testDir -Recurse | Select-Object FullName | Format-Table -AutoSize

# 运行 Launcher
Write-Host "`n运行 Launcher..." -ForegroundColor Green
$launcherPath = "$testDir\LanMountainDesktop.Launcher.exe"

if (Test-Path $launcherPath) {
    Write-Host "启动: $launcherPath" -ForegroundColor Green
    Start-Process -FilePath $launcherPath -WorkingDirectory $testDir -Wait
} else {
    Write-Host "错误: 找不到 Launcher 可执行文件" -ForegroundColor Red
}

Write-Host "`n测试完成" -ForegroundColor Cyan
