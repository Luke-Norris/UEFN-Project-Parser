import type { ComponentTemplate } from './types'
import { FORTNITE_FONTS } from '../styles/fortnite-theme'

export const CombatEventTemplate: ComponentTemplate = {
  id: 'combat-event',
  name: 'Combat Event',
  description: 'Elimination/assist/win widget with swappable event icon',
  category: 'combat',
  width: 500,
  height: 100,
  layers: [
    {
      id: 'event-bg',
      type: 'rect',
      name: 'Background',
      left: 0,
      top: 0,
      width: 500,
      height: 100,
      rectFill: 'rgba(0, 0, 0, 0.75)',
      cornerRadius: 8,
      editable: true,
      swappable: false,
      locked: true
    },
    {
      id: 'event-accent',
      type: 'rect',
      name: 'Accent Bar',
      left: 0,
      top: 0,
      width: 4,
      height: 100,
      rectFill: '#c76b29',
      editable: true,
      swappable: false,
      locked: true
    },
    {
      id: 'event-icon',
      type: 'image',
      name: 'Event Icon',
      left: 20,
      top: 14,
      width: 72,
      height: 72,
      assetCategory: 'Icons',
      assetFilter: 'Icon',
      defaultAsset: 'Target Icon.png',
      editable: true,
      swappable: true,
      locked: false
    },
    {
      id: 'player-name',
      type: 'text',
      name: 'Player Name',
      left: 110,
      top: 22,
      width: 250,
      height: 40,
      text: 'PlayerName',
      fontFamily: FORTNITE_FONTS.black,
      fontSize: 28,
      fill: '#FFFFFF',
      textAlign: 'left',
      editable: true,
      swappable: false,
      locked: false
    },
    {
      id: 'event-description',
      type: 'text',
      name: 'Event Text',
      left: 110,
      top: 58,
      width: 250,
      height: 30,
      text: 'Eliminated EnemyPlayer',
      fontFamily: FORTNITE_FONTS.bold,
      fontSize: 18,
      fill: '#999999',
      textAlign: 'left',
      editable: true,
      swappable: false,
      locked: false
    },
    {
      id: 'weapon-icon',
      type: 'image',
      name: 'Weapon Used',
      left: 400,
      top: 18,
      width: 80,
      height: 64,
      assetCategory: 'Weapon Icons',
      defaultAsset: undefined,
      editable: true,
      swappable: true,
      locked: false
    }
  ],
  variables: [
    {
      id: 'var-event-icon',
      name: 'Event Icon',
      type: 'image',
      defaultValue: '',
      layerId: 'event-icon',
      layerProperty: 'src'
    },
    {
      id: 'var-player-name',
      name: 'Player Name',
      type: 'text',
      defaultValue: 'PlayerName',
      layerId: 'player-name',
      layerProperty: 'text'
    },
    {
      id: 'var-event-text',
      name: 'Event Text',
      type: 'text',
      defaultValue: 'Eliminated EnemyPlayer',
      layerId: 'event-description',
      layerProperty: 'text'
    },
    {
      id: 'var-weapon-icon',
      name: 'Weapon Icon',
      type: 'image',
      defaultValue: '',
      layerId: 'weapon-icon',
      layerProperty: 'src'
    },
    {
      id: 'var-accent-color',
      name: 'Accent Color',
      type: 'color',
      defaultValue: '#c76b29',
      layerId: 'event-accent',
      layerProperty: 'fill'
    },
    {
      id: 'var-player-name-color',
      name: 'Name Color',
      type: 'color',
      defaultValue: '#FFFFFF',
      layerId: 'player-name',
      layerProperty: 'fill'
    }
  ]
}
