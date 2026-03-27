/**
 * Asset name prettification utilities.
 *
 * Transforms raw UEFN asset names (BP_VendingMachine_C, PBWA_W1S_Floor_C, etc.)
 * into human-readable display names.
 */

/** Known prefixes to strip, in priority order. Longest match wins. */
const STRIP_PREFIXES = [
  'PBWA_',      // Prefab World Actor (placed building pieces)
  'BP_Prop_',   // Blueprint Prop
  'BP_Playset_', // Blueprint Playset
  'BP_Device_', // Blueprint Device
  'BP_',        // Generic Blueprint
  'B_Prop_',    // Prop variant
  'B_',         // Generic B_ prefix
  'SM_',        // Static Mesh
  'MI_',        // Material Instance
  'M_',         // Material
  'T_',         // Texture
  'SK_',        // Skeletal Mesh
  'WBP_',       // Widget Blueprint
  'WB_',        // Widget Blueprint alt
  'GA_',        // Gameplay Ability
  'GE_',        // Gameplay Effect
  'PC_',        // Player Controller
  'ABP_',       // Animation Blueprint
  'NS_',        // Niagara System
  'DA_',        // Data Asset
  'DT_',        // Data Table
  'E_',         // Enum
  'S_',         // Struct
]

/** Well-known abbreviations that map to readable words. */
const ABBREVIATIONS: Record<string, string> = {
  'W1S': 'Wood 1x1',
  'W2S': 'Wood 2x1',
  'W3S': 'Wood 3x1',
  'S1S': 'Stone 1x1',
  'S2S': 'Stone 2x1',
  'S3S': 'Stone 3x1',
  'M1S': 'Metal 1x1',
  'M2S': 'Metal 2x1',
  'M3S': 'Metal 3x1',
  'Lg': 'Large',
  'Sm': 'Small',
  'Med': 'Medium',
  'Dbl': 'Double',
  'Dmg': 'Damage',
  'Hlth': 'Health',
  'Env': 'Environment',
  'FX': 'Effects',
  'VFX': 'Visual Effects',
  'SFX': 'Sound Effects',
  'Ctrl': 'Controller',
  'Mgr': 'Manager',
  'Btn': 'Button',
  'Txt': 'Text',
  'Img': 'Image',
  'Bg': 'Background',
  'Cfg': 'Config',
}

/**
 * Categorize an asset by its raw class name or prefix.
 * Returns a human-friendly category label.
 */
export function categorizeAsset(assetClass: string, name: string): string {
  const cls = assetClass.toLowerCase()
  const nm = name.toLowerCase()

  // Devices
  if (cls.includes('device') || nm.includes('device')) return 'Devices'

  // Widget Blueprints
  if (cls.includes('widget') || nm.startsWith('wbp_') || nm.startsWith('wb_')) return 'Widgets'

  // Materials
  if (cls.includes('material') || nm.startsWith('mi_') || nm.startsWith('m_')) return 'Materials'

  // Textures
  if (cls.includes('texture') || nm.startsWith('t_')) return 'Textures'

  // Static Meshes
  if (cls.includes('staticmesh') || nm.startsWith('sm_')) return 'Meshes'

  // Skeletal Meshes
  if (cls.includes('skeletalmesh') || nm.startsWith('sk_')) return 'Skeletal Meshes'

  // Niagara / Particle
  if (cls.includes('niagara') || cls.includes('particle') || nm.startsWith('ns_')) return 'Effects'

  // Animation
  if (cls.includes('anim') || nm.startsWith('abp_')) return 'Animations'

  // Data Assets
  if (cls.includes('datatable') || cls.includes('dataasset') || nm.startsWith('dt_') || nm.startsWith('da_')) return 'Data'

  // Building pieces (PBWA)
  if (nm.startsWith('pbwa_')) return 'Building Pieces'

  // Blueprints (generic)
  if (cls.includes('blueprint') || nm.startsWith('bp_')) return 'Blueprints'

  // Sound
  if (cls.includes('sound') || cls.includes('audio')) return 'Audio'

  return 'Other'
}

/**
 * Transform a raw UEFN asset name into a human-readable display name.
 *
 * Examples:
 *   "BP_VendingMachine_C"        -> "Vending Machine"
 *   "PBWA_W1S_Floor"             -> "Wood 1x1 Floor"
 *   "MI_Brick_Wall_Red"          -> "Brick Wall Red"
 *   "BP_Device_ItemSpawner_C"    -> "Item Spawner"
 *   "FortVolumeManager"          -> "Fort Volume Manager"
 */
export function prettifyAssetName(rawName: string): string {
  if (!rawName) return 'Unknown'

  let name = rawName

  // Strip file extension if present
  name = name.replace(/\.(uasset|uexp|umap)$/i, '')

  // Strip _C suffix (class reference suffix in UE)
  name = name.replace(/_C$/, '')

  // Strip known prefixes
  for (const prefix of STRIP_PREFIXES) {
    if (name.startsWith(prefix)) {
      name = name.slice(prefix.length)
      break
    }
  }

  // Replace known abbreviations in segments
  const segments = name.split('_')
  const expanded = segments.map((seg) => ABBREVIATIONS[seg] ?? seg)

  // Join with spaces
  name = expanded.join(' ')

  // Insert spaces before capitals in PascalCase segments
  // e.g., "VendingMachine" -> "Vending Machine"
  name = name.replace(/([a-z])([A-Z])/g, '$1 $2')
  name = name.replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')

  // Clean up multiple spaces
  name = name.replace(/\s+/g, ' ').trim()

  // Capitalize first letter of each word
  name = name.replace(/\b([a-z])/g, (_, c) => c.toUpperCase())

  return name || 'Unknown'
}

/**
 * Extract just the filename (without path or extension) from a full path.
 */
export function extractFileName(filePath: string): string {
  const parts = filePath.replace(/\\/g, '/').split('/')
  const filename = parts[parts.length - 1] ?? ''
  return filename.replace(/\.[^.]+$/, '')
}

/**
 * Format byte size into human-readable string.
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
