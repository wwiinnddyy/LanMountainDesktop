import { app as electronApp } from 'electron'
import { join } from 'path'
import Database from 'better-sqlite3'

export type SqliteDb = {
  pragma: (sql: string) => unknown
  exec: (sql: string) => unknown
  close: () => void
}

export type SqliteHandle = {
  db: SqliteDb
  filePath: string
  close: () => void
}

export function createSqlite(): SqliteHandle {
  const filePath = join(electronApp.getPath('userData'), 'lanmountain.sqlite3')
  const db = new Database(filePath) as unknown as SqliteDb

  db.pragma('journal_mode = WAL')
  db.pragma('foreign_keys = ON')
  db.exec(`
    CREATE TABLE IF NOT EXISTS kv (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL,
      updatedAt INTEGER NOT NULL
    );
  `)

  const close = (): void => {
    try {
      db.close()
    } catch {
      return
    }
  }

  electronApp.once('before-quit', close)

  return { db, filePath, close }
}
