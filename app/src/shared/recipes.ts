/**
 * Device Recipe — a reusable multi-device pattern.
 *
 * A recipe captures a group of devices with their:
 * - Relative positions (offset from a center point)
 * - Property configurations (non-default values)
 * - Wiring between devices (signal connections)
 * - Device types and metadata
 *
 * Recipes can be:
 * - Extracted from existing levels (select devices → save)
 * - Shared as JSON files
 * - Deployed into any project (clone + configure + wire)
 */

export interface DeviceRecipe {
  /** Schema version */
  $schema: 'device-recipe-v1'
  /** Unique recipe ID */
  id: string
  /** Human-readable name */
  name: string
  /** What this recipe does */
  description: string
  /** Category for browsing */
  category: RecipeCategory
  /** Tags for search */
  tags: string[]
  /** Who created this */
  author?: string
  /** When created */
  createdAt: string
  /** Source project (for provenance) */
  sourceProject?: string
  /** Source level */
  sourceLevel?: string
  /** The devices in this recipe */
  devices: RecipeDevice[]
  /** Signal connections between devices */
  wiring: RecipeWiring[]
}

export type RecipeCategory =
  | 'gameplay'    // Core game mechanics (scoring, rounds, teams)
  | 'combat'      // Weapons, damage, elimination
  | 'spawning'    // Player/item spawning patterns
  | 'triggers'    // Trigger/condition/logic chains
  | 'ui'          // HUD, messaging, notifications
  | 'environment' // Barriers, zones, volumes
  | 'economy'     // Vending, item granting, currency
  | 'custom'      // User-defined

export const RECIPE_CATEGORIES: { id: RecipeCategory; label: string; icon: string }[] = [
  { id: 'gameplay', label: 'Gameplay', icon: '🎮' },
  { id: 'combat', label: 'Combat', icon: '⚔️' },
  { id: 'spawning', label: 'Spawning', icon: '🔄' },
  { id: 'triggers', label: 'Logic & Triggers', icon: '⚡' },
  { id: 'ui', label: 'UI & HUD', icon: '💬' },
  { id: 'environment', label: 'Environment', icon: '🏗️' },
  { id: 'economy', label: 'Economy', icon: '💰' },
  { id: 'custom', label: 'Custom', icon: '📦' },
]

export interface RecipeDevice {
  /** Role name within the recipe (e.g., "capture_trigger", "score_manager") */
  role: string
  /** Device class (e.g., "BP_TriggerDevice_C") */
  deviceClass: string
  /** Pretty display name */
  displayName: string
  /** Position offset from recipe center (Unreal units) */
  offset: { x: number; y: number; z: number }
  /** Rotation (Unreal angles) */
  rotation: { pitch: number; yaw: number; roll: number }
  /** Scale */
  scale: { x: number; y: number; z: number }
  /** Non-default property overrides */
  properties: RecipeProperty[]
  /** Original actor name (for wiring reference during extraction) */
  _sourceActorName?: string
}

export interface RecipeProperty {
  name: string
  value: string
  type: string
}

export interface RecipeWiring {
  /** Source device role */
  sourceRole: string
  /** Event that fires on source */
  outputEvent: string
  /** Target device role */
  targetRole: string
  /** Action to trigger on target */
  inputAction: string
  /** Optional channel */
  channel?: string
}

// ─── Helper functions ────────────────────────────────────────────────────────

/** Generate a recipe ID */
export function generateRecipeId(): string {
  return Date.now().toString(36) + Math.random().toString(36).substring(2, 8)
}

/** Compute the center point of a group of positions */
export function computeCenter(
  positions: Array<{ x: number; y: number; z: number }>
): { x: number; y: number; z: number } {
  if (positions.length === 0) return { x: 0, y: 0, z: 0 }
  const sum = positions.reduce(
    (acc, p) => ({ x: acc.x + p.x, y: acc.y + p.y, z: acc.z + p.z }),
    { x: 0, y: 0, z: 0 }
  )
  return {
    x: sum.x / positions.length,
    y: sum.y / positions.length,
    z: sum.z / positions.length,
  }
}

/** Convert absolute positions to offsets from center */
export function toOffsets(
  positions: Array<{ x: number; y: number; z: number }>,
  center: { x: number; y: number; z: number }
): Array<{ x: number; y: number; z: number }> {
  return positions.map((p) => ({
    x: p.x - center.x,
    y: p.y - center.y,
    z: p.z - center.z,
  }))
}

/** Infer category from device types in a recipe */
export function inferCategory(devices: RecipeDevice[]): RecipeCategory {
  const types = devices.map((d) => d.deviceClass.toLowerCase()).join(' ')
  if (types.includes('spawn')) return 'spawning'
  if (types.includes('vending') || types.includes('granter') || types.includes('item')) return 'economy'
  if (types.includes('trigger') || types.includes('condition') || types.includes('button')) return 'triggers'
  if (types.includes('damage') || types.includes('elimination') || types.includes('weapon')) return 'combat'
  if (types.includes('hud') || types.includes('message') || types.includes('billboard')) return 'ui'
  if (types.includes('barrier') || types.includes('zone') || types.includes('volume')) return 'environment'
  if (types.includes('score') || types.includes('team') || types.includes('timer') || types.includes('round')) return 'gameplay'
  return 'custom'
}

/** Validate a recipe has the required structure */
export function validateRecipe(recipe: unknown): recipe is DeviceRecipe {
  if (!recipe || typeof recipe !== 'object') return false
  const r = recipe as Record<string, unknown>
  return (
    r.$schema === 'device-recipe-v1' &&
    typeof r.id === 'string' &&
    typeof r.name === 'string' &&
    Array.isArray(r.devices) &&
    r.devices.length > 0 &&
    Array.isArray(r.wiring)
  )
}
