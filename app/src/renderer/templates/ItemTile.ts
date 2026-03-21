import type { ComponentTemplate } from './types'
import { FORTNITE_FONTS } from '../styles/fortnite-theme'

export const ItemTileTemplate: ComponentTemplate = {
  id: 'item-tile',
  name: 'Item Tile',
  description: 'Weapon/item tile with rarity background, icon, and name label',
  category: 'item',
  width: 512,
  height: 512,
  layers: [
    {
      id: 'rarity-bg',
      type: 'image',
      name: 'Rarity Background',
      left: 0,
      top: 0,
      width: 512,
      height: 512,
      assetCategory: 'Rarity Assets',
      assetFilter: 'Weapon Rarity Background',
      defaultAsset: 'Legendary Weapon Rarity Background.png',
      editable: true,
      swappable: true,
      locked: true
    },
    {
      id: 'item-icon',
      type: 'image',
      name: 'Item Icon',
      left: 56,
      top: 40,
      width: 400,
      height: 320,
      assetCategory: 'Assault Rifles',
      defaultAsset: undefined,
      editable: true,
      swappable: true,
      locked: false
    },
    {
      id: 'item-name',
      type: 'text',
      name: 'Item Name',
      left: 256,
      top: 400,
      width: 440,
      height: 60,
      text: 'ITEM NAME',
      fontFamily: FORTNITE_FONTS.black,
      fontSize: 42,
      fill: '#FFFFFF',
      stroke: '#000000',
      strokeWidth: 2,
      textAlign: 'center',
      editable: true,
      swappable: false,
      locked: false
    },
    {
      id: 'item-subtitle',
      type: 'text',
      name: 'Subtitle',
      left: 256,
      top: 455,
      width: 440,
      height: 40,
      text: 'Assault Rifle',
      fontFamily: FORTNITE_FONTS.bold,
      fontSize: 24,
      fill: '#CCCCCC',
      textAlign: 'center',
      editable: true,
      swappable: false,
      locked: false
    }
  ],
  variables: [
    {
      id: 'var-rarity-bg',
      name: 'Rarity Background',
      type: 'image',
      defaultValue: '',
      layerId: 'rarity-bg',
      layerProperty: 'src'
    },
    {
      id: 'var-item-icon',
      name: 'Item Icon',
      type: 'image',
      defaultValue: '',
      layerId: 'item-icon',
      layerProperty: 'src'
    },
    {
      id: 'var-item-name',
      name: 'Item Name',
      type: 'text',
      defaultValue: 'ITEM NAME',
      layerId: 'item-name',
      layerProperty: 'text'
    },
    {
      id: 'var-item-subtitle',
      name: 'Subtitle',
      type: 'text',
      defaultValue: 'Assault Rifle',
      layerId: 'item-subtitle',
      layerProperty: 'text'
    },
    {
      id: 'var-item-name-color',
      name: 'Name Color',
      type: 'color',
      defaultValue: '#FFFFFF',
      layerId: 'item-name',
      layerProperty: 'fill'
    },
    {
      id: 'var-item-name-size',
      name: 'Name Font Size',
      type: 'number',
      defaultValue: '42',
      layerId: 'item-name',
      layerProperty: 'fontSize'
    }
  ]
}
