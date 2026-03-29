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
  WellVersedStatus,
  WellVersedProjectList,
  WellVersedProject,
  WellVersedDiscoveredProject,
  WellVersedLevel,
  AuditResult,
  ContentBrowseResult,
  AssetInspectResult,
  DeviceListResult,
  DeviceInspectResult,
  UserAssetListResult,
  EpicAssetListResult,
  VerseFileContent,
  StagedListResult,
  SnapshotResult,
  SnapshotListResult,
  DiffResult,
  LibraryList,
  LibraryEntry,
  LibraryIndexResult,
  LibAssetGroup,
  LibrarySearchResult,
  LibVerseFileEntry,
  EncyclopediaSearchResponse,
  DeviceReferenceResponse,
  CommonConfigsResponse,
  DeviceListingResponse,
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

// ─── WellVersed .NET bridge (via Rust sidecar) ────────────────────────────

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

// Widget parsing
export interface WidgetSummary {
  name: string
  path: string
  widgetCount: number
}

export async function forgeListProjectWidgets(): Promise<{ widgets: WidgetSummary[] }> {
  return invoke('forge_list_project_widgets')
}

export async function forgeParseWidget(path: string): Promise<{ spec: any }> {
  return invoke('forge_parse_widget', { path })
}

export async function forgeWidgetTexture(
  texturePath: string
): Promise<{ found: boolean; dataUrl?: string; width?: number; height?: number; warning?: string; message?: string }> {
  return invoke('forge_widget_texture', { texturePath })
}

export async function forgeListLibraryWidgets(): Promise<{ widgets: WidgetSummary[] }> {
  return invoke('forge_list_library_widgets')
}

// Project management
export async function wellVersedStatus(): Promise<WellVersedStatus> {
  return invoke('forge_status')
}

export async function wellVersedListProjects(): Promise<WellVersedProjectList> {
  return invoke('forge_list_projects')
}

export async function forgeAddProject(
  path: string,
  type: string
): Promise<WellVersedProject> {
  return invoke('forge_add_project', { path, projectType: type })
}

export async function forgeDiffProjects(
  projectPathA: string,
  projectPathB: string,
): Promise<{
  projectA: string
  projectB: string
  description: string
  addedCount: number
  modifiedCount: number
  deletedCount: number
  totalChanges: number
  changes: Array<{
    path: string
    type: string
    oldSize: number
    newSize: number
    actorClass?: string
    actorName?: string
  }>
}> {
  return invoke('forge_diff_projects', { projectPathA, projectPathB })
}

export async function forgeCreateDevCopy(
  projectPath: string
): Promise<{
  success: boolean
  devCopyPath: string
  sourceProject: string
  filesCopied: number
  bridgeInstalled: string[]
  message: string
}> {
  return invoke('forge_create_dev_copy', { projectPath })
}

export async function forgeOpenInUefn(
  projectPath: string
): Promise<{ opened: boolean; file: string }> {
  return invoke('forge_open_in_uefn', { projectPath })
}

export async function forgeInstallBridge(
  projectPath: string
): Promise<{
  success: boolean
  projectPath: string
  pythonDir: string
  installed: string[]
  message: string
}> {
  return invoke('forge_install_bridge', { projectPath })
}

export async function forgeRemoveProject(
  id: string
): Promise<{ removed: boolean }> {
  return invoke('forge_remove_project', { id })
}

export async function wellVersedActivateProject(id: string): Promise<WellVersedProject> {
  return invoke('forge_activate_project', { id })
}

export async function forgeScanProjects(
  path: string
): Promise<WellVersedDiscoveredProject[]> {
  return invoke('forge_scan_projects', { path })
}

export async function forgeListLevels(): Promise<WellVersedLevel[]> {
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

// Project diff / snapshots
export async function forgeTakeSnapshot(description?: string): Promise<SnapshotResult> {
  const params = description ? { description } : {}
  return invoke('forge_take_snapshot', params)
}

export async function forgeListSnapshots(): Promise<SnapshotListResult> {
  return invoke('forge_list_snapshots')
}

export async function forgeCompareSnapshot(snapshotId: string): Promise<DiffResult> {
  return invoke('forge_compare_snapshot', { snapshotId })
}

// File watcher
export async function startFileWatcher(projectPath: string): Promise<{ watching: boolean; projectPath: string }> {
  return invoke('start_file_watcher', { projectPath })
}

export async function stopFileWatcher(): Promise<{ stopped: boolean }> {
  return invoke('stop_file_watcher')
}

export async function fileWatcherStatus(): Promise<{ watching: boolean; projectPath: string | null }> {
  return invoke('file_watcher_status')
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

// Device Encyclopedia
export async function forgeEncyclopediaSearch(
  query: string
): Promise<EncyclopediaSearchResponse> {
  return invoke('forge_encyclopedia_search', { query })
}

export async function forgeEncyclopediaDeviceReference(
  deviceClass: string
): Promise<DeviceReferenceResponse> {
  return invoke('forge_encyclopedia_device_reference', { deviceClass })
}

export async function forgeEncyclopediaCommonConfigs(
  deviceClass: string
): Promise<CommonConfigsResponse> {
  return invoke('forge_encyclopedia_common_configs', { deviceClass })
}

export async function forgeEncyclopediaListDevices(): Promise<DeviceListingResponse> {
  return invoke('forge_encyclopedia_list_devices')
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

export interface MeshExportResult {
  deviceClass: string
  found: boolean
  glbBase64?: string
  vertexCount?: number
  cached?: boolean
  assetPath?: string
  sizeBytes?: number
  error?: string
}

export async function previewExportMesh(
  deviceClass: string
): Promise<MeshExportResult> {
  return invoke('forge_preview_export_mesh', { deviceClass })
}

export async function previewExportMeshBatch(
  deviceClasses: string[]
): Promise<{ results: MeshExportResult[]; total: number; exported: number }> {
  return invoke('forge_preview_export_mesh_batch', { deviceClasses })
}

// ─── Game Designer API ──────────────────────────────────────────────────────

export async function forgeDesignGame(
  description: string,
  playerCount?: number,
  teamCount?: number
): Promise<Record<string, unknown>> {
  return invoke('forge_design_game', {
    description,
    playerCount: playerCount ?? null,
    teamCount: teamCount ?? null,
  })
}

// ─── Stamp API ──────────────────────────────────────────────────────────────

export async function forgeStampList(): Promise<{
  stamps: Array<{
    name: string
    actorCount: number
    createdAt: string
    description?: string
    tags?: string[]
  }>
}> {
  return invoke('forge_stamp_list')
}

export async function forgeStampSave(name: string): Promise<{ success: boolean; name: string }> {
  return invoke('forge_stamp_save', { name })
}

export async function forgeStampPlace(
  name: string,
  x: number,
  y: number,
  z: number
): Promise<{ success: boolean }> {
  return invoke('forge_stamp_place', { name, x, y, z })
}

// ─── Publish Audit API ──────────────────────────────────────────────────────

export async function forgeRunPublishAudit(): Promise<Record<string, unknown>> {
  return invoke('forge_run_publish_audit')
}

// ─── Device Behavior Simulator (DFA model) ───────────────────────────────────

export interface DFANode {
  deviceName: string
  deviceClass: string
  deviceType: string
  phase: string
  events: string[]
  actions: string[]
  x: number
  y: number
}

export interface DFAEdge {
  sourceDevice: string
  event: string
  targetDevice: string
  action: string
  resultingPhase: string
  isConditional: boolean
  condition?: string
}

export interface DFASnapshot {
  stepNumber: number
  simulatedTime: number
  firedEdgeSource: string
  firedEdgeEvent: string
  stateHash: string
  devicePhases: Record<string, string>
}

export interface GameLoopResult {
  initialTrigger: string
  stepCount: number
  totalSimulatedTime: number
  reachesEndGame: boolean
  warnings: string[]
  nodes: DFANode[]
  edges: DFAEdge[]
  history: DFASnapshot[]
  finalStates: Record<string, string>
}

export interface SimulateEventResult {
  initialTrigger: string
  stepCount: number
  totalSimulatedTime: number
  reachesEndGame: boolean
  warnings: string[]
  nodes: DFANode[]
  edges: DFAEdge[]
  history: DFASnapshot[]
  finalStates: Record<string, string>
}

export async function forgeSimulateGameLoop(
  levelPath: string
): Promise<GameLoopResult> {
  return invoke('forge_simulate_game_loop', { levelPath })
}

export async function forgeSimulateEvent(
  levelPath: string,
  deviceName: string,
  eventName: string
): Promise<SimulateEventResult> {
  return invoke('forge_simulate_event', { levelPath, deviceName, eventName })
}

// ─── System extraction ──────────────────────────────────────────────────────

export async function forgeAnalyzeLevelSystems(
  levelPath: string
): Promise<{
  levelPath: string
  totalDevices: number
  systemsFound: number
  systems: Array<{
    name: string
    category: string
    detectionMethod: string
    confidence: number
    deviceCount: number
    devices: Array<{ role: string; deviceClass: string; deviceType: string; label: string }>
    wiring: Array<{ connection: string; channel: string | null }>
  }>
  errors: string[]
}> {
  return invoke('forge_analyze_level_systems', { levelPath })
}

export async function forgeAnalyzeProjectSystems(): Promise<{
  projectPath: string
  levelsScanned: number
  totalSystems: number
  uniquePatterns: number
  systems: Array<{
    name: string
    category: string
    confidence: number
    deviceCount: number
    frequency: number
  }>
  errors: string[]
}> {
  return invoke('forge_analyze_project_systems')
}

// ─── UEFN Bridge (live connection to running UEFN instance) ─────────────────

export async function forgeBridgeConnect(
  port?: number
): Promise<{ connected: boolean; status?: string; error?: string; data?: unknown }> {
  return invoke('forge_bridge_connect', { port: port ?? null })
}

export async function forgeBridgeStatus(): Promise<{
  connected: boolean
  bridgeStatus?: string
  data?: unknown
  message?: string
  error?: string
}> {
  return invoke('forge_bridge_status')
}

export async function forgeBridgeCommand(
  command: string,
  params?: Record<string, unknown>
): Promise<{
  success: boolean
  status?: string
  data?: unknown
  error?: string
}> {
  return invoke('forge_bridge_command', { command, params: params ?? null })
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
  forgeListProjectWidgets,
  forgeParseWidget,
  forgeWidgetTexture,
  forgeListLibraryWidgets,
  wellVersedStatus,
  wellVersedListProjects,
  forgeAddProject,
  forgeOpenInUefn,
  forgeRemoveProject,
  wellVersedActivateProject,
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
  // Project diff / snapshots
  forgeTakeSnapshot,
  forgeListSnapshots,
  forgeCompareSnapshot,
  // File watcher
  startFileWatcher,
  stopFileWatcher,
  fileWatcherStatus,
  forgeListLibraries,
  forgeAddLibrary,
  forgeRemoveLibrary,
  forgeActivateLibrary,
  forgeIndexLibrary,
  forgeGetLibraryVerseFiles,
  forgeGetLibraryAssetsByType,
  forgeBrowseLibraryDir,
  forgeSearchLibraryIndex,
  // Device Encyclopedia
  forgeEncyclopediaSearch,
  forgeEncyclopediaDeviceReference,
  forgeEncyclopediaCommonConfigs,
  forgeEncyclopediaListDevices,
  forgeReadTextFile,
  forgeListDirectory,
  // CUE4Parse preview
  previewInit,
  previewStatus,
  previewSearch,
  previewTexture,
  previewMeshInfo,
  previewExportMesh,
  previewExportMeshBatch,
  // UEFN Bridge
  forgeBridgeConnect,
  forgeBridgeStatus,
  forgeBridgeCommand,
  // Game Designer
  forgeDesignGame,
  // Stamps
  forgeStampList,
  forgeStampSave,
  forgeStampPlace,
  // Publish
  forgeRunPublishAudit,
  // Device Behavior Simulator
  forgeSimulateGameLoop,
  forgeSimulateEvent,
}

// ─── Verse LSP API ──────────────────────────────────────────────────────────

export async function lspStatus() {
  return invoke<{ available: boolean; binaryPath: string | null; ready: boolean; capabilities: any }>('lsp_status')
}

export async function lspStart(workspacePath: string) {
  return invoke<{ status: string; capabilities?: any }>('lsp_start', { workspacePath })
}

export async function lspStop() {
  return invoke<{ status: string }>('lsp_stop')
}

export async function lspDidOpen(uri: string, content: string) {
  return invoke('lsp_did_open', { uri, content })
}

export async function lspDidChange(uri: string, content: string, version: number) {
  return invoke('lsp_did_change', { uri, content, version: Math.floor(version) })
}

export async function lspCompletion(uri: string, line: number, character: number) {
  return invoke('lsp_completion', { uri, line: Math.floor(line), character: Math.floor(character) })
}

export async function lspHover(uri: string, line: number, character: number) {
  return invoke('lsp_hover', { uri, line: Math.floor(line), character: Math.floor(character) })
}

export async function lspDefinition(uri: string, line: number, character: number) {
  return invoke('lsp_definition', { uri, line: Math.floor(line), character: Math.floor(character) })
}

export async function lspDocumentSymbols(uri: string) {
  return invoke('lsp_document_symbols', { uri })
}

export async function lspSignatureHelp(uri: string, line: number, character: number) {
  return invoke('lsp_signature_help', { uri, line: Math.floor(line), character: Math.floor(character) })
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
