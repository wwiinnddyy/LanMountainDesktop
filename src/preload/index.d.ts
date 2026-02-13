import { ElectronAPI } from '@electron-toolkit/preload'

export interface AppApi {
  request: (payload: {
    method: string
    path: string
    headers?: Record<string, string>
    body?: unknown
  }) => Promise<{ status: number; headers: Record<string, string>; bodyText: string }>
  call: <T = unknown>(payload: {
    method: string
    path: string
    headers?: Record<string, string>
    body?: unknown
  }) => Promise<T>
}

declare global {
  interface Window {
    electron: ElectronAPI
    api: AppApi
  }
}
