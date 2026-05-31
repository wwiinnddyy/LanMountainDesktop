#!/usr/bin/env pwsh
$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

git --no-pager log --since="2026-05-31 00:00:00" --until="2026-05-31 23:59:59" --format="%H|%an|%ae|%ai|%s" --no-merges
