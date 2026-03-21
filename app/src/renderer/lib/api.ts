/**
 * Tauri API bridge — replaces Electron's window.electronAPI preload bridge.
 *
 * All renderer code should import from this module instead of calling
 * window.electronAPI directly. Each function maps to a #[tauri::command]
 * in src-tauri/src/commands.rs.
 */

import { invoke } from '@tauri-apps/api/core'
import { open, save } from '@tauri-apps/plugin-dialog'

import type {
  AssetIndex,
  ForgeStatus,
  ForgeProjectList,
  ForgeProject,
  ForgeDiscoveredProject,
  ForgeLevel,
  AuditResult,
  ContentBrowseResult,
  AssetInspectResult,
  DeviceListResult,
  DeviceInspectResult,
  UserAssetListResult,
  EpicAssetListResult,
  VerseFileContent,
  StagedListResult,
  LibraryList,
  LibraryEntry,
  LibraryIndexResult,
  LibAssetGroup,
  LibrarySearchResult,
  LibVerseFileEntry,
} from '../../shared/types'

// ─── Asset management (local file system via Rust) ───────────────────────────

export async function scanAssets(): Promise<AssetIndex> {
  return invoke('scan_assets')
}

export async function getAssetData(filePath: string): Promise<string | null> {
  return invoke('get_asset_data', { filePath })
}

export async function getFonts(): Promise<string[]> {
  return invoke('get_fonts')
}

export async function getFontData(
  fontFilename: string
): Promise<{ data: string; filename: string } | null> {
  return invoke('get_font_data', { fontFilename })
}

export async function importFiles(): Promise<
  Array<{ path: string; name: string; dataUrl: string }>
> {
  // Use Tauri dialog to pick files, then read them via Rust
  const selected = await open({
    multiple: true,
    filters: [
      { name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'] },
    ],
  })

  if (!selected) return []

  const paths = Array.isArray(selected) ? selected : [selected]
  const results: Array<{ path: string; name: string; dataUrl: string }> = []

  for (const filePath of paths) {
    const dataUrl = await getAssetData(filePath)
    if (dataUrl) {
      const name = filePath.split(/[/\\]/).pop()?.replace(/\.[^.]+$/, '') ?? 'unnamed'
      results.push({ path: filePath, name, dataUrl })
    }
  }

  return results
}

export async function importToAssets(
  targetFolder: string
): Promise<{ success: boolean; imported: string[]; index?: AssetIndex; error?: string }> {
  const selected = await open({
    multiple: true,
    filters: [
      { name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'] },
    ],
  })

  if (!selected) return { success: false, imported: [] }

  const filePaths = Array.isArray(selected) ? selected : [selected]
  return invoke('import_to_assets', { targetFolder, filePaths })
}

export async function createAssetFolder(
  folderName: string,
  parentPath: string
): Promise<{ success: boolean; path?: string; index?: AssetIndex; error?: string }> {
  return invoke('create_asset_folder', { folderName, parentPath })
}

export async function deleteAsset(
  filePath: string
): Promise<{ success: boolean; index?: AssetIndex; error?: string }> {
  return invoke('delete_asset', { filePath })
}

export async function exportPng(
  dataUrl: string,
  defaultName: string
): Promise<string | null> {
  const filePath = await save({
    defaultPath: defaultName,
    filters: [{ name: 'PNG Image', extensions: ['png'] }],
  })

  if (!filePath) return null

  return invoke('export_png', { dataUrl, filePath })
}

export async function selectDirectory(): Promise<string | null> {
  const selected = await open({
    directory: true,
  })
  return selected ?? null
}

export async function exportBatch(
  items: Array<{ dataUrl: string; filename: string }>,
  outputDir: string
): Promise<string[]> {
  return invoke('export_batch', { items, outputDir })
}

export async function importWidgetSpec(): Promise<{
  success: boolean
  spec?: unknown
  path?: string
  error?: string
}> {
  const filePath = await open({
    filters: [{ name: 'Widget Spec', extensions: ['json'] }],
  })

  if (!filePath) return { success: false }

  try {
    const result: { content?: string; name?: string; error?: string } = await invoke(
      'read_text_file',
      { filePath }
    )
    if (result.error || !result.content) {
      return { success: false, error: result.error ?? 'Failed to read file' }
    }
    const spec = JSON.parse(result.content)
    if (spec.$schema !== 'widget-spec-v1') {
      return { success: false, error: 'Not a valid widget-spec-v1 file' }
    }
    return { success: true, spec, path: filePath }
  } catch (err) {
    return { success: false, error: `Failed to parse: ${err}` }
  }
}

export async function exportWidgetSpec(
  specJson: string
): Promise<{ success: boolean; path?: string; error?: string }> {
  const filePath = await save({
    defaultPath: 'widget-spec.json',
    filters: [{ name: 'Widget Spec', extensions: ['json'] }],
  })

  if (!filePath) return { success: false }

  try {
    // Write via Rust — reuse export_png's write pattern but for text
    // For now, write as base64-encoded text through the existing mechanism
    const encoder = new TextEncoder()
    const bytes = encoder.encode(specJson)
    const binary = Array.from(bytes)
      .map((b) => String.fromCharCode(b))
      .join('')
    const b64 = btoa(binary)
    await invoke('export_png', {
      dataUrl: `data:application/json;base64,${b64}`,
      filePath,
    })
    return { success: true, path: filePath }
  } catch (err) {
    return { success: false, error: `Failed to write: ${err}` }
  }
}

// ─── FortniteForge .NET bridge (via Rust sidecar) ────────────────────────────

export async function forgePing(): Promise<{ pong: boolean } | { error: string }> {
  return invoke('forge_ping')
}

export async function forgeValidateSpec(
  specJson: string
): Promise<{
  valid: boolean
  errors: Array<{ path: string; severity: string; message: string }>
}> {
  return invoke('forge_validate_spec', { spec: JSON.parse(specJson) })
}

export async function forgeBuildUasset(
  specJson: string,
  outputDir: string,
  variables?: Record<string, string>
): Promise<{ success: boolean; uassetPath?: string; versePath?: string; error?: string }> {
  return invoke('forge_build_uasset', {
    spec: JSON.parse(specJson),
    outputDir,
    variables: variables ?? null,
  })
}

export async function forgeGenerateVerse(
  specJson: string
): Promise<{ code: string }> {
  return invoke('forge_generate_verse', { spec: JSON.parse(specJson) })
}

// Project management
export async function forgeStatus(): Promise<ForgeStatus> {
  return invoke('forge_status')
}

export async function forgeListProjects(): Promise<ForgeProjectList> {
  return invoke('forge_list_projects')
}

export async function forgeAddProject(
  path: string,
  type: string
): Promise<ForgeProject> {
  return invoke('forge_add_project', { path, projectType: type })
}

export async function forgeRemoveProject(
  id: string
): Promise<{ removed: boolean }> {
  return invoke('forge_remove_project', { id })
}

export async function forgeActivateProject(id: string): Promise<ForgeProject> {
  return invoke('forge_activate_project', { id })
}

export async function forgeScanProjects(
  path: string
): Promise<ForgeDiscoveredProject[]> {
  return invoke('forge_scan_projects', { path })
}

export async function forgeListLevels(): Promise<ForgeLevel[]> {
  return invoke('forge_list_levels')
}

export async function forgeAudit(level?: string): Promise<AuditResult> {
  return invoke('forge_audit', { level: level ?? null })
}

// Browse & inspect
export async function forgeBrowseContent(
  path?: string
): Promise<ContentBrowseResult> {
  return invoke('forge_browse_content', { path: path ?? null })
}

export async function forgeInspectAsset(
  path: string
): Promise<AssetInspectResult> {
  if (!path) throw new Error('No asset path provided')
  return invoke('forge_inspect_asset', { path: String(path) })
}

export async function forgeListDevices(
  levelPath?: string
): Promise<DeviceListResult> {
  return invoke('forge_list_devices', { levelPath: levelPath ?? null })
}

export async function forgeInspectDevice(
  path: string
): Promise<DeviceInspectResult> {
  if (!path) throw new Error('No device path provided')
  return invoke('forge_inspect_device', { path: String(path) })
}

export async function forgeListUserAssets(): Promise<UserAssetListResult> {
  return invoke('forge_list_user_assets')
}

export async function forgeListEpicAssets(): Promise<EpicAssetListResult> {
  return invoke('forge_list_epic_assets')
}

export async function forgeReadVerse(path: string): Promise<VerseFileContent> {
  if (!path) throw new Error('No verse file path provided')
  return invoke('forge_read_verse', { path: String(path) })
}

export async function forgeListStaged(): Promise<StagedListResult> {
  return invoke('forge_list_staged')
}

export async function forgeApplyStaged(): Promise<{ applied: number }> {
  return invoke('forge_apply_staged')
}

export async function forgeDiscardStaged(): Promise<{ discarded: number }> {
  return invoke('forge_discard_staged')
}

// Library management
export async function forgeListLibraries(): Promise<LibraryList> {
  return invoke('forge_list_libraries')
}

export async function forgeAddLibrary(path: string): Promise<LibraryEntry> {
  return invoke('forge_add_library', { path })
}

export async function forgeRemoveLibrary(
  id: string
): Promise<{ removed: boolean }> {
  return invoke('forge_remove_library', { id })
}

export async function forgeActivateLibrary(
  id: string
): Promise<LibraryEntry> {
  return invoke('forge_activate_library', { id })
}

export async function forgeIndexLibrary(
  id?: string
): Promise<LibraryIndexResult> {
  return invoke('forge_index_library', { id: id ?? null })
}

export async function forgeGetLibraryVerseFiles(
  filter?: string
): Promise<{ verseFiles: LibVerseFileEntry[] }> {
  return invoke('forge_get_library_verse_files', { filter: filter ?? null })
}

export async function forgeGetLibraryAssetsByType(): Promise<{
  groups: LibAssetGroup[]
}> {
  return invoke('forge_get_library_assets_by_type')
}

export async function forgeBrowseLibraryDir(path?: string): Promise<{
  entries: Array<{
    name: string
    path: string
    type: string
    size: number
    lastModified: string
  }>
}> {
  return invoke('forge_browse_library_dir', { path: path ?? null })
}

export async function forgeSearchLibraryIndex(
  query: string
): Promise<LibrarySearchResult> {
  return invoke('forge_search_library_index', { query })
}

// General file reading
export async function forgeReadTextFile(
  filePath: string
): Promise<{ content?: string; name?: string; error?: string }> {
  return invoke('read_text_file', { filePath })
}

export async function forgeListDirectory(
  dirPath: string
): Promise<{
  entries: Array<{
    name: string
    path: string
    isDirectory: boolean
    size: number
  }>
  error?: string
}> {
  return invoke('list_directory', { dirPath })
}

// ─── CUE4Parse asset preview (optional — requires Fortnite installed) ────────

export async function previewInit(
  fortnitePath: string
): Promise<{ initialized: boolean; fileCount: number; gamePath: string }> {
  return invoke('forge_preview_init', { fortnitePath })
}

export async function previewStatus(): Promise<{
  initialized: boolean
  fileCount: number
  gamePath: string | null
}> {
  return invoke('forge_preview_status')
}

export async function previewSearch(
  query: string,
  limit?: number
): Promise<{ query: string; results: string[]; count: number }> {
  return invoke('forge_preview_search', { query, limit: limit ?? 50 })
}

export async function previewTexture(
  assetPath: string
): Promise<{ assetPath: string; dataUrl: string; size: number }> {
  return invoke('forge_preview_texture', { assetPath })
}

export async function previewMeshInfo(
  assetPath: string
): Promise<{
  assetPath: string
  vertexCount: number
  triangleCount: number
  lodCount: number
  materialCount: number
}> {
  return invoke('forge_preview_mesh_info', { assetPath })
}

// ─── Backward-compatible shim ────────────────────────────────────────────────
// This allows existing code that calls window.electronAPI.* to work during
// the migration. New code should import directly from this module.

const api = {
  scanAssets,
  getAssetData,
  getFonts,
  getFontData,
  importFiles,
  importToAssets,
  createAssetFolder,
  deleteAsset,
  exportPng,
  selectDirectory,
  exportBatch,
  importWidgetSpec,
  exportWidgetSpec,
  forgePing,
  forgeValidateSpec,
  forgeBuildUasset,
  forgeGenerateVerse,
  forgeStatus,
  forgeListProjects,
  forgeAddProject,
  forgeRemoveProject,
  forgeActivateProject,
  forgeScanProjects,
  forgeListLevels,
  forgeAudit,
  forgeBrowseContent,
  forgeInspectAsset,
  forgeListDevices,
  forgeInspectDevice,
  forgeListUserAssets,
  forgeListEpicAssets,
  forgeReadVerse,
  forgeListStaged,
  forgeApplyStaged,
  forgeDiscardStaged,
  forgeListLibraries,
  forgeAddLibrary,
  forgeRemoveLibrary,
  forgeActivateLibrary,
  forgeIndexLibrary,
  forgeGetLibraryVerseFiles,
  forgeGetLibraryAssetsByType,
  forgeBrowseLibraryDir,
  forgeSearchLibraryIndex,
  forgeReadTextFile,
  forgeListDirectory,
  // CUE4Parse preview
  previewInit,
  previewStatus,
  previewSearch,
  previewTexture,
  previewMeshInfo,
}

// Install the shim on window so existing code works without changes
declare global {
  interface Window {
    electronAPI: typeof api
  }
}

if (typeof window !== 'undefined') {
  ;(window as any).electronAPI = api
}

export type ElectronAPI = typeof api
