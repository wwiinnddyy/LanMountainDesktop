import { join } from 'path'

export interface StartMenuPaths {
  userProgramsPath: string | null
  commonProgramsPath: string | null
}

export function getStartMenuPaths(): StartMenuPaths {
  const appData = process.env['APPDATA']
  const programData = process.env['ProgramData'] ?? process.env['PROGRAMDATA']

  return {
    userProgramsPath: appData
      ? join(appData, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
      : null,
    commonProgramsPath: programData
      ? join(programData, 'Microsoft', 'Windows', 'Start Menu', 'Programs')
      : null
  }
}

export function getStartMenuRoots(): string[] {
  const { userProgramsPath, commonProgramsPath } = getStartMenuPaths()
  return [userProgramsPath, commonProgramsPath].filter((p): p is string => Boolean(p))
}
