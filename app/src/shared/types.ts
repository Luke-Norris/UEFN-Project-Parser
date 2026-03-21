// Asset types
export interface AssetEntry {
  name: string
  filename: string
  path: string // Absolute path on disk
  relativePath: string // Relative to fortnite_assets/
  category: string // Parent directory name
  extension: string
}

export interface AssetCategory {
  name: string
  path: string
  assets: AssetEntry[]
  subcategories: AssetCategory[]
}

export interface AssetIndex {
  categories: AssetCategory[]
  totalAssets: number
}

// Template types
export type LayerType = 'image' | 'text' | 'rect'
export type TemplateCategory = 'item' | 'combat' | 'hud' | 'umg' | 'custom'
export type RarityTier = 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary' | 'mythic' | 'exotic'

export const RARITY_COLORS: Record<RarityTier, string> = {
  common: '#bfbfbf',
  uncommon: '#60aa3a',
  rare: '#3d85e0',
  epic: '#a34ee1',
  legendary: '#c76b29',
  mythic: '#c4a23c',
  exotic: '#76d6e3'
}

export const RARITY_TIERS: RarityTier[] = [
  'common', 'uncommon', 'rare', 'epic', 'legendary', 'mythic', 'exotic'
]

export interface TemplateLayer {
  id: string
  type: LayerType
  name: string
  left: number
  top: number
  width: number
  height: number
  // Image layers
  assetCategory?: string
  assetFilter?: string
  defaultAsset?: string
  // Text layers
  text?: string
  fontFamily?: string
  fontSize?: number
  fill?: string
  stroke?: string
  strokeWidth?: number
  textAlign?: string
  // Rect layers
  rectFill?: string
  cornerRadius?: number
  opacity?: number
  // Behavior
  editable: boolean
  swappable: boolean
  locked: boolean
  // Hierarchy (for UMG widget tree display)
  parentId?: string
  depth?: number
  widgetType?: string // e.g. "CanvasPanel", "Overlay", "TextBlock"
  isContainer?: boolean
  // UMG slot properties (stored on the child, describe its placement in parent)
  slotHAlign?: string
  slotVAlign?: string
  slotPadding?: { left: number; top: number; right: number; bottom: number }
  // UMG widget-specific properties
  umgOrientation?: 'Horizontal' | 'Vertical'
  umgJustification?: 'Left' | 'Center' | 'Right'
}

export interface TemplateVariable {
  id: string
  name: string
  type: 'text' | 'color' | 'image' | 'number'
  defaultValue: string
  /** Which layer property this variable binds to */
  layerId: string
  layerProperty: string // 'text' | 'fill' | 'src' | 'fontSize' etc.
}

export interface ComponentTemplate {
  id: string
  name: string
  description: string
  category: TemplateCategory
  width: number
  height: number
  layers: TemplateLayer[]
  variables?: TemplateVariable[]
  /** If true, this is a user-created custom template */
  isCustom?: boolean
}

// IPC channel names
export const IPC_CHANNELS = {
  SCAN_ASSETS: 'assets:scan',
  GET_ASSET_DATA: 'assets:getData',
  IMPORT_FILES: 'assets:importFiles',
  IMPORT_TO_ASSETS: 'assets:importToAssets',
  CREATE_ASSET_FOLDER: 'assets:createFolder',
  DELETE_ASSET: 'assets:delete',
  EXPORT_PNG: 'export:png',
  EXPORT_BATCH: 'export:batch',
  SAVE_FILE: 'file:save',
  SELECT_DIRECTORY: 'file:selectDirectory',
  GET_FONTS: 'fonts:list',
  GET_FONT_DATA: 'fonts:getData',
  IMPORT_WIDGET_SPEC: 'widget:importSpec',
  EXPORT_WIDGET_SPEC: 'widget:exportSpec',
  // FortniteForge .NET sidecar bridge
  FORGE_PING: 'forge:ping',
  FORGE_VALIDATE_SPEC: 'forge:validateSpec',
  FORGE_BUILD_UASSET: 'forge:buildUasset',
  FORGE_GENERATE_VERSE: 'forge:generateVerse',
  // Project management
  FORGE_STATUS: 'forge:status',
  FORGE_LIST_PROJECTS: 'forge:listProjects',
  FORGE_ADD_PROJECT: 'forge:addProject',
  FORGE_REMOVE_PROJECT: 'forge:removeProject',
  FORGE_ACTIVATE_PROJECT: 'forge:activateProject',
  FORGE_SCAN_PROJECTS: 'forge:scanProjects',
  FORGE_LIST_LEVELS: 'forge:listLevels',
  FORGE_AUDIT: 'forge:audit',
  // Browse & inspect
  FORGE_BROWSE_CONTENT: 'forge:browseContent',
  FORGE_INSPECT_ASSET: 'forge:inspectAsset',
  FORGE_LIST_DEVICES: 'forge:listDevices',
  FORGE_INSPECT_DEVICE: 'forge:inspectDevice',
  FORGE_LIST_USER_ASSETS: 'forge:listUserAssets',
  FORGE_LIST_EPIC_ASSETS: 'forge:listEpicAssets',
  FORGE_READ_VERSE: 'forge:readVerse',
  FORGE_LIST_STAGED: 'forge:listStaged',
  FORGE_APPLY_STAGED: 'forge:applyStaged',
  FORGE_DISCARD_STAGED: 'forge:discardStaged',
  // Library management (reference collections — NOT projects)
  FORGE_LIST_LIBRARIES: 'forge:listLibraries',
  FORGE_ADD_LIBRARY: 'forge:addLibrary',
  FORGE_REMOVE_LIBRARY: 'forge:removeLibrary',
  FORGE_ACTIVATE_LIBRARY: 'forge:activateLibrary',
  FORGE_INDEX_LIBRARY: 'forge:indexLibrary',
  FORGE_GET_LIBRARY_VERSE_FILES: 'forge:getLibraryVerseFiles',
  FORGE_GET_LIBRARY_ASSETS_BY_TYPE: 'forge:getLibraryAssetsByType',
  FORGE_BROWSE_LIBRARY_DIR: 'forge:browseLibraryDir',
  FORGE_SEARCH_LIBRARY_INDEX: 'forge:searchLibraryIndex',
  // General file reading (for docs, verse-book, etc.)
  FORGE_READ_TEXT_FILE: 'forge:readTextFile',
  FORGE_LIST_DIRECTORY: 'forge:listDirectory'
} as const

// FortniteForge project types
export interface ForgeStatus {
  isConfigured: boolean
  projectName: string
  projectPath?: string
  projectType?: string
  isUefnProject?: boolean
  contentPath?: string
  isUefnRunning?: boolean
  uefnPid?: number | null
  hasUrc?: boolean
  urcActive?: boolean
  mode: string
  modeReason?: string
  stagedFileCount?: number
  assetCount: number
  definitionCount?: number
  verseCount: number
  levelCount?: number
  readOnly?: boolean
}

export interface ForgeProject {
  id: string
  projectPath: string
  name: string
  type: string
  isUefnProject: boolean
  hasUrc: boolean
  contentPath: string
  assetCount: number
  externalActorCount: number
  verseFileCount: number
  levelCount: number
  addedAt: string
}

export interface ForgeProjectList {
  activeProjectId: string | null
  projects: ForgeProject[]
}

export interface ForgeDiscoveredProject {
  projectPath: string
  projectName: string
  isUefnProject: boolean
  hasUrc: boolean
  assetCount: number
  externalActorCount: number
  verseFileCount: number
  levelCount: number
  alreadyAdded: boolean
}

export interface ForgeLevel {
  filePath: string
  relativePath: string
  name: string
}

// Audit types
export interface AuditFinding {
  severity: 'Error' | 'Warning' | 'Info'
  category: string
  message: string
  suggestion?: string
  filePath?: string
}

export interface AuditResult {
  projectName: string
  level?: string
  findings: AuditFinding[]
  timestamp: string
}

// Content browser types
export interface ContentEntry {
  name: string
  path: string
  relativePath: string
  isDirectory: boolean
  extension?: string
  size?: number
  lastModified?: string
}

export interface ContentBrowseResult {
  currentPath: string
  relativePath: string
  entries: ContentEntry[]
}

// Asset inspect types
export interface AssetExport {
  exportType: string
  objectName: string
}

export interface AssetProperty {
  name: string
  value: string
  type: string
  isEditable?: boolean
}

export interface AssetInspectResult {
  filePath: string
  fileName: string
  assetClass?: string
  exports: AssetExport[]
  properties: AssetProperty[]
}

// Device types
export interface DeviceEntry {
  name: string
  filePath: string
  deviceType: string
  position?: { x: number; y: number; z: number }
}

export interface DeviceListResult {
  levelPath: string
  devices: DeviceEntry[]
}

export interface DeviceInspectResult {
  filePath: string
  deviceType: string
  name: string
  position?: { x: number; y: number; z: number }
  properties: AssetProperty[]
}

// User asset types
export interface UserAssetEntry {
  name: string
  filePath: string
  relativePath: string
  assetClass: string
  size: number
}

export interface UserAssetListResult {
  assets: UserAssetEntry[]
  totalCount: number
}

// Epic asset types
export interface EpicAssetTypeEntry {
  typeName: string
  className?: string
  displayName?: string
  count: number
  isDevice: boolean
  samplePaths?: string[]
}

export interface EpicAssetListResult {
  types: EpicAssetTypeEntry[]
  totalPlaced: number
  uniqueTypes: number
  deviceCount: number
  propCount: number
}

// Verse types
export interface VerseFileEntry {
  name: string
  filePath: string
  relativePath: string
  lineCount: number
}

export interface VerseFileContent {
  filePath: string
  name: string
  content: string
  lineCount: number
}

// Staged types
export interface StagedFileEntry {
  filePath: string
  relativePath: string
  size: number
}

export interface StagedListResult {
  files: StagedFileEntry[]
  totalSize: number
}

// Library management types (reference collections — NOT projects)
export interface LibraryEntry {
  id: string
  path: string
  name: string
  verseFileCount: number
  assetCount: number
  indexedAt: string | null
  addedAt: string
}

export interface LibraryList {
  activeLibraryId: string | null
  libraries: LibraryEntry[]
}

export interface LibraryIndexResult {
  libraryId: string
  libraryName: string
  indexPath: string
  totalProjects: number
  totalVerseFiles: number
  totalAssets: number
  totalDeviceTypes: number
  indexedAt: string
}

export interface LibVerseFileEntry {
  name: string
  filePath: string
  lineCount: number
  classes: string[]
  functions: string[]
  deviceReferences: string[]
  imports: string[]
  summary: string
  projectName: string
}

export interface LibAssetEntry {
  name: string
  filePath: string
  assetClass: string
  fileSize: number
  projectName: string
}

export interface LibAssetGroup {
  assetClass: string
  count: number
  assets: LibAssetEntry[]
}

export interface LibrarySearchResult {
  query: string
  verseFiles: Array<LibVerseFileEntry & { score: number }>
  assets: Array<LibAssetEntry & { score: number }>
  deviceTypes: Array<{
    className: string
    displayName: string
    count: number
    projectName: string
    score: number
  }>
}

// Library/widget types
export interface LibraryWidgetEntry {
  name: string
  widgetType: string
  category: string
  exportCount: number
  filePath?: string
}

// Export options
export interface ExportOptions {
  width: number
  height: number
  format: 'png' | 'jpeg'
  quality: number
  outputPath: string
  filename: string
}

export interface BatchExportOptions {
  template: string
  outputDir: string
  variants: Record<string, string[]> // layerId -> array of asset paths
  width: number
  height: number
}
