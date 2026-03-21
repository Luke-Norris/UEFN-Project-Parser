import type { ComponentTemplate } from './types'
import { ItemTileTemplate } from './ItemTile'
import { CombatEventTemplate } from './CombatEvent'

export const templateRegistry: ComponentTemplate[] = [
  ItemTileTemplate,
  CombatEventTemplate
]

export function getTemplate(id: string): ComponentTemplate | undefined {
  return templateRegistry.find((t) => t.id === id)
}
