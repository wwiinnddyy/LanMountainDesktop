export type WindowsStartMenuEntryType = 'lnk' | 'url' | 'appref-ms' | 'uwp' | 'other'

export interface WindowsStartMenuAppEntry {
  id: string
  name: string
  type: WindowsStartMenuEntryType
  filePath: string
  relativePath: string
  appUserModelId?: string | null
  targetPath?: string | null
  arguments?: string | null
  workingDirectory?: string | null
  iconLocation?: string | null
  description?: string | null
  source: 'user' | 'common' | 'appsfolder' | 'unknown'
}
