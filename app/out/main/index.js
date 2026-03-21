"use strict";
const electron = require("electron");
const path = require("path");
const fs = require("fs");
const child_process = require("child_process");
const events = require("events");
const readline = require("readline");
const is = {
  dev: !electron.app.isPackaged
};
const platform = {
  isWindows: process.platform === "win32",
  isMacOS: process.platform === "darwin",
  isLinux: process.platform === "linux"
};
const electronApp = {
  setAppUserModelId(id) {
    if (platform.isWindows)
      electron.app.setAppUserModelId(is.dev ? process.execPath : id);
  },
  setAutoLaunch(auto) {
    if (platform.isLinux)
      return false;
    const isOpenAtLogin = () => {
      return electron.app.getLoginItemSettings().openAtLogin;
    };
    if (isOpenAtLogin() !== auto) {
      electron.app.setLoginItemSettings({ openAtLogin: auto });
      return isOpenAtLogin() === auto;
    } else {
      return true;
    }
  },
  skipProxy() {
    return electron.session.defaultSession.setProxy({ mode: "direct" });
  }
};
const optimizer = {
  watchWindowShortcuts(window, shortcutOptions) {
    if (!window)
      return;
    const { webContents } = window;
    const { escToCloseWindow = false, zoom = false } = shortcutOptions || {};
    webContents.on("before-input-event", (event, input) => {
      if (input.type === "keyDown") {
        if (!is.dev) {
          if (input.code === "KeyR" && (input.control || input.meta))
            event.preventDefault();
          if (input.code === "KeyI" && (input.alt && input.meta || input.control && input.shift)) {
            event.preventDefault();
          }
        } else {
          if (input.code === "F12") {
            if (webContents.isDevToolsOpened()) {
              webContents.closeDevTools();
            } else {
              webContents.openDevTools({ mode: "undocked" });
              console.log("Open dev tool...");
            }
          }
        }
        if (escToCloseWindow) {
          if (input.code === "Escape" && input.key !== "Process") {
            window.close();
            event.preventDefault();
          }
        }
        if (!zoom) {
          if (input.code === "Minus" && (input.control || input.meta))
            event.preventDefault();
          if (input.code === "Equal" && input.shift && (input.control || input.meta))
            event.preventDefault();
        }
      }
    });
  },
  registerFramelessWindowIpc() {
    electron.ipcMain.on("win:invoke", (event, action) => {
      const win = electron.BrowserWindow.fromWebContents(event.sender);
      if (win) {
        if (action === "show") {
          win.show();
        } else if (action === "showInactive") {
          win.showInactive();
        } else if (action === "min") {
          win.minimize();
        } else if (action === "max") {
          const isMaximized = win.isMaximized();
          if (isMaximized) {
            win.unmaximize();
          } else {
            win.maximize();
          }
        } else if (action === "close") {
          win.close();
        }
      }
    });
  }
};
const IMAGE_EXTENSIONS = /* @__PURE__ */ new Set([".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"]);
function scanDirectory(dirPath, basePath) {
  const name = path.basename(dirPath);
  const assets = [];
  const subcategories = [];
  const entries = fs.readdirSync(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      const sub = scanDirectory(fullPath, basePath);
      if (sub.assets.length > 0 || sub.subcategories.length > 0) {
        subcategories.push(sub);
      }
    } else if (entry.isFile()) {
      const ext = path.extname(entry.name).toLowerCase();
      if (IMAGE_EXTENSIONS.has(ext)) {
        assets.push({
          name: path.basename(entry.name, ext),
          filename: entry.name,
          path: fullPath,
          relativePath: path.relative(basePath, fullPath),
          category: name,
          extension: ext
        });
      }
    }
  }
  assets.sort((a, b) => a.name.localeCompare(b.name));
  subcategories.sort((a, b) => a.name.localeCompare(b.name));
  return { name, path: dirPath, assets, subcategories };
}
function scanAssets(assetsDir) {
  const root = scanDirectory(assetsDir, assetsDir);
  let totalAssets = 0;
  function countAssets(cat) {
    totalAssets += cat.assets.length;
    for (const sub of cat.subcategories) {
      countAssets(sub);
    }
  }
  countAssets(root);
  const fortniteFolder = {
    name: "Fortnite Assets",
    path: assetsDir,
    assets: root.assets,
    subcategories: root.subcategories
  };
  return {
    categories: [fortniteFolder],
    totalAssets
  };
}
const IPC_CHANNELS = {
  SCAN_ASSETS: "assets:scan",
  GET_ASSET_DATA: "assets:getData",
  IMPORT_FILES: "assets:importFiles",
  IMPORT_TO_ASSETS: "assets:importToAssets",
  CREATE_ASSET_FOLDER: "assets:createFolder",
  DELETE_ASSET: "assets:delete",
  EXPORT_PNG: "export:png",
  EXPORT_BATCH: "export:batch",
  SELECT_DIRECTORY: "file:selectDirectory",
  GET_FONTS: "fonts:list",
  GET_FONT_DATA: "fonts:getData",
  IMPORT_WIDGET_SPEC: "widget:importSpec",
  EXPORT_WIDGET_SPEC: "widget:exportSpec",
  // FortniteForge .NET sidecar bridge
  FORGE_PING: "forge:ping",
  FORGE_VALIDATE_SPEC: "forge:validateSpec",
  FORGE_BUILD_UASSET: "forge:buildUasset",
  FORGE_GENERATE_VERSE: "forge:generateVerse",
  // Project management
  FORGE_STATUS: "forge:status",
  FORGE_LIST_PROJECTS: "forge:listProjects",
  FORGE_ADD_PROJECT: "forge:addProject",
  FORGE_REMOVE_PROJECT: "forge:removeProject",
  FORGE_ACTIVATE_PROJECT: "forge:activateProject",
  FORGE_SCAN_PROJECTS: "forge:scanProjects",
  FORGE_LIST_LEVELS: "forge:listLevels",
  FORGE_AUDIT: "forge:audit",
  // Browse & inspect
  FORGE_BROWSE_CONTENT: "forge:browseContent",
  FORGE_INSPECT_ASSET: "forge:inspectAsset",
  FORGE_LIST_DEVICES: "forge:listDevices",
  FORGE_INSPECT_DEVICE: "forge:inspectDevice",
  FORGE_LIST_USER_ASSETS: "forge:listUserAssets",
  FORGE_LIST_EPIC_ASSETS: "forge:listEpicAssets",
  FORGE_READ_VERSE: "forge:readVerse",
  FORGE_LIST_STAGED: "forge:listStaged",
  FORGE_APPLY_STAGED: "forge:applyStaged",
  FORGE_DISCARD_STAGED: "forge:discardStaged",
  // Library management (reference collections — NOT projects)
  FORGE_LIST_LIBRARIES: "forge:listLibraries",
  FORGE_ADD_LIBRARY: "forge:addLibrary",
  FORGE_REMOVE_LIBRARY: "forge:removeLibrary",
  FORGE_ACTIVATE_LIBRARY: "forge:activateLibrary",
  FORGE_INDEX_LIBRARY: "forge:indexLibrary",
  FORGE_GET_LIBRARY_VERSE_FILES: "forge:getLibraryVerseFiles",
  FORGE_GET_LIBRARY_ASSETS_BY_TYPE: "forge:getLibraryAssetsByType",
  FORGE_BROWSE_LIBRARY_DIR: "forge:browseLibraryDir",
  FORGE_SEARCH_LIBRARY_INDEX: "forge:searchLibraryIndex",
  // General file reading (for docs, verse-book, etc.)
  FORGE_READ_TEXT_FILE: "forge:readTextFile",
  FORGE_LIST_DIRECTORY: "forge:listDirectory"
};
let cachedAssetIndex = null;
function registerIpcHandlers(assetsDir, fontsDir) {
  electron.ipcMain.handle(IPC_CHANNELS.SCAN_ASSETS, () => {
    cachedAssetIndex = scanAssets(assetsDir);
    return cachedAssetIndex;
  });
  electron.ipcMain.handle(IPC_CHANNELS.GET_ASSET_DATA, (_event, filePath) => {
    try {
      const data = fs.readFileSync(filePath);
      const ext = path.extname(filePath).toLowerCase();
      const mime = ext === ".png" ? "image/png" : ext === ".jpg" || ext === ".jpeg" ? "image/jpeg" : "image/png";
      return `data:${mime};base64,${data.toString("base64")}`;
    } catch {
      return null;
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.GET_FONTS, () => {
    try {
      if (!fs.existsSync(fontsDir)) return [];
      const files = fs.readdirSync(fontsDir);
      return files.filter((f) => {
        const ext = path.extname(f).toLowerCase();
        return ext === ".ttf" || ext === ".otf" || ext === ".woff" || ext === ".woff2";
      });
    } catch {
      return [];
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.GET_FONT_DATA, (_event, fontFilename) => {
    try {
      const fontPath = path.join(fontsDir, fontFilename);
      const data = fs.readFileSync(fontPath);
      const ext = path.extname(fontFilename).toLowerCase();
      let mime = "font/ttf";
      if (ext === ".otf") mime = "font/otf";
      else if (ext === ".woff") mime = "font/woff";
      else if (ext === ".woff2") mime = "font/woff2";
      return { data: `data:${mime};base64,${data.toString("base64")}`, filename: fontFilename };
    } catch {
      return null;
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.IMPORT_FILES, async () => {
    const win = electron.BrowserWindow.getFocusedWindow();
    if (!win) return [];
    const result = await electron.dialog.showOpenDialog(win, {
      properties: ["openFile", "multiSelections"],
      filters: [
        { name: "Images", extensions: ["png", "jpg", "jpeg", "webp", "gif", "svg"] }
      ]
    });
    if (result.canceled || result.filePaths.length === 0) return [];
    return result.filePaths.map((filePath) => {
      const data = fs.readFileSync(filePath);
      const ext = path.extname(filePath).toLowerCase();
      const mime = ext === ".png" ? "image/png" : ext === ".svg" ? "image/svg+xml" : ext === ".webp" ? "image/webp" : ext === ".gif" ? "image/gif" : "image/jpeg";
      return {
        path: filePath,
        name: path.basename(filePath, ext),
        dataUrl: `data:${mime};base64,${data.toString("base64")}`
      };
    });
  });
  electron.ipcMain.handle(IPC_CHANNELS.EXPORT_PNG, async (_event, dataUrl, defaultName) => {
    const win = electron.BrowserWindow.getFocusedWindow();
    if (!win) return null;
    const result = await electron.dialog.showSaveDialog(win, {
      defaultPath: defaultName,
      filters: [{ name: "PNG Image", extensions: ["png"] }]
    });
    if (result.canceled || !result.filePath) return null;
    const base64Data = dataUrl.replace(/^data:image\/png;base64,/, "");
    fs.writeFileSync(result.filePath, Buffer.from(base64Data, "base64"));
    return result.filePath;
  });
  electron.ipcMain.handle(IPC_CHANNELS.SELECT_DIRECTORY, async () => {
    const win = electron.BrowserWindow.getFocusedWindow();
    if (!win) return null;
    const result = await electron.dialog.showOpenDialog(win, {
      properties: ["openDirectory", "createDirectory"]
    });
    if (result.canceled || result.filePaths.length === 0) return null;
    return result.filePaths[0];
  });
  electron.ipcMain.handle(
    IPC_CHANNELS.EXPORT_BATCH,
    async (_event, items, outputDir) => {
      if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
      }
      const results = [];
      for (const item of items) {
        const base64Data = item.dataUrl.replace(/^data:image\/png;base64,/, "");
        const outputPath = path.join(outputDir, item.filename);
        fs.writeFileSync(outputPath, Buffer.from(base64Data, "base64"));
        results.push(outputPath);
      }
      return results;
    }
  );
  electron.ipcMain.handle(
    IPC_CHANNELS.IMPORT_TO_ASSETS,
    async (_event, targetFolder) => {
      const win = electron.BrowserWindow.getFocusedWindow();
      if (!win) return { success: false, imported: [] };
      const result = await electron.dialog.showOpenDialog(win, {
        properties: ["openFile", "multiSelections"],
        filters: [
          { name: "Images", extensions: ["png", "jpg", "jpeg", "webp", "gif", "svg"] }
        ]
      });
      if (result.canceled || result.filePaths.length === 0) {
        return { success: false, imported: [] };
      }
      const targetDir = path.resolve(assetsDir, targetFolder);
      if (!targetDir.startsWith(path.resolve(assetsDir))) {
        return { success: false, imported: [], error: "Invalid target folder" };
      }
      if (!fs.existsSync(targetDir)) {
        fs.mkdirSync(targetDir, { recursive: true });
      }
      const imported = [];
      for (const srcPath of result.filePaths) {
        const filename = path.basename(srcPath);
        let destPath = path.join(targetDir, filename);
        if (fs.existsSync(destPath)) {
          const ext = path.extname(filename);
          const name = path.basename(filename, ext);
          let counter = 1;
          while (fs.existsSync(destPath)) {
            destPath = path.join(targetDir, `${name} (${counter})${ext}`);
            counter++;
          }
        }
        fs.copyFileSync(srcPath, destPath);
        imported.push(destPath);
      }
      cachedAssetIndex = scanAssets(assetsDir);
      return { success: true, imported, index: cachedAssetIndex };
    }
  );
  electron.ipcMain.handle(
    IPC_CHANNELS.CREATE_ASSET_FOLDER,
    (_event, folderName, parentPath) => {
      const parentDir = path.resolve(assetsDir, parentPath);
      if (!parentDir.startsWith(path.resolve(assetsDir))) {
        return { success: false, error: "Invalid parent folder" };
      }
      const newFolderPath = path.join(parentDir, folderName);
      if (fs.existsSync(newFolderPath)) {
        return { success: false, error: "Folder already exists" };
      }
      fs.mkdirSync(newFolderPath, { recursive: true });
      cachedAssetIndex = scanAssets(assetsDir);
      return { success: true, path: newFolderPath, index: cachedAssetIndex };
    }
  );
  electron.ipcMain.handle(
    IPC_CHANNELS.DELETE_ASSET,
    (_event, filePath) => {
      const resolved = path.resolve(filePath);
      if (!resolved.startsWith(path.resolve(assetsDir))) {
        return { success: false, error: "File is not within assets directory" };
      }
      if (!fs.existsSync(resolved)) {
        return { success: false, error: "File not found" };
      }
      fs.unlinkSync(resolved);
      cachedAssetIndex = scanAssets(assetsDir);
      return { success: true, index: cachedAssetIndex };
    }
  );
  electron.ipcMain.handle(IPC_CHANNELS.IMPORT_WIDGET_SPEC, async () => {
    const win = electron.BrowserWindow.getFocusedWindow();
    if (!win) return { success: false };
    const result = await electron.dialog.showOpenDialog(win, {
      properties: ["openFile"],
      filters: [{ name: "Widget Spec", extensions: ["json"] }]
    });
    if (result.canceled || result.filePaths.length === 0) {
      return { success: false };
    }
    try {
      const content = fs.readFileSync(result.filePaths[0], "utf-8");
      const spec = JSON.parse(content);
      if (spec.$schema !== "widget-spec-v1") {
        return { success: false, error: "Not a valid widget-spec-v1 file" };
      }
      return { success: true, spec, path: result.filePaths[0] };
    } catch (err) {
      return { success: false, error: `Failed to parse: ${err}` };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.EXPORT_WIDGET_SPEC, async (_event, specJson) => {
    const win = electron.BrowserWindow.getFocusedWindow();
    if (!win) return { success: false };
    const result = await electron.dialog.showSaveDialog(win, {
      filters: [{ name: "Widget Spec", extensions: ["json"] }],
      defaultPath: "widget-spec.json"
    });
    if (result.canceled || !result.filePath) {
      return { success: false };
    }
    try {
      fs.writeFileSync(result.filePath, specJson, "utf-8");
      return { success: true, path: result.filePath };
    } catch (err) {
      return { success: false, error: `Failed to write: ${err}` };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_READ_TEXT_FILE, (_event, filePath) => {
    try {
      const resolved = path.resolve(filePath);
      if (!fs.existsSync(resolved)) {
        return { error: `File not found: ${filePath}` };
      }
      const content = fs.readFileSync(resolved, "utf-8");
      const name = path.basename(resolved);
      return { content, name };
    } catch (err) {
      return { error: `Failed to read file: ${err}` };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_DIRECTORY, (_event, dirPath) => {
    try {
      const resolved = path.resolve(dirPath);
      if (!fs.existsSync(resolved)) {
        return { error: `Directory not found: ${dirPath}`, entries: [] };
      }
      const entries = fs.readdirSync(resolved).map((name) => {
        const fullPath = path.join(resolved, name);
        try {
          const stat = fs.statSync(fullPath);
          return {
            name,
            path: fullPath,
            isDirectory: stat.isDirectory(),
            size: stat.size
          };
        } catch {
          return { name, path: fullPath, isDirectory: false, size: 0 };
        }
      });
      return { entries };
    } catch (err) {
      return { error: `Failed to list directory: ${err}`, entries: [] };
    }
  });
}
class ForgeBridge extends events.EventEmitter {
  constructor() {
    super();
    this.process = null;
    this.readline = null;
    this.pending = /* @__PURE__ */ new Map();
    this.requestId = 0;
    this.ready = false;
    this.readyPromise = new Promise((resolve) => {
      this.resolveReady = resolve;
    });
  }
  /** Spawn the .NET sidecar process. Pre-builds first, then runs with --no-build to keep stdout clean. */
  start() {
    const repoRoot = is.dev ? path.join(electron.app.getAppPath(), "..") : path.join(electron.app.getPath("exe"), "..");
    const cliProject = path.join(repoRoot, "src", "FortniteForge.CLI");
    console.log("[forge-bridge] Building sidecar...");
    const build = child_process.spawn("dotnet", ["build", cliProject, "-v", "q"], {
      stdio: ["ignore", "pipe", "pipe"],
      cwd: repoRoot
    });
    build.on("close", (code) => {
      if (code !== 0) {
        console.error("[forge-bridge] Build failed with code:", code);
        this.emit("error", new Error("Sidecar build failed"));
        return;
      }
      console.log("[forge-bridge] Build succeeded, starting sidecar...");
      this.process = child_process.spawn("dotnet", ["run", "--project", cliProject, "--no-build", "--", "sidecar"], {
        stdio: ["pipe", "pipe", "pipe"],
        cwd: repoRoot
      });
      this.readline = readline.createInterface({ input: this.process.stdout });
      this.readline.on("line", (line) => this.handleLine(line));
      this.process.stderr?.on("data", (data) => {
        console.error("[forge]", data.toString().trim());
      });
      this.process.on("exit", (exitCode) => {
        console.log("[forge-bridge] Sidecar exited with code:", exitCode);
        this.ready = false;
        this.emit("exit", exitCode);
        for (const [, { reject }] of this.pending) {
          reject(new Error(`Sidecar exited with code ${exitCode}`));
        }
        this.pending.clear();
      });
      this.process.on("error", (err) => {
        console.error("[forge-bridge] Failed to start sidecar:", err.message);
        this.emit("error", err);
      });
      setTimeout(() => {
        if (!this.ready) {
          console.error("[forge-bridge] Sidecar ready timeout — resolving anyway");
          this.ready = true;
          this.resolveReady();
        }
      }, 15e3);
    });
    build.stderr?.on("data", (data) => {
      const msg = data.toString().trim();
      if (msg) console.error("[forge-build]", msg);
    });
  }
  /** Wait for the sidecar to be ready, then send a request. */
  async call(method, params = {}) {
    await this.readyPromise;
    if (!this.process?.stdin?.writable) {
      throw new Error("Sidecar not running");
    }
    const id = `req-${++this.requestId}`;
    return new Promise((resolve, reject) => {
      this.pending.set(id, {
        resolve,
        reject
      });
      const line = JSON.stringify({ id, method, params });
      this.process.stdin.write(line + "\n");
    });
  }
  /** Check if the sidecar is connected. */
  isReady() {
    return this.ready;
  }
  /** Stop the sidecar process. */
  stop() {
    this.process?.stdin?.end();
    this.process?.kill();
    this.process = null;
    this.ready = false;
  }
  handleLine(line) {
    try {
      const response = JSON.parse(line);
      if (response.id === "ready") {
        console.log("[forge-bridge] Sidecar ready");
        this.ready = true;
        this.resolveReady();
        this.emit("ready");
        return;
      }
      const handler = this.pending.get(response.id);
      if (!handler) return;
      this.pending.delete(response.id);
      if (response.error) {
        const err = new Error(response.error.message);
        err.code = response.error.code;
        err.details = response.error.details;
        handler.reject(err);
      } else {
        handler.resolve(response.result);
      }
    } catch {
    }
  }
}
function registerForgeHandlers(bridge) {
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_PING, async () => {
    try {
      return await bridge.call("ping");
    } catch {
      return { error: "Sidecar not running" };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_VALIDATE_SPEC, async (_event, specJson) => {
    return bridge.call("validate-spec", { spec: JSON.parse(specJson) });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_BUILD_UASSET, async (_event, specJson, outputDir, variables) => {
    return bridge.call("build-uasset", {
      spec: JSON.parse(specJson),
      outputDir,
      ...variables ? { variables } : {}
    });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_GENERATE_VERSE, async (_event, specJson) => {
    return bridge.call("generate-verse", { spec: JSON.parse(specJson) });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_STATUS, async () => {
    try {
      return await bridge.call("status");
    } catch {
      return { isConfigured: false, projectName: "No Project", mode: "None", assetCount: 0, verseCount: 0 };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_PROJECTS, async () => {
    try {
      return await bridge.call("list-projects");
    } catch {
      return { activeProjectId: null, projects: [] };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_ADD_PROJECT, async (_event, path2, type) => {
    return bridge.call("add-project", { path: path2, type });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_REMOVE_PROJECT, async (_event, id) => {
    return bridge.call("remove-project", { id });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_ACTIVATE_PROJECT, async (_event, id) => {
    return bridge.call("activate-project", { id });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_SCAN_PROJECTS, async (_event, path2) => {
    return bridge.call("scan-projects", { path: path2 });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_LEVELS, async () => {
    return bridge.call("list-levels");
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_AUDIT, async (_event, level) => {
    try {
      return await bridge.call("audit", level ? { level } : {});
    } catch {
      return { status: "Error", findings: [], error: { message: "No active project or audit failed" } };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_BROWSE_CONTENT, async (_event, path2) => {
    try {
      return await bridge.call("browse-content", path2 ? { path: path2 } : {});
    } catch {
      return { currentPath: "", relativePath: "", entries: [] };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_INSPECT_ASSET, async (_event, path2) => {
    return bridge.call("inspect-asset", { path: path2 });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_DEVICES, async (_event, levelPath) => {
    try {
      return await bridge.call("list-devices", levelPath ? { levelPath } : {});
    } catch {
      return { levelPath: "", devices: [] };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_INSPECT_DEVICE, async (_event, path2) => {
    return bridge.call("inspect-device", { path: path2 });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_USER_ASSETS, async () => {
    try {
      return await bridge.call("list-user-assets");
    } catch {
      return { assets: [], totalCount: 0 };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_EPIC_ASSETS, async () => {
    try {
      return await bridge.call("list-epic-assets");
    } catch {
      return { types: [], totalPlaced: 0, uniqueTypes: 0, deviceCount: 0, propCount: 0 };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_READ_VERSE, async (_event, path2) => {
    return bridge.call("read-verse", { path: path2 });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_STAGED, async () => {
    try {
      return await bridge.call("list-staged");
    } catch {
      return { files: [], totalSize: 0 };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_APPLY_STAGED, async () => {
    return bridge.call("apply-staged");
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_DISCARD_STAGED, async () => {
    return bridge.call("discard-staged");
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_LIST_LIBRARIES, async () => {
    try {
      return await bridge.call("list-libraries");
    } catch {
      return { activeLibraryId: null, libraries: [] };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_ADD_LIBRARY, async (_event, path2) => {
    return bridge.call("add-library", { path: path2 });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_REMOVE_LIBRARY, async (_event, id) => {
    return bridge.call("remove-library", { id });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_ACTIVATE_LIBRARY, async (_event, id) => {
    return bridge.call("activate-library", { id });
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_INDEX_LIBRARY, async (_event, id) => {
    return bridge.call("index-library", id ? { id } : {});
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_GET_LIBRARY_VERSE_FILES, async (_event, filter) => {
    return bridge.call("get-library-verse-files", filter ? { filter } : {});
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_GET_LIBRARY_ASSETS_BY_TYPE, async () => {
    return bridge.call("get-library-assets-by-type");
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_BROWSE_LIBRARY_DIR, async (_event, path2) => {
    try {
      return await bridge.call("browse-library-dir", path2 ? { path: path2 } : {});
    } catch {
      return { entries: [] };
    }
  });
  electron.ipcMain.handle(IPC_CHANNELS.FORGE_SEARCH_LIBRARY_INDEX, async (_event, query) => {
    return bridge.call("search-library-index", { query });
  });
}
const ROOT_DIR = is.dev ? electron.app.getAppPath() : path.join(electron.app.getPath("exe"), "..");
const ASSETS_DIR = path.join(ROOT_DIR, "fortnite_assets");
const FONTS_DIR = path.join(ROOT_DIR, "fonts");
function createWindow() {
  const mainWindow = new electron.BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1e3,
    minHeight: 700,
    show: false,
    backgroundColor: "#0f0f1a",
    icon: path.join(__dirname, "../../src/renderer/assets/icon.png"),
    // Use default title bar on Windows for proper resize handles
    ...process.platform === "darwin" ? { titleBarStyle: "hiddenInset" } : {},
    title: "WellVersed",
    webPreferences: {
      preload: path.join(__dirname, "../preload/index.js"),
      sandbox: false,
      webSecurity: false
      // Allow loading local file:// images
    }
  });
  mainWindow.on("ready-to-show", () => {
    mainWindow.show();
  });
  mainWindow.webContents.setWindowOpenHandler((details) => {
    electron.shell.openExternal(details.url);
    return { action: "deny" };
  });
  if (is.dev && process.env["ELECTRON_RENDERER_URL"]) {
    mainWindow.loadURL(process.env["ELECTRON_RENDERER_URL"]);
  } else {
    mainWindow.loadFile(path.join(__dirname, "../renderer/index.html"));
  }
}
electron.app.whenReady().then(() => {
  electronApp.setAppUserModelId("ai.wellversed");
  electron.app.on("browser-window-created", (_, window) => {
    optimizer.watchWindowShortcuts(window);
  });
  const forgeBridge = new ForgeBridge();
  forgeBridge.start();
  forgeBridge.on("ready", () => console.log("[main] FortniteForge sidecar connected"));
  forgeBridge.on("error", (err) => console.error("[main] Sidecar error:", err));
  registerIpcHandlers(ASSETS_DIR, FONTS_DIR);
  registerForgeHandlers(forgeBridge);
  createWindow();
  electron.app.on("activate", () => {
    if (electron.BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});
electron.app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    electron.app.quit();
  }
});
electron.app.on("will-quit", () => {
});
