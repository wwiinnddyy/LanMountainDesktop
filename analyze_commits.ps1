# 分析当天的 Git 提交并生成 Markdown 报告

$todayStart = [DateTime]::Today
$todayEnd = [DateTime]::Now
$outputDir = "docs\auto_commit_md"

# 创建输出目录（如果不存在）
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "创建目录: $outputDir"
}

# 获取当天的所有提交
Write-Host "正在获取 $todayStart 到 $todayEnd 之间的提交..."
$commits = git log --since="$($todayStart.ToString("yyyy-MM-dd HH:mm:ss"))" --until="$($todayEnd.ToString("yyyy-MM-dd HH:mm:ss"))" --pretty=format:"%H|%an|%ai|%s"

if ([string]::IsNullOrWhiteSpace($commits)) {
    Write-Host "当天没有新的提交。"
    exit 0
}

Write-Host "找到 $($commits.Split([Environment]::NewLine).Count) 个提交"

# 处理每个提交
$commitLines = $commits -split [Environment]::NewLine
foreach ($line in $commitLines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }

    $parts = $line -split '\|', 4
    $hash = $parts[0]
    $author = $parts[1]
    $date = $parts[2]
    $message = $parts[3]

    $shortHash = $hash.Substring(0, 7)
    $dateStr = [DateTime]::Parse($date).ToString("yyyyMMdd")
    $outputFile = Join-Path $outputDir "${dateStr}_${shortHash}.md"

    Write-Host "处理提交: $shortHash - $message"

    # 获取详细的 diff
    $diff = git show --stat --stat-width=120 --stat-name-width=80 $hash
    $fullDiff = git show $hash

    # 构建 Markdown 内容
    $markdown = @"
# Git 提交分析报告

## 基本信息

| 项目 | 内容 |
|------|------|
| **提交哈希** | $hash |
| **短哈希** | $shortHash |
| **作者** | $author |
| **提交时间** | $date |

## 提交信息

$message

## 变更统计

``````
$diff
``````

## 详细变更

``````diff
$fullDiff
``````

## 代码审查要点

> 本部分由系统自动生成，需要人工审查确认。

- 请检查代码变更是否符合项目规范
- 请检查是否有潜在的 bug 或安全问题
- 请检查测试是否覆盖了新代码
- 请检查文档是否需要更新

---
*报告生成时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")*
"@

    # 保存文件
    $markdown | Out-File -FilePath $outputFile -Encoding UTF8
    Write-Host "已保存: $outputFile"
}

Write-Host "`n完成！共生成 $($commitLines.Count) 份报告。"
