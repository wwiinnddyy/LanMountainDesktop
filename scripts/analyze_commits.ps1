# Analyze Git commits from today and generate Markdown reports

param(
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot)
)

Write-Host "Analyzing repository: $RepoPath"

# Create output directory
$outputDir = Join-Path (Join-Path $RepoPath "docs") "auto_commit_md"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Host "Created directory: $outputDir"
} else {
    Write-Host "Output directory: $outputDir"
}

Write-Host ""

# Get today's date range
$today = Get-Date
$todayStart = $today.Date.ToString("yyyy-MM-ddTHH:mm:ss")
$todayEnd = $today.Date.AddDays(1).AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss")

# Get commits from today
$commitsOutput = & git -C $RepoPath log --since="$todayStart" --until="$todayEnd" --pretty=format:"%H|%an|%ae|%ad|%s" --date=iso

if (-not $commitsOutput) {
    Write-Host "No new commits today."
    exit 0
}

$commits = @()
foreach ($line in $commitsOutput -split "`n") {
    if (-not $line) { continue }
    $parts = $line -split '\|', 5
    if ($parts.Count -eq 5) {
        $commits += @{
            Hash = $parts[0]
            AuthorName = $parts[1]
            AuthorEmail = $parts[2]
            Date = $parts[3]
            Message = $parts[4]
        }
    }
}

Write-Host "Found $($commits.Count) commits today."
Write-Host ""

foreach ($commit in $commits) {
    $shortHash = $commit.Hash.Substring(0, 7)
    Write-Host "Processing commit: $shortHash - $($commit.Message)"

    # Get commit details
    $diffStat = & git -C $RepoPath show --stat $commit.Hash
    $diffDetails = & git -C $RepoPath show $commit.Hash

    # Parse statistics
    $filesChanged = @()
    $totalInsertions = 0
    $totalDeletions = 0

    foreach ($line in $diffStat -split "`n") {
        if ($line -match '(\d+) insertion') {
            $totalInsertions += [int]$matches[1]
        }
        if ($line -match '(\d+) deletion') {
            $totalDeletions += [int]$matches[1]
        }
        if ($line -match '^\s*(.*?)\s*\|\s*(\d+)') {
            $filesChanged += @{
                File = $matches[1]
                Lines = [int]$matches[2]
            }
        }
    }

    # Generate filename
    $dateStr = $commit.Date.Split(' ')[0].Replace('-', '')
    $filename = "$dateStr`_$shortHash.md"
    $outputPath = Join-Path $outputDir $filename

    # Generate Markdown content
    $report = @"
# Commit Analysis Report - $shortHash

## Basic Information

| Item | Content |
|------|---------|
| Commit Hash | `` $($commit.Hash) `` |
| Author | $($commit.AuthorName) ($($commit.AuthorEmail)) |
| Commit Time | $($commit.Date) |

## Commit Message

$($commit.Message)

## Change Statistics

| Metric | Value |
|--------|-------|
| Files Changed | $($filesChanged.Count) |
| Lines Added | +$totalInsertions |
| Lines Removed | -$totalDeletions |

## Modified Files

"@

    foreach ($file in $filesChanged) {
        $report += "- $($file.File) ($($file.Lines) lines)`n"
    }

    $report += @"

## Detailed Changes

```diff
$diffDetails
```

## Code Review Checklist

> This section is auto-generated, manual review recommended

- [ ] Check for potential bugs
- [ ] Verify code follows project standards
- [ ] Test new functionality if applicable
- [ ] Check for security issues

---

*Report generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")*
"@

    # Save file
    [System.IO.File]::WriteAllText($outputPath, $report, [System.Text.Encoding]::UTF8)
    Write-Host "  Report saved: $outputPath"
}

Write-Host ""
Write-Host "Done!"
