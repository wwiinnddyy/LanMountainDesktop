# LanMontainDesktop

一个使用 Electron 打包的桌面应用：前端采用 Vue 3（Renderer），主进程内置 Elysia.js 作为本地后端服务（Main）。

## 技术栈

- Electron + electron-vite（主进程/构建/开发）
- Vue 3 + Vite + TypeScript（渲染进程 UI）
- Elysia.js + @elysiajs/node（主进程内的本地后端 API）

## 架构说明

这个项目不是传统意义上“浏览器前端 + 远程后端”的部署形态，而是：

- 主进程（Electron Main）负责创建窗口，并启动 Elysia.js（HTTP Server 绑定到 127.0.0.1 的随机端口）。
- 预加载（Preload）通过 `ipcRenderer.invoke('eiysia:request', ...)` 把“类 HTTP 请求”转发到主进程里的 Elysia 路由。
- 渲染进程（Vue 3 Renderer）通过 `window.api.call({ method, path, body })` 调用后端接口（例如 `/apps/list`、`/apps/launch`、`/open/external`）。

## 目录结构（关键）

- `src/main/`：Electron 主进程入口（创建窗口、启动 Elysia 服务）
- `src/preload/`：Preload 桥接层（暴露 `window.api`）
- `src/renderer/`：Vue 3 渲染进程（UI 与交互）
- `src/eiysia/`：Elysia.js “后端”路由与启动逻辑

## 推荐 IDE

- VSCode + Volar（Vue Language Features）+ ESLint + Prettier

## 开发与构建

### 安装

```bash
$ pnpm install
```

### 开发

```bash
$ pnpm dev
```

### 构建

```bash
# For windows
$ pnpm build:win

# For macOS
$ pnpm build:mac

# For Linux
$ pnpm build:linux
```
