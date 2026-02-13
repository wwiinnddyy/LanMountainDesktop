"use strict";
const electron = require("electron");
const path = require("path");
const utils = require("@electron-toolkit/utils");
const elysia = require("elysia");
const fs = require("fs");
const child_process = require("child_process");
const util = require("util");
const node = require("@elysiajs/node");
const icon = path.join(__dirname, "../../resources/icon.png");
function getStartMenuPaths() {
  const appData = process.env["APPDATA"];
  const programData = process.env["ProgramData"] ?? process.env["PROGRAMDATA"];
  return {
    userProgramsPath: appData ? path.join(appData, "Microsoft", "Windows", "Start Menu", "Programs") : null,
    commonProgramsPath: programData ? path.join(programData, "Microsoft", "Windows", "Start Menu", "Programs") : null
  };
}
function getStartMenuRoots() {
  const { userProgramsPath, commonProgramsPath } = getStartMenuPaths();
  return [userProgramsPath, commonProgramsPath].filter((p) => Boolean(p));
}
const execFileAsync$1 = util.promisify(child_process.execFile);
function getWindowsPowerShellExe() {
  const systemRoot = process.env["SystemRoot"] ?? process.env["WINDIR"] ?? "C:\\Windows";
  return path.join(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
}
function buildPowerShellScript(roots) {
  const rootsJson = JSON.stringify(roots).replace(/'/g, "''");
  return [
    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
    '$ErrorActionPreference = "Stop"',
    "$out = $null",
    "try {",
    `$roots = '${rootsJson}' | ConvertFrom-Json`,
    "$wsh = New-Object -ComObject WScript.Shell",
    "$entries = @()",
    "foreach ($root in $roots) {",
    "  if (-not (Test-Path -LiteralPath $root)) { continue }",
    "  Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {",
    "    $ext = $_.Extension.ToLowerInvariant()",
    '    if ($ext -ne ".lnk" -and $ext -ne ".url" -and $ext -ne ".appref-ms") { return }',
    '    $source = "unknown"',
    '    if ($root -like "*\\\\AppData\\\\Roaming*") { $source = "user" }',
    '    if ($root -like "*\\\\ProgramData*") { $source = "common" }',
    '    $rel = $_.FullName.Substring($root.Length).TrimStart("\\\\")',
    '    if ($ext -eq ".lnk") {',
    "      $sc = $wsh.CreateShortcut($_.FullName)",
    "      $entries += [pscustomobject]@{",
    "        id = $_.FullName",
    "        name = $_.BaseName",
    '        type = "lnk"',
    "        filePath = $_.FullName",
    "        relativePath = $rel",
    "        targetPath = $sc.TargetPath",
    "        arguments = $sc.Arguments",
    "        workingDirectory = $sc.WorkingDirectory",
    "        iconLocation = $sc.IconLocation",
    "        description = $sc.Description",
    "        source = $source",
    "      }",
    "      return",
    "    }",
    '    $t = "appref-ms"',
    '    if ($ext -eq ".url") { $t = "url" }',
    "    $entries += [pscustomobject]@{",
    "      id = $_.FullName",
    "      name = $_.BaseName",
    "      type = $t",
    "      filePath = $_.FullName",
    "      relativePath = $rel",
    "      source = $source",
    "    }",
    "  }",
    "}",
    "$usedStartApps = $false",
    "try {",
    "  $startApps = Get-StartApps | Select-Object Name, AppID",
    "  if ($startApps -ne $null) {",
    "    $usedStartApps = $true",
    "    foreach ($a in $startApps) {",
    "      if ([string]::IsNullOrWhiteSpace($a.AppID)) { continue }",
    "      $entries += [pscustomobject]@{",
    '        id = "appsFolder:" + $a.AppID',
    "        name = $a.Name",
    '        type = "uwp"',
    '        filePath = "shell:AppsFolder\\\\" + $a.AppID',
    '        relativePath = "AppsFolder\\\\" + $a.Name',
    "        appUserModelId = $a.AppID",
    '        source = "appsfolder"',
    "      }",
    "    }",
    "  }",
    "} catch { }",
    "if (-not $usedStartApps) {",
    "  $shell = New-Object -ComObject Shell.Application",
    '  $appsFolder = $shell.NameSpace("shell:AppsFolder")',
    "  if ($appsFolder -ne $null) {",
    "    $appsFolder.Items() | ForEach-Object {",
    "      $aumid = $_.Path",
    "      if ([string]::IsNullOrWhiteSpace($aumid)) { return }",
    "      $name = $_.Name",
    "      $entries += [pscustomobject]@{",
    '        id = "appsFolder:" + $aumid',
    "        name = $name",
    '        type = "uwp"',
    '        filePath = "shell:AppsFolder\\\\" + $aumid',
    '        relativePath = "AppsFolder\\\\" + $name',
    "        appUserModelId = $aumid",
    '        source = "appsfolder"',
    "      }",
    "    }",
    "  }",
    "}",
    "$out = [pscustomobject]@{ ok = $true; entries = $entries }",
    "} catch {",
    "  $out = [pscustomobject]@{ ok = $false; error = ($_ | Out-String); entries = @() }",
    "}",
    "$out | ConvertTo-Json -Depth 6"
  ].join("\n");
}
async function listWindowsStartMenuApps() {
  if (process.platform !== "win32") return [];
  const roots = getStartMenuRoots();
  const script = buildPowerShellScript(roots);
  const powershellExe = getWindowsPowerShellExe();
  const { stdout } = await execFileAsync$1(
    powershellExe,
    ["-NoProfile", "-NonInteractive", "-Sta", "-ExecutionPolicy", "Bypass", "-Command", script],
    { windowsHide: true, maxBuffer: 50 * 1024 * 1024, timeout: 6e4 }
  );
  const trimmed = stdout.trim();
  if (!trimmed) return [];
  const parsed = JSON.parse(trimmed);
  if (typeof parsed === "object" && parsed !== null && "ok" in parsed) {
    const ok = parsed.ok;
    if (ok === false) {
      const error = parsed.error;
      throw new Error(typeof error === "string" ? error : "PowerShellFailed");
    }
  }
  const rawEntries = typeof parsed === "object" && parsed !== null && "entries" in parsed ? parsed.entries : parsed;
  const list = Array.isArray(rawEntries) ? rawEntries : rawEntries ? [rawEntries] : [];
  const isEntry = (value) => {
    if (typeof value !== "object" || value === null) return false;
    const v = value;
    return typeof v.id === "string" && typeof v.name === "string" && typeof v.type === "string" && typeof v.filePath === "string" && typeof v.relativePath === "string";
  };
  const seen = /* @__PURE__ */ new Set();
  const result = [];
  for (const item of list) {
    if (!isEntry(item)) continue;
    if (seen.has(item.id)) continue;
    seen.add(item.id);
    result.push(item);
  }
  result.sort((a, b) => a.name.localeCompare(b.name, "zh-CN"));
  return result;
}
const execFileAsync = util.promisify(child_process.execFile);
function isUnderRoot(filePath, root) {
  const normalizedFile = path.resolve(filePath).toLowerCase();
  const normalizedRoot = path.resolve(root).toLowerCase();
  return normalizedFile === normalizedRoot || normalizedFile.startsWith(normalizedRoot + "\\");
}
async function launchStartMenuEntry(filePath) {
  if (process.platform !== "win32") return;
  if (filePath.startsWith("shell:AppsFolder\\")) {
    await execFileAsync("explorer.exe", [filePath], { windowsHide: true });
    return;
  }
  const roots = getStartMenuRoots();
  const allowed = roots.some((root) => isUnderRoot(filePath, root));
  if (!allowed) {
    throw new Error("PathNotAllowed");
  }
  const result = await electron.shell.openPath(filePath);
  if (result) {
    throw new Error(result);
  }
}
function createEiysiaApp(deps) {
  const iconCache = /* @__PURE__ */ new Map();
  const appsCacheFilePath = path.join(electron.app.getPath("userData"), "apps-cache.json");
  let cachedApps = [];
  let cacheLoadPromise = null;
  let refreshPromise = null;
  const ensureAppsCacheLoaded = async () => {
    if (cacheLoadPromise) return cacheLoadPromise;
    cacheLoadPromise = (async () => {
      try {
        const raw = await fs.promises.readFile(appsCacheFilePath, "utf-8");
        const parsed = JSON.parse(raw);
        const apps = Array.isArray(parsed.apps) ? parsed.apps : [];
        const parsedApps = apps.map((a) => {
          const id = typeof a.id === "string" ? a.id : "";
          const name = typeof a.name === "string" ? a.name : "";
          const filePath = typeof a.filePath === "string" ? a.filePath : "";
          const iconDataUrl = typeof a.iconDataUrl === "string" ? a.iconDataUrl : "";
          if (!id || !name || !filePath || !iconDataUrl) return null;
          return { id, name, filePath, iconDataUrl };
        });
        cachedApps = parsedApps.filter((a) => Boolean(a));
      } catch {
        cachedApps = [];
      }
    })();
    return cacheLoadPromise;
  };
  const persistAppsCache = async (apps) => {
    const payload = JSON.stringify(
      {
        version: 1,
        updatedAt: Date.now(),
        apps
      },
      null,
      2
    );
    await fs.promises.writeFile(appsCacheFilePath, payload, "utf-8");
  };
  const refreshAppsCache = async () => {
    if (refreshPromise) return refreshPromise;
    refreshPromise = (async () => {
      const cachedById = new Map(cachedApps.map((a) => [a.id, a]));
      const entries = await listWindowsStartMenuApps();
      let index = 0;
      const limit = Math.max(4, Math.min(16, entries.length));
      const nextApps = new Array(entries.length);
      const workers = Array.from({ length: limit }, async () => {
        while (true) {
          const i = index;
          index += 1;
          if (i >= entries.length) return;
          const e = entries[i];
          const cached = cachedById.get(e.id);
          if (cached && cached.name === e.name && cached.filePath === e.filePath && cached.iconDataUrl) {
            nextApps[i] = cached;
            continue;
          }
          const iconDataUrl = await getIconDataUrl(e);
          nextApps[i] = { id: e.id, name: e.name, filePath: e.filePath, iconDataUrl };
        }
      });
      await Promise.all(workers);
      cachedApps = nextApps.filter(Boolean);
      await persistAppsCache(cachedApps);
    })().finally(() => {
      refreshPromise = null;
    });
    return refreshPromise;
  };
  const placeholderIconDataUrl = (name, id) => {
    const letter = (name.trim().charAt(0) || "?").toUpperCase();
    let hash = 0;
    for (let i = 0; i < id.length; i += 1) {
      hash = hash * 31 + id.charCodeAt(i) | 0;
    }
    const hue = Math.abs(hash) % 360;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><circle cx="32" cy="32" r="32" fill="hsl(${hue} 70% 45%)"/><text x="32" y="40" text-anchor="middle" font-family="Segoe UI, Arial" font-size="28" font-weight="700" fill="white">${letter.replace(
      /[<>&]/g,
      ""
    )}</text></svg>`;
    return `data:image/svg+xml;base64,${Buffer.from(svg, "utf-8").toString("base64")}`;
  };
  const getIconDataUrl = async (entry) => {
    const cached = iconCache.get(entry.id);
    if (cached) return cached;
    if (entry.type === "uwp" || entry.filePath.startsWith("shell:")) {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id);
      iconCache.set(entry.id, dataUrl);
      return dataUrl;
    }
    if (!electron.app.isReady()) {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id);
      iconCache.set(entry.id, dataUrl);
      return dataUrl;
    }
    try {
      const tryPath = typeof entry.targetPath === "string" && entry.targetPath.trim() ? entry.targetPath : entry.filePath;
      const rawIcon = await electron.app.getFileIcon(tryPath, { size: "large" }).catch(() => electron.app.getFileIcon(tryPath, { size: "normal" }));
      let icon2 = rawIcon;
      if (!icon2.isEmpty()) {
        const { width, height } = icon2.getSize();
        const target = 64;
        if (width > 0 && height > 0 && (width < target || height < target)) {
          icon2 = icon2.resize({ width: target, height: target, quality: "best" });
        }
      }
      const dataUrl = icon2.isEmpty() ? placeholderIconDataUrl(entry.name, entry.id) : icon2.toDataURL();
      iconCache.set(entry.id, dataUrl);
      return dataUrl;
    } catch {
      const dataUrl = placeholderIconDataUrl(entry.name, entry.id);
      iconCache.set(entry.id, dataUrl);
      return dataUrl;
    }
  };
  return new elysia.Elysia().get("/health", () => ({
    ok: true,
    time: Date.now()
  })).onStart(() => {
    void ensureAppsCacheLoaded().then(() => refreshAppsCache());
  }).get("/apps/list", async () => {
    try {
      await ensureAppsCacheLoaded();
      if (cachedApps.length === 0) {
        await refreshAppsCache();
      } else {
        void refreshAppsCache();
      }
      return { apps: cachedApps, error: null };
    } catch (error) {
      const message = error instanceof Error ? error.message : "UnknownError";
      console.error("[apps/list] failed:", message);
      return { apps: [], error: message };
    }
  }).post("/apps/launch", async ({ body }) => {
    const payload = body;
    if (typeof payload.filePath !== "string") {
      return new Response("BadRequest", { status: 400 });
    }
    try {
      await launchStartMenuEntry(payload.filePath);
      return { ok: true };
    } catch (error) {
      const message = error instanceof Error ? error.message : "UnknownError";
      if (message === "PathNotAllowed") return new Response("Forbidden", { status: 403 });
      return new Response("LaunchFailed", { status: 500 });
    }
  }).get("/backend/port", () => ({
    port: deps.getHttpPort()
  })).post("/app/minimize", () => {
    deps.getMainWindow()?.minimize();
    return { ok: true };
  });
}
function registerEiysiaIpc(app) {
  electron.ipcMain.handle(
    "eiysia:request",
    async (_event, payload) => {
      const method = payload.method?.toUpperCase?.() ?? "GET";
      const path2 = payload.path?.startsWith("/") ? payload.path : `/${payload.path ?? ""}`;
      const headers = new Headers(payload.headers ?? {});
      let body;
      if (payload.body !== void 0) {
        if (typeof payload.body === "string" || payload.body instanceof ArrayBuffer || ArrayBuffer.isView(payload.body)) {
          body = payload.body;
        } else {
          body = JSON.stringify(payload.body);
          if (!headers.has("content-type")) {
            headers.set("content-type", "application/json");
          }
        }
      }
      const request = new Request(`http://eiysia.local${path2}`, {
        method,
        headers,
        body
      });
      const response = await app.handle(request);
      const bodyText = await response.text();
      return {
        status: response.status,
        headers: Object.fromEntries(response.headers.entries()),
        bodyText
      };
    }
  );
}
function startEiysiaHttpServer(app) {
  const serverApp = new elysia.Elysia({ adapter: node.node() }).use(app).listen({ hostname: "127.0.0.1", port: 0 });
  let stopped = false;
  const stop = () => {
    if (stopped) return;
    stopped = true;
    try {
      if (!serverApp.server) return;
      const maybe = serverApp;
      if (typeof maybe.stop === "function") maybe.stop();
      else if (typeof maybe.close === "function") maybe.close();
    } catch {
      return;
    }
  };
  return {
    app: serverApp,
    port: serverApp.server?.port ?? null,
    stop
  };
}
electron.app.commandLine.appendSwitch("touch-events", "enabled");
let mainWindow = null;
let eiysiaServerStop = null;
let eiysiaHttpPort = null;
function createWindow() {
  const window = new electron.BrowserWindow({
    show: false,
    autoHideMenuBar: true,
    fullscreen: true,
    ...process.platform === "linux" ? { icon } : {},
    webPreferences: {
      preload: path.join(__dirname, "../preload/index.js"),
      sandbox: false
    }
  });
  mainWindow = window;
  window.on("ready-to-show", () => {
    window.show();
  });
  window.webContents.setWindowOpenHandler((details) => {
    electron.shell.openExternal(details.url);
    return { action: "deny" };
  });
  if (utils.is.dev && process.env["ELECTRON_RENDERER_URL"]) {
    window.loadURL(process.env["ELECTRON_RENDERER_URL"]);
  } else {
    window.loadFile(path.join(__dirname, "../renderer/index.html"));
  }
}
electron.app.whenReady().then(() => {
  utils.electronApp.setAppUserModelId("com.electron");
  electron.app.on("browser-window-created", (_, window) => {
    utils.optimizer.watchWindowShortcuts(window);
  });
  const eiysia = createEiysiaApp({
    getMainWindow: () => mainWindow,
    getHttpPort: () => eiysiaHttpPort
  });
  registerEiysiaIpc(eiysia);
  const server = startEiysiaHttpServer(eiysia);
  eiysiaHttpPort = server.port;
  eiysiaServerStop = server.stop;
  createWindow();
  electron.app.on("activate", function() {
    if (electron.BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});
electron.app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    electron.app.quit();
  }
});
electron.app.on("before-quit", () => {
  eiysiaServerStop?.();
  eiysiaServerStop = null;
  eiysiaHttpPort = null;
});
