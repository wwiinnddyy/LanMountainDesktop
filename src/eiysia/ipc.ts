import { ipcMain } from 'electron'

export interface EiysiaIpcRequest {
  method: string
  path: string
  headers?: Record<string, string>
  body?: unknown
}

export interface EiysiaIpcResponse {
  status: number
  headers: Record<string, string>
  bodyText: string
}

export function registerEiysiaIpc(app: {
  handle: (request: Request) => Response | Promise<Response>
}): void {
  ipcMain.handle(
    'eiysia:request',
    async (_event, payload: EiysiaIpcRequest): Promise<EiysiaIpcResponse> => {
      const method = payload.method?.toUpperCase?.() ?? 'GET'
      const path = payload.path?.startsWith('/') ? payload.path : `/${payload.path ?? ''}`

      const headers = new Headers(payload.headers ?? {})
      let body: BodyInit | undefined

      if (payload.body !== undefined) {
        if (
          typeof payload.body === 'string' ||
          payload.body instanceof ArrayBuffer ||
          ArrayBuffer.isView(payload.body)
        ) {
          body = payload.body as BodyInit
        } else {
          body = JSON.stringify(payload.body)
          if (!headers.has('content-type')) {
            headers.set('content-type', 'application/json')
          }
        }
      }

      const request = new Request(`http://eiysia.local${path}`, {
        method,
        headers,
        body
      })

      const response = await app.handle(request)
      const bodyText = await response.text()

      return {
        status: response.status,
        headers: Object.fromEntries(response.headers.entries()),
        bodyText
      }
    }
  )
}
