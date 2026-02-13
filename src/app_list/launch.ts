import { shell } from 'electron'
import { execFile } from 'child_process'
import { resolve } from 'path'
import { promisify } from 'util'
import { getStartMenuRoots } from './paths'

const execFileAsync = promisify(execFile)

function isUnderRoot(filePath: string, root: string): boolean {
  const normalizedFile = resolve(filePath).toLowerCase()
  const normalizedRoot = resolve(root).toLowerCase()
  return normalizedFile === normalizedRoot || normalizedFile.startsWith(normalizedRoot + '\\')
}

export async function launchStartMenuEntry(filePath: string): Promise<void> {
  if (process.platform !== 'win32') return

  if (filePath.startsWith('shell:AppsFolder\\')) {
    await execFileAsync('explorer.exe', [filePath], { windowsHide: true })
    return
  }

  const roots = getStartMenuRoots()
  const allowed = roots.some((root) => isUnderRoot(filePath, root))
  if (!allowed) {
    throw new Error('PathNotAllowed')
  }

  const result = await shell.openPath(filePath)
  if (result) {
    throw new Error(result)
  }
}
