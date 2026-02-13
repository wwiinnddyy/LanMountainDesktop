import { Elysia } from 'elysia'
import { node } from '@elysiajs/node'

export interface EiysiaServerHandle {
  app: unknown
  port: number | null
  stop: () => void
}

export function startEiysiaHttpServer(app: unknown): EiysiaServerHandle {
  const serverApp = new Elysia({ adapter: node() })
    .use(app as never)
    .listen({ hostname: '127.0.0.1', port: 0 })

  let stopped = false
  const stop = (): void => {
    if (stopped) return
    stopped = true

    try {
      if (!serverApp.server) return

      const maybe = serverApp as unknown as {
        stop?: () => Promise<void> | void
        close?: () => void
      }
      if (typeof maybe.stop === 'function') maybe.stop()
      else if (typeof maybe.close === 'function') maybe.close()
    } catch {
      return
    }
  }

  return {
    app: serverApp,
    port: serverApp.server?.port ?? null,
    stop
  }
}
