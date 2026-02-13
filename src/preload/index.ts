import { contextBridge, ipcRenderer } from 'electron'
import { electronAPI } from '@electron-toolkit/preload'

// Custom APIs for renderer
const api = {
  request: (payload: {
    method: string
    path: string
    headers?: Record<string, string>
    body?: unknown
  }): Promise<{ status: number; headers: Record<string, string>; bodyText: string }> =>
    ipcRenderer.invoke('eiysia:request', payload),
  call: async <T = unknown>(payload: {
    method: string
    path: string
    headers?: Record<string, string>
    body?: unknown
  }): Promise<T> => {
    const response: { status: number; headers: Record<string, string>; bodyText: string } =
      await ipcRenderer.invoke('eiysia:request', payload)

    const contentType = response.headers['content-type'] ?? response.headers['Content-Type']
    if (contentType?.includes('application/json')) {
      return JSON.parse(response.bodyText) as T
    }

    return response.bodyText as T
  }
}

// Use `contextBridge` APIs to expose Electron APIs to
// renderer only if context isolation is enabled, otherwise
// just add to the DOM global.
if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('electron', electronAPI)
    contextBridge.exposeInMainWorld('api', api)
  } catch (error) {
    console.error(error)
  }
} else {
  // @ts-ignore (define in dts)
  window.electron = electronAPI
  // @ts-ignore (define in dts)
  window.api = api
}
