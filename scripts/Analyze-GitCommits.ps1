<#
.SYNOPSIS
    Git Commit 深度分析工具
    用于解析 Git 对象文件并生成详细的代码变更分析报告
#>

param(
    [string]$RepoPath = "d:\github\LanMountainDesktop",
    [string]$OutputDir = "docs\auto_commit_md"
)

# 添加压缩支持
Add-Type -AssemblyName System.IO.Compression

function Read-GitObject {
    param([string]$RepoPath, [string]$ObjHash)

    if ($ObjHash.Length -lt 4) { return $null }

    $objDir = $ObjHash.Substring(0, 2)
    $objFile = $ObjHash.Substring(2)
    $objPath = Join-Path $RepoPath ".git\objects\$objDir\$objFile"

    if (-not (Test-Path $objPath)) { return $null }

    try {
        $compressedData = [System.IO.File]::ReadAllBytes($objPath)

        # 使用 .NET 解压缩
        $ms = New-Object System.IO.MemoryStream(,$compressedData)
        $deflate = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
        $reader = New-Object System.IO.StreamReader($deflate)
        $content = $reader.ReadToEnd()
        $reader.Close()
        $deflate.Close()
        $ms.Close()

        # 解析对象头
        $nullIdx = $content.IndexOf("`0")
        if ($nullIdx -eq -1) { return $null }

        $header = $content.Substring(0, $nullIdx)
        $body = $content.Substring($nullIdx + 1)
        $objType = $header.Split(' ')[0]

        return @{
            Type = $objType
            Content = $body
            RawContent = [System.Text.Encoding]::UTF8.GetBytes($body)
        }
    }
    catch {
        Write-Host "Error reading object ${ObjHash}: $_" -ForegroundColor Red
        return $null
    }
}

function Parse-Commit {
    param([string]$RepoPath, [string]$CommitHash)

    $obj = Read-GitObject -RepoPath $RepoPath -ObjHash $CommitHash
    if (-not $obj -or $obj.Type -ne 'commit') { return $null }

    $content = $obj.Content
    $lines = $content -split "`n"

    $parent = $null
    $tree = $null
    $author = $null
    $email = $null
    $timestamp = $null
    $timezone = $null
    $messageLines = @()
    $inMessage = $false

    foreach ($line in $lines) {
        if ($inMessage) {
            $messageLines += $line
        }
        elseif ($line -match '^tree (.+)') {
            $tree = $matches[1].Trim()
        }
        elseif ($line -match '^parent (.+)') {
            $parent = $matches[1].Trim()
        }
        elseif ($line -match '^author (.+) <(.+)> (\d+) ([+-]\d+)') {
            $author = $matches[1]
            $email = $matches[2]
            $timestamp = [int]$matches[3]
            $timezone = $matches[4]
        }
        elseif ($line -eq '') {
            $inMessage = $true
        }
    }

    $message = ($messageLines -join "`n").Trim()

    return @{
        Hash = $CommitHash
        Parent = $parent
        Tree = $tree
        Author = $author
        Email = $email
        Timestamp = $timestamp
        Timezone = $timezone
        Message = $message
    }
}

function Parse-Tree {
    param([string]$RepoPath, [string]$TreeHash)

    $obj = Read-GitObject -RepoPath $RepoPath -ObjHash $TreeHash
    if (-not $obj -or $obj.Type -ne 'tree') { return @{} }

    $entries = @{}
    $content = $obj.RawContent
    $idx = 0

    while ($idx -lt $content.Length) {
        # 查找空格
        $spaceIdx = [Array]::IndexOf($content, [byte][char]' ', $idx)
        if ($spaceIdx -eq -1) { break }

        $mode = [System.Text.Encoding]::UTF8.GetString($content[$idx..($spaceIdx-1)])

        # 查找 null
        $nullIdx = [Array]::IndexOf($content, [byte]0, $spaceIdx)
        if ($nullIdx -eq -1) { break }

        $name = [System.Text.Encoding]::UTF8.GetString($content[($spaceIdx+1)..($nullIdx-1)])

        # 读取 20 字节 SHA
        $shaStart = $nullIdx + 1
        $shaEnd = $shaStart + 20
        if ($shaEnd -gt $content.Length) { break }

        $shaBytes = $content[$shaStart..($shaEnd-1)]
        $sha = [BitConverter]::ToString($shaBytes).Replace("-", "").ToLower()

        $entries[$name] = $sha
        $idx = $shaEnd
    }

    return $entries
}

function Get-CommitChanges {
    param([string]$RepoPath, [string]$CommitHash)

    $commit = Parse-Commit -RepoPath $RepoPath -CommitHash $CommitHash
    if (-not $commit) { return @() }

    $currentTree = Parse-Tree -RepoPath $RepoPath -TreeHash $commit.Tree
    $parentTree = @{}

    if ($commit.Parent) {
        $parentCommit = Parse-Commit -RepoPath $RepoPath -CommitHash $commit.Parent
        if ($parentCommit) {
            $parentTree = Parse-Tree -RepoPath $RepoPath -TreeHash $parentCommit.Tree
        }
    }

    $changes = @()
    $stats = @{ Added = 0; Modified = 0; Deleted = 0 }

    $allPaths = ($currentTree.Keys + $parentTree.Keys) | Select-Object -Unique

    foreach ($path in $allPaths) {
        if ($currentTree.ContainsKey($path) -and -not $parentTree.ContainsKey($path)) {
            $changes += @{ Path = $path; Type = 'added' }
            $stats.Added++
        }
        elseif (-not $currentTree.ContainsKey($path) -and $parentTree.ContainsKey($path)) {
            $changes += @{ Path = $path; Type = 'deleted' }
            $stats.Deleted++
        }
        elseif ($currentTree[$path] -ne $parentTree[$path]) {
            $changes += @{ Path = $path; Type = 'modified' }
            $stats.Modified++
        }
    }

    return @{
        Changes = $changes
        Stats = $stats
        Commit = $commit
    }
}

function Assess-Importance {
    param([string]$Message, [array]$Changes, [hashtable]$Stats)

    $msgLower = $Message.ToLower()

    $criticalKeywords = @('fix', 'bug', 'security', 'crash', 'memory leak', 'deadlock')
    $featureKeywords = @('feat', 'feature', 'add', 'implement', 'new')
    $refactorKeywords = @('refactor', 'restructure', 'cleanup', 'optimize')

    foreach ($kw in $criticalKeywords) {
        if ($msgLower -like "*$kw*") { return 'critical' }
    }

    foreach ($kw in $featureKeywords) {
        if ($msgLower -like "*$kw*") { return 'feature' }
    }

    $totalChanges = $Stats.Added + $Stats.Modified + $Stats.Deleted
    if ($totalChanges -gt 20) { return 'major' }

    foreach ($kw in $refactorKeywords) {
        if ($msgLower -like "*$kw*") { return 'refactor' }
    }

    return 'minor'
}

function Get-FileTypeDistribution {
    param([array]$Changes)

    $fileTypes = @{}
    foreach ($change in $Changes) {
        $ext = [System.IO.Path]::GetExtension($change.Path)
        if ([string]::IsNullOrEmpty($ext)) { $ext = 'no_extension' }
        if (-not $fileTypes.ContainsKey($ext)) { $fileTypes[$ext] = 0 }
        $fileTypes[$ext]++
    }
    return $fileTypes
}

function Analyze-Impact {
    param([array]$Changes, [string]$Message)

    $impacts = @()

    # 分析受影响的模块
    $modules = @{}
    foreach ($change in $Changes) {
        $parts = $change.Path -split '/'
        if ($parts.Length -gt 1) {
            if (-not $modules.ContainsKey($parts[0])) { $modules[$parts[0]] = 0 }
            $modules[$parts[0]]++
        }
    }

    if ($modules.Count -gt 0) {
        $moduleList = ($modules.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 5 | ForEach-Object { $_.Key }) -join ', '
        $impacts += "受影响的模块: $moduleList"
    }

    # 分析文件类型
    $fileTypes = Get-FileTypeDistribution -Changes $Changes
    if ($fileTypes.ContainsKey('.cs')) {
        $impacts += "涉及 $($fileTypes['.cs']) 个 C# 文件变更"
    }
    if ($fileTypes.ContainsKey('.axaml') -or $fileTypes.ContainsKey('.xaml')) {
        $impacts += "涉及 UI/XAML 文件变更"
    }
    if ($fileTypes.ContainsKey('.md')) {
        $impacts += "涉及文档更新"
    }

    # 根据提交消息分析
    $msgLower = $Message.ToLower()
    if ($msgLower -like '*fix*') {
        $impacts += "这是一个修复性提交，可能解决现有问题"
    }
    if ($msgLower -like '*feat*' -or $msgLower -like '*feature*') {
        $impacts += "这是一个功能新增提交，扩展了项目能力"
    }
    if ($msgLower -like '*refactor*') {
        $impacts += "这是一个重构提交，改善了代码结构"
    }

    return $impacts
}

function Generate-ReviewPoints {
    param([array]$Changes, [string]$Message)

    $points = @()

    # 检查关键文件
    $criticalPatterns = @('Program.cs', 'App.axaml', 'MainWindow', 'Core', 'Service')
    foreach ($change in $Changes) {
        foreach ($pattern in $criticalPatterns) {
            if ($change.Path -like "*$pattern*") {
                $points += "关键文件变更: $($change.Path) - 需要特别关注"
                break
            }
        }
    }

    # 检查提交消息质量
    if ($Message.Length -lt 10) {
        $points += "提交消息较短，建议提供更详细的变更说明"
    }

    if ($Message.ToLower() -like '*wip*' -or $Message.ToLower() -like '*todo*') {
        $points += "提交包含 WIP/TODO 标记，确认是否已完成"
    }

    # 检查文件删除
    $deleted = $Changes | Where-Object { $_.Type -eq 'deleted' }
    if ($deleted.Count -gt 0) {
        $points += "删除了 $($deleted.Count) 个文件，确认是否有其他代码依赖这些文件"
    }

    return $points
}

function Get-KeySnippets {
    param([string]$RepoPath, [array]$Changes)

    $snippets = @()
    $count = 0

    foreach ($change in $Changes | Select-Object -First 10) {
        if ($change.Type -eq 'deleted') { continue }

        $filePath = Join-Path $RepoPath $change.Path
        if (Test-Path $filePath -PathType Leaf) {
            try {
                $content = Get-Content $filePath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
                if ($content) {
                    $lines = $content -split "`n"
                    $preview = if ($lines.Count -gt 30) { ($lines[0..29] -join "`n") } else { $content }

                    $snippets += @{
                        File = $change.Path
                        Type = $change.Type
                        LinesCount = $lines.Count
                        Preview = $preview.Substring(0, [Math]::Min(2000, $preview.Length))
                    }
                    $count++
                }
            }
            catch {
                # 忽略无法读取的文件
            }
        }
    }

    return $snippets
}

function Generate-MarkdownReport {
    param([hashtable]$Analysis)

    $lines = @()

    # 标题
    $lines += "# Commit 深度分析报告"
    $lines += ""
    $lines += "**提交哈希**: ``$($Analysis.CommitHash)``"
    $lines += "**提交时间**: $($Analysis.Date)"
    $lines += "**作者**: $($Analysis.Author) <$($Analysis.Email)>"
    $lines += "**重要性**: $($Analysis.Importance.ToUpper())"
    $lines += ""

    # 提交消息
    $lines += "## 提交消息"
    $lines += "``````"
    $lines += $Analysis.Message
    $lines += "``````"
    $lines += ""

    # 变更统计
    $lines += "## 变更统计"
    $lines += "- **新增文件**: $($Analysis.Stats.Added)"
    $lines += "- **修改文件**: $($Analysis.Stats.Modified)"
    $lines += "- **删除文件**: $($Analysis.Stats.Deleted)"
    $lines += ""

    # 文件类型分布
    if ($Analysis.FileTypes.Count -gt 0) {
        $lines += "### 文件类型分布"
        $sortedTypes = $Analysis.FileTypes.GetEnumerator() | Sort-Object Value -Descending
        foreach ($ft in $sortedTypes) {
            $lines += "- ``$($ft.Key)``: $($ft.Value) 个文件"
        }
        $lines += ""
    }

    # 变更文件列表
    if ($Analysis.Changes.Count -gt 0) {
        $lines += "## 变更文件列表"
        $lines += "| 文件路径 | 变更类型 |"
        $lines += "|---------|---------|"
        $typeMap = @{ 'added' = '新增'; 'modified' = '修改'; 'deleted' = '删除' }
        foreach ($change in $Analysis.Changes | Select-Object -First 50) {
            $typeStr = if ($typeMap.ContainsKey($change.Type)) { $typeMap[$change.Type] } else { $change.Type }
            $lines += "| ``$($change.Path)`` | $typeStr |"
        }
        $lines += ""
    }

    # 影响分析
    if ($Analysis.ImpactAnalysis.Count -gt 0) {
        $lines += "## 影响分析"
        foreach ($impact in $Analysis.ImpactAnalysis) {
            $lines += "- $impact"
        }
        $lines += ""
    }

    # 代码审查要点
    if ($Analysis.ReviewPoints.Count -gt 0) {
        $lines += "## 代码审查要点"
        foreach ($point in $Analysis.ReviewPoints) {
            $lines += "- ⚠️ $point"
        }
        $lines += ""
    }

    # 关键代码片段
    if ($Analysis.KeySnippets.Count -gt 0) {
        $lines += "## 关键代码片段"
        foreach ($snippet in $Analysis.KeySnippets | Select-Object -First 5) {
            $lines += "### $($snippet.File)"
            $lines += "- **类型**: $($snippet.Type)"
            $lines += "- **行数**: $($snippet.LinesCount)"
            $lines += ""
            $lines += "``````"
            $lines += $snippet.Preview
            $lines += "``````"
            $lines += ""
        }
    }

    return $lines -join "`n"
}

# 主逻辑
Write-Host "Git Commit 深度分析工具" -ForegroundColor Cyan
Write-Host "======================" -ForegroundColor Cyan
Write-Host ""

# 确保输出目录存在
$outputPath = Join-Path $RepoPath $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

# 读取 HEAD 日志
$headLogPath = Join-Path $RepoPath ".git\logs\HEAD"
if (-not (Test-Path $headLogPath)) {
    Write-Host "错误: 找不到 HEAD 日志文件: $headLogPath" -ForegroundColor Red
    exit 1
}

# 解析 HEAD 日志
$commits = @()
$logContent = Get-Content $headLogPath

foreach ($line in $logContent) {
    $line = $line.Trim()
    if ([string]::IsNullOrEmpty($line)) { continue }

    # 解析日志行
    $parts = $line -split "`t"
    if ($parts.Count -lt 2) { continue }

    $metaPart = $parts[0]
    $actionPart = $parts[1]

    $metaTokens = $metaPart -split '\s+'
    if ($metaTokens.Count -lt 5) { continue }

    $newHash = $metaTokens[1]

    # 只处理 commit 操作
    if ($actionPart -match 'commit' -or $actionPart -match '^commit:') {
        $message = $actionPart -replace '^commit:\s*', ''
        $commits += @{
            Hash = $newHash
            Message = $message
        }
    }
}

Write-Host "找到 $($commits.Count) 个 commit" -ForegroundColor Green
Write-Host ""

# 分析每个 commit
$processed = 0
$success = 0

foreach ($commitInfo in $commits) {
    $commitHash = $commitInfo.Hash
    $shortHash = $commitHash.Substring(0, 7)
    $processed++

    Write-Host "[$processed/$($commits.Count)] 分析 commit: $shortHash - $($commitInfo.Message.Substring(0, [Math]::Min(50, $commitInfo.Message.Length)))" -NoNewline

    try {
        # 获取变更
        $changeInfo = Get-CommitChanges -RepoPath $RepoPath -CommitHash $commitHash
        if (-not $changeInfo) {
            Write-Host " [跳过]" -ForegroundColor Yellow
            continue
        }

        $commit = $changeInfo.Commit
        $changes = $changeInfo.Changes
        $stats = $changeInfo.Stats

        # 分析
        $importance = Assess-Importance -Message $commit.Message -Changes $changes -Stats $stats
        $fileTypes = Get-FileTypeDistribution -Changes $changes
        $impactAnalysis = Analyze-Impact -Changes $changes -Message $commit.Message
        $reviewPoints = Generate-ReviewPoints -Changes $changes -Message $commit.Message
        $keySnippets = Get-KeySnippets -RepoPath $RepoPath -Changes $changes

        # 构建分析结果
        $analysis = @{
            CommitHash = $commitHash
            Message = $commit.Message
            Author = $commit.Author
            Email = $commit.Email
            Timestamp = $commit.Timestamp
            Date = (Get-Date -Date ([DateTime]::UnixEpoch.AddSeconds($commit.Timestamp)) -Format 'yyyy-MM-dd HH:mm:ss')
            Stats = $stats
            Changes = $changes
            FileTypes = $fileTypes
            Importance = $importance
            ImpactAnalysis = $impactAnalysis
            ReviewPoints = $reviewPoints
            KeySnippets = $keySnippets
        }

        # 生成报告
        $report = Generate-MarkdownReport -Analysis $analysis

        # 保存报告
        $dateStr = Get-Date -Date ([DateTime]::UnixEpoch.AddSeconds($commit.Timestamp)) -Format 'yyyyMMdd'
        $filename = "${dateStr}_${shortHash}_deep_analysis.md"
        $outputFile = Join-Path $outputPath $filename

        $report | Out-File -FilePath $outputFile -Encoding UTF8

        Write-Host " [已保存]" -ForegroundColor Green
        $success++
    }
    catch {
        Write-Host " [错误: $_]" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "分析完成! 成功处理 $success / $processed 个 commit" -ForegroundColor Cyan
