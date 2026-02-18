import { app as electronApp, BrowserWindow, shell } from 'electron'
import { Elysia } from 'elysia'
import { execFile } from 'child_process'
import { promises as fs } from 'fs'
import { join } from 'path'
import { promisify } from 'util'
import { launchStartMenuEntry, listWindowsStartMenuApps } from '../app_list'
import { createSqlite } from '../SQLitte'

export interface EiysiaDependencies {
  getMainWindow: () => BrowserWindow | null
  getHttpPort: () => number | null
}

const execFileAsync = promisify(execFile)

export function createEiysiaApp(deps: EiysiaDependencies): {
  handle: (request: Request) => Response | Promise<Response>
} {
  type CachedApp = {
    id: string
    name: string
    filePath: string
    iconDataUrl: string
  }

  const sqliteHandle = createSqlite()

  const iconCache = new Map<string, string>()
  const appsCacheFilePath = join(electronApp.getPath('userData'), 'apps-cache.json')
  let cachedApps: CachedApp[] = []
  let cacheLoadPromise: Promise<void> | null = null
  let refreshPromise: Promise<void> | null = null

  const ensureAppsCacheLoaded = async (): Promise<void> => {
    if (cacheLoadPromise) return cacheLoadPromise
    cacheLoadPromise = (async () => {
      try {
        const raw = await fs.readFile(appsCacheFilePath, 'utf-8')
        const parsed = JSON.parse(raw) as {
          apps?: Array<{ id?: unknown; name?: unknown; filePath?: unknown; iconDataUrl?: unknown }>
        }

        const apps = Array.isArray(parsed.apps) ? parsed.apps : []
        const parsedApps = apps.map((a) => {
          const id = typeof a.id === 'string' ? a.id : ''
          const name = typeof a.name === 'string' ? a.name : ''
          const filePath = typeof a.filePath === 'string' ? a.filePath : ''
          const iconDataUrl = typeof a.iconDataUrl === 'string' ? a.iconDataUrl : ''
          if (!id || !name || !filePath || !iconDataUrl) return null
          return { id, name, filePath, iconDataUrl }
        })

        cachedApps = parsedApps.filter((a): a is CachedApp => Boolean(a))
      } catch {
        cachedApps = []
      }
    })()
    return cacheLoadPromise
  }

  const persistAppsCache = async (apps: CachedApp[]): Promise<void> => {
    const payload = JSON.stringify(
      {
        version: 1,
        updatedAt: Date.now(),
        apps
      },
      null,
      2
    )
    await fs.writeFile(appsCacheFilePath, payload, 'utf-8')
  }

  const refreshAppsCache = async (): Promise<void> => {
    if (refreshPromise) return refreshPromise
    refreshPromise = (async () => {
      const cachedById = new Map(cachedApps.map((a) => [a.id, a]))
      const entries = await listWindowsStartMenuApps()

      let index = 0
      const limit = Math.max(4, Math.min(16, entries.length))
      const nextApps: CachedApp[] = new Array(entries.length)

      const workers = Array.from({ length: limit }, async () => {
        while (true) {
          const i = index
          index += 1
          if (i >= entries.length) return

          const e = entries[i]
          const cached = cachedById.get(e.id)
          if (
            cached &&
            cached.name === e.name &&
            cached.filePath === e.filePath &&
            cached.iconDataUrl
          ) {
            nextApps[i] = cached
            continue
          }

          const iconDataUrl = await getIconDataUrl(e)
          nextApps[i] = { id: e.id, name: e.name, filePath: e.filePath, iconDataUrl }
        }
      })

      await Promise.all(workers)
      cachedApps = nextApps.filter(Boolean)
      await persistAppsCache(cachedApps)
    })().finally(() => {
      refreshPromise = null
    })

    return refreshPromise
  }

  const placeholderIconDataUrl = (name: string, id: string): string => {
    const letter = (name.trim().charAt(0) || '?').toUpperCase()

    let hash = 0
    for (let i = 0; i < id.length; i += 1) {
      hash = (hash * 31 + id.charCodeAt(i)) | 0
    }
    const hue = Math.abs(hash) % 360

    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><circle cx="32" cy="32" r="32" fill="hsl(${hue} 70% 45%)"/><text x="32" y="40" text-anchor="middle" font-family="Segoe UI, Arial" font-size="28" font-weight="700" fill="white">${letter.replace(
      /[<>&]/g,
      ''
    )}</text></svg>`

    return `data:image/svg+xml;base64,${Buffer.from(svg, 'utf-8').toString('base64')}`
  }

  const getIconDataUrl = async (entry: {
    id: string
    name: string
    type: string
    filePath: string
    targetPath?: string | null
  }): Promise<string> => {
    const cached = iconCache.get(entry.id)
    if (cached) return cached

    if (entry.type === 'uwp' || entry.filePath.startsWith('shell:')) {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id)
      iconCache.set(entry.id, dataUrl)
      return dataUrl
    }

    if (!electronApp.isReady()) {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id)
      iconCache.set(entry.id, dataUrl)
      return dataUrl
    }

    try {
      const tryPath =
        typeof entry.targetPath === 'string' && entry.targetPath.trim()
          ? entry.targetPath
          : entry.filePath
      const rawIcon = await electronApp
        .getFileIcon(tryPath, { size: 'large' })
        .catch(() => electronApp.getFileIcon(tryPath, { size: 'normal' }))

      let icon = rawIcon
      if (!icon.isEmpty()) {
        const { width, height } = icon.getSize()
        const target = 64
        if (width > 0 && height > 0 && (width < target || height < target)) {
          icon = icon.resize({ width: target, height: target, quality: 'best' })
        }
      }

      const dataUrl = icon.isEmpty()
        ? placeholderIconDataUrl(entry.name, entry.id)
        : icon.toDataURL()
      iconCache.set(entry.id, dataUrl)
      return dataUrl
    } catch {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id)
      iconCache.set(entry.id, dataUrl)
      return dataUrl
    }
  }

  return new Elysia()
    .get('/db/sqlite/health', () => ({
      ok: true,
      path: sqliteHandle.filePath
    }))
    .get('/health', () => ({
      ok: true,
      time: Date.now()
    }))
    .onStart(() => {
      void ensureAppsCacheLoaded().then(() => refreshAppsCache())
    })
    .get('/apps/list', async () => {
      try {
        await ensureAppsCacheLoaded()
        if (cachedApps.length === 0) {
          await refreshAppsCache()
        } else {
          void refreshAppsCache()
        }

        return { apps: cachedApps, error: null as string | null }
      } catch (error) {
        const message = error instanceof Error ? error.message : 'UnknownError'
        console.error('[apps/list] failed:', message)
        return { apps: [], error: message }
      }
    })
    .post('/apps/launch', async ({ body }) => {
      const payload = body as { filePath?: unknown }
      if (typeof payload.filePath !== 'string') {
        return new Response('BadRequest', { status: 400 })
      }

      try {
        await launchStartMenuEntry(payload.filePath)
        return { ok: true }
      } catch (error) {
        const message = error instanceof Error ? error.message : 'UnknownError'
        if (message === 'PathNotAllowed') return new Response('Forbidden', { status: 403 })
        return new Response('LaunchFailed', { status: 500 })
      }
    })
    .post('/open/external', async ({ body }) => {
      const payload = body as { url?: unknown }
      if (typeof payload.url !== 'string') {
        return new Response('BadRequest', { status: 400 })
      }

      const url = payload.url.trim()
      if (!url) return new Response('BadRequest', { status: 400 })

      const lower = url.toLowerCase()
      if (
        lower.startsWith('javascript:') ||
        lower.startsWith('data:') ||
        lower.startsWith('file:')
      ) {
        return new Response('Forbidden', { status: 403 })
      }

      try {
        if (process.platform === 'win32' && lower.startsWith('shell:')) {
          await execFileAsync('explorer.exe', [url], { windowsHide: true })
          return { ok: true }
        }

        await shell.openExternal(url)
        return { ok: true }
      } catch {
        return new Response('OpenFailed', { status: 500 })
      }
    })
    .get('/backend/port', () => ({
      port: deps.getHttpPort()
    }))
    .post('/app/minimize', () => {
      deps.getMainWindow()?.minimize()
      return { ok: true }
    })
}
