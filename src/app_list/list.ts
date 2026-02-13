import { execFile } from 'child_process'
import { join } from 'path'
import { promisify } from 'util'
import { getStartMenuRoots } from './paths'
import type { WindowsStartMenuAppEntry } from './types'

const execFileAsync = promisify(execFile)

function getWindowsPowerShellExe(): string {
  const systemRoot = process.env['SystemRoot'] ?? process.env['WINDIR'] ?? 'C:\\Windows'
  return join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe')
}

function buildPowerShellScript(roots: string[]): string {
  const rootsJson = JSON.stringify(roots).replace(/'/g, "''")

  return [
    '[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
    '$ErrorActionPreference = "Stop"',
    '$out = $null',
    'try {',
    `$roots = '${rootsJson}' | ConvertFrom-Json`,
    '$wsh = New-Object -ComObject WScript.Shell',
    '$entries = @()',
    'foreach ($root in $roots) {',
    '  if (-not (Test-Path -LiteralPath $root)) { continue }',
    '  Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {',
    '    $ext = $_.Extension.ToLowerInvariant()',
    '    if ($ext -ne ".lnk" -and $ext -ne ".url" -and $ext -ne ".appref-ms") { return }',
    '    $source = "unknown"',
    '    if ($root -like "*\\\\AppData\\\\Roaming*") { $source = "user" }',
    '    if ($root -like "*\\\\ProgramData*") { $source = "common" }',
    '    $rel = $_.FullName.Substring($root.Length).TrimStart("\\\\")',
    '    if ($ext -eq ".lnk") {',
    '      $sc = $wsh.CreateShortcut($_.FullName)',
    '      $entries += [pscustomobject]@{',
    '        id = $_.FullName',
    '        name = $_.BaseName',
    '        type = "lnk"',
    '        filePath = $_.FullName',
    '        relativePath = $rel',
    '        targetPath = $sc.TargetPath',
    '        arguments = $sc.Arguments',
    '        workingDirectory = $sc.WorkingDirectory',
    '        iconLocation = $sc.IconLocation',
    '        description = $sc.Description',
    '        source = $source',
    '      }',
    '      return',
    '    }',
    '    $t = "appref-ms"',
    '    if ($ext -eq ".url") { $t = "url" }',
    '    $entries += [pscustomobject]@{',
    '      id = $_.FullName',
    '      name = $_.BaseName',
    '      type = $t',
    '      filePath = $_.FullName',
    '      relativePath = $rel',
    '      source = $source',
    '    }',
    '  }',
    '}',
    '$usedStartApps = $false',
    'try {',
    '  $startApps = Get-StartApps | Select-Object Name, AppID',
    '  if ($startApps -ne $null) {',
    '    $usedStartApps = $true',
    '    foreach ($a in $startApps) {',
    '      if ([string]::IsNullOrWhiteSpace($a.AppID)) { continue }',
    '      $entries += [pscustomobject]@{',
    '        id = "appsFolder:" + $a.AppID',
    '        name = $a.Name',
    '        type = "uwp"',
    '        filePath = "shell:AppsFolder\\\\" + $a.AppID',
    '        relativePath = "AppsFolder\\\\" + $a.Name',
    '        appUserModelId = $a.AppID',
    '        source = "appsfolder"',
    '      }',
    '    }',
    '  }',
    '} catch { }',
    'if (-not $usedStartApps) {',
    '  $shell = New-Object -ComObject Shell.Application',
    '  $appsFolder = $shell.NameSpace("shell:AppsFolder")',
    '  if ($appsFolder -ne $null) {',
    '    $appsFolder.Items() | ForEach-Object {',
    '      $aumid = $_.Path',
    '      if ([string]::IsNullOrWhiteSpace($aumid)) { return }',
    '      $name = $_.Name',
    '      $entries += [pscustomobject]@{',
    '        id = "appsFolder:" + $aumid',
    '        name = $name',
    '        type = "uwp"',
    '        filePath = "shell:AppsFolder\\\\" + $aumid',
    '        relativePath = "AppsFolder\\\\" + $name',
    '        appUserModelId = $aumid',
    '        source = "appsfolder"',
    '      }',
    '    }',
    '  }',
    '}',
    '$out = [pscustomobject]@{ ok = $true; entries = $entries }',
    '} catch {',
    '  $out = [pscustomobject]@{ ok = $false; error = ($_ | Out-String); entries = @() }',
    '}',
    '$out | ConvertTo-Json -Depth 6'
  ].join('\n')
}

export async function listWindowsStartMenuApps(): Promise<WindowsStartMenuAppEntry[]> {
  if (process.platform !== 'win32') return []

  const roots = getStartMenuRoots()

  const script = buildPowerShellScript(roots)
  const powershellExe = getWindowsPowerShellExe()
  const { stdout } = await execFileAsync(
    powershellExe,
    ['-NoProfile', '-NonInteractive', '-Sta', '-ExecutionPolicy', 'Bypass', '-Command', script],
    { windowsHide: true, maxBuffer: 50 * 1024 * 1024, timeout: 60_000 }
  )

  const trimmed = stdout.trim()
  if (!trimmed) return []

  const parsed: unknown = JSON.parse(trimmed)

  if (typeof parsed === 'object' && parsed !== null && 'ok' in parsed) {
    const ok = (parsed as { ok?: unknown }).ok
    if (ok === false) {
      const error = (parsed as { error?: unknown }).error
      throw new Error(typeof error === 'string' ? error : 'PowerShellFailed')
    }
  }

  const rawEntries: unknown =
    typeof parsed === 'object' && parsed !== null && 'entries' in parsed
      ? (parsed as { entries?: unknown }).entries
      : parsed

  const list: unknown[] = Array.isArray(rawEntries) ? rawEntries : rawEntries ? [rawEntries] : []

  const isEntry = (value: unknown): value is WindowsStartMenuAppEntry => {
    if (typeof value !== 'object' || value === null) return false
    const v = value as Record<string, unknown>
    return (
      typeof v.id === 'string' &&
      typeof v.name === 'string' &&
      typeof v.type === 'string' &&
      typeof v.filePath === 'string' &&
      typeof v.relativePath === 'string'
    )
  }

  const seen = new Set<string>()
  const result: WindowsStartMenuAppEntry[] = []

  for (const item of list) {
    if (!isEntry(item)) continue
    if (seen.has(item.id)) continue
    seen.add(item.id)
    result.push(item)
  }

  result.sort((a, b) => a.name.localeCompare(b.name, 'zh-CN'))
  return result
}
