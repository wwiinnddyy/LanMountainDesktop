"use strict";
const electron = require("electron");
const preload = require("@electron-toolkit/preload");
const api = {
  request: (payload) => electron.ipcRenderer.invoke("eiysia:request", payload),
  call: async (payload) => {
    const response = await electron.ipcRenderer.invoke("eiysia:request", payload);
    const contentType = response.headers["content-type"] ?? response.headers["Content-Type"];
    if (contentType?.includes("application/json")) {
      return JSON.parse(response.bodyText);
    }
    return response.bodyText;
  }
};
if (process.contextIsolated) {
  try {
    electron.contextBridge.exposeInMainWorld("electron", preload.electronAPI);
    electron.contextBridge.exposeInMainWorld("api", api);
  } catch (error) {
    console.error(error);
  }
} else {
  window.electron = preload.electronAPI;
  window.api = api;
}
