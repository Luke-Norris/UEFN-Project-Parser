/**
 * UMG Widget containment rules and property definitions.
 * Derived from WIDGET_RULES.md — 883 real Widget Blueprints across 61 UEFN projects.
 */

import type { WidgetNodeType } from './widget-spec'

// === Slot types ===

export type SlotType =
  | 'CanvasPanelSlot'
  | 'OverlaySlot'
  | 'StackBoxSlot'
  | 'GridSlot'
  | 'SizeBoxSlot'
  | 'ScaleBoxSlot'

// === Property type definitions ===

export type WidgetPropertyType =
  | 'string'
  | 'color'
  | 'number'
  | 'boolean'
  | 'enum'
  | 'padding'

export interface WidgetPropertyDef {
  key: string
  label: string
  type: WidgetPropertyType
  /** For enum types — list of valid values */
  options?: string[]
  /** Default value */
  defaultValue?: string | number | boolean
  /** For number types — minimum value */
  min?: number
  /** For number types — maximum value */
  max?: number
  /** For number types — step increment */
  step?: number
  /** Which Fabric.js property this maps to (if direct) */
  fabricProp?: string
}

export interface SlotPropertyDef {
  key: string
  label: string
  type: WidgetPropertyType
  options?: string[]
  defaultValue?: string | number | boolean
}

// === Widget type info ===

export interface WidgetTypeInfo {
  type: WidgetNodeType
  label: string
  isContainer: boolean
  /** Max children: -1 = unlimited, 1 = single child */
  maxChildren: number
  slotType: SlotType | null
  allowedChildren: WidgetNodeType[]
  icon: string // short icon identifier for the header
}

export const WIDGET_TYPE_INFO: Record<string, WidgetTypeInfo> = {
  CanvasPanel: {
    type: 'CanvasPanel',
    label: 'Canvas Panel',
    isContainer: true,
    maxChildren: -1,
    slotType: 'CanvasPanelSlot',
    allowedChildren: [
      'CanvasPanel',
      'Image',
      'Overlay',
      'TextBlock',
      'ButtonLoud',
      'ButtonQuiet',
      'StackBox',
      'SizeBox',
      'GridPanel'
    ],
    icon: 'canvas'
  },
  Overlay: {
    type: 'Overlay',
    label: 'Overlay',
    isContainer: true,
    maxChildren: -1,
    slotType: 'OverlaySlot',
    allowedChildren: ['Image', 'Overlay', 'StackBox', 'TextBlock', 'ButtonRegular'],
    icon: 'overlay'
  },
  StackBox: {
    type: 'StackBox',
    label: 'Stack Box',
    isContainer: true,
    maxChildren: -1,
    slotType: 'StackBoxSlot',
    allowedChildren: ['Image', 'Overlay', 'StackBox', 'TextBlock', 'ButtonQuiet'],
    icon: 'stack'
  },
  GridPanel: {
    type: 'GridPanel',
    label: 'Grid Panel',
    isContainer: true,
    maxChildren: -1,
    slotType: 'GridSlot',
    allowedChildren: ['Image'],
    icon: 'grid'
  },
  SizeBox: {
    type: 'SizeBox',
    label: 'Size Box',
    isContainer: true,
    maxChildren: 1,
    slotType: 'SizeBoxSlot',
    allowedChildren: ['Image', 'Overlay', 'SizeBox'],
    icon: 'size'
  },
  ScaleBox: {
    type: 'ScaleBox',
    label: 'Scale Box',
    isContainer: true,
    maxChildren: 1,
    slotType: 'ScaleBoxSlot',
    allowedChildren: ['CanvasPanel'],
    icon: 'scale'
  },
  Image: {
    type: 'Image',
    label: 'Image',
    isContainer: false,
    maxChildren: 0,
    slotType: null,
    allowedChildren: [],
    icon: 'image'
  },
  TextBlock: {
    type: 'TextBlock',
    label: 'Text Block',
    isContainer: false,
    maxChildren: 0,
    slotType: null,
    allowedChildren: [],
    icon: 'text'
  },
  ButtonLoud: {
    type: 'ButtonLoud',
    label: 'Button (Loud)',
    isContainer: false,
    maxChildren: 0,
    slotType: null,
    allowedChildren: [],
    icon: 'button'
  },
  ButtonQuiet: {
    type: 'ButtonQuiet',
    label: 'Button (Quiet)',
    isContainer: false,
    maxChildren: 0,
    slotType: null,
    allowedChildren: [],
    icon: 'button'
  },
  ButtonRegular: {
    type: 'ButtonRegular',
    label: 'Button (Regular)',
    isContainer: false,
    maxChildren: 0,
    slotType: null,
    allowedChildren: [],
    icon: 'button'
  }
}

// === Slot properties per slot type ===

export const SLOT_PROPERTIES: Record<string, SlotPropertyDef[]> = {
  CanvasPanelSlot: [
    {
      key: 'anchorPreset',
      label: 'Anchor',
      type: 'enum',
      options: [
        'TopLeft',
        'TopCenter',
        'TopRight',
        'CenterLeft',
        'Center',
        'CenterRight',
        'BottomLeft',
        'BottomCenter',
        'BottomRight',
        'FullScreen'
      ],
      defaultValue: 'TopLeft'
    },
    { key: 'offsetLeft', label: 'Left', type: 'number', defaultValue: 0 },
    { key: 'offsetTop', label: 'Top', type: 'number', defaultValue: 0 },
    { key: 'offsetRight', label: 'Right', type: 'number', defaultValue: 0 },
    { key: 'offsetBottom', label: 'Bottom', type: 'number', defaultValue: 0 },
    { key: 'alignmentX', label: 'Align X', type: 'number', defaultValue: 0 },
    { key: 'alignmentY', label: 'Align Y', type: 'number', defaultValue: 0 },
    { key: 'bAutoSize', label: 'Auto Size', type: 'boolean', defaultValue: false }
  ],
  OverlaySlot: [
    {
      key: 'slotHAlign',
      label: 'H Align',
      type: 'enum',
      options: ['Fill', 'Center', 'Left', 'Right'],
      defaultValue: 'Fill'
    },
    {
      key: 'slotVAlign',
      label: 'V Align',
      type: 'enum',
      options: ['Fill', 'Center', 'Top', 'Bottom'],
      defaultValue: 'Fill'
    },
    { key: 'slotPadding', label: 'Padding', type: 'padding', defaultValue: 0 }
  ],
  StackBoxSlot: [
    {
      key: 'slotHAlign',
      label: 'H Align',
      type: 'enum',
      options: ['Fill', 'Center', 'Left'],
      defaultValue: 'Fill'
    },
    {
      key: 'slotVAlign',
      label: 'V Align',
      type: 'enum',
      options: ['Fill', 'Center', 'Top'],
      defaultValue: 'Fill'
    },
    { key: 'slotPadding', label: 'Padding', type: 'padding', defaultValue: 0 },
    {
      key: 'slotSizeRule',
      label: 'Size',
      type: 'enum',
      options: ['Auto', 'Fill'],
      defaultValue: 'Auto'
    }
  ],
  GridSlot: [
    {
      key: 'slotHAlign',
      label: 'H Align',
      type: 'enum',
      options: ['Center'],
      defaultValue: 'Center'
    },
    {
      key: 'slotVAlign',
      label: 'V Align',
      type: 'enum',
      options: ['Center'],
      defaultValue: 'Center'
    },
    { key: 'slotPadding', label: 'Padding', type: 'padding', defaultValue: 0 }
  ],
  SizeBoxSlot: [],
  ScaleBoxSlot: []
}

// === Widget-specific editable properties ===

export const WIDGET_PROPERTIES: Record<string, WidgetPropertyDef[]> = {
  TextBlock: [
    { key: 'text', label: 'Text', type: 'string', fabricProp: 'text', defaultValue: '' },
    {
      key: 'umgJustification',
      label: 'Justify',
      type: 'enum',
      options: ['Left', 'Center', 'Right'],
      defaultValue: 'Center'
    },
    {
      key: 'fill',
      label: 'Color',
      type: 'color',
      fabricProp: 'fill',
      defaultValue: '#FFFFFF'
    },
    {
      key: 'umgVisibility',
      label: 'Visibility',
      type: 'enum',
      options: ['Visible', 'Collapsed', 'Hidden'],
      defaultValue: 'Visible'
    },
    {
      key: 'displayLabel',
      label: 'Display Label',
      type: 'string',
      defaultValue: ''
    }
  ],
  Image: [
    {
      key: 'tintColor',
      label: 'Tint Color',
      type: 'color',
      fabricProp: 'fill',
      defaultValue: '#FFFFFF'
    },
    {
      key: 'texturePath',
      label: 'Texture',
      type: 'string',
      defaultValue: ''
    },
    {
      key: 'displayLabel',
      label: 'Display Label',
      type: 'string',
      defaultValue: ''
    }
  ],
  ButtonLoud: [
    { key: 'text', label: 'Text', type: 'string', fabricProp: 'text', defaultValue: 'BUTTON' },
    { key: 'minWidth', label: 'Min Width', type: 'number', defaultValue: 190, min: 0 },
    { key: 'minHeight', label: 'Min Height', type: 'number', defaultValue: 80, min: 0 }
  ],
  ButtonQuiet: [
    { key: 'text', label: 'Text', type: 'string', fabricProp: 'text', defaultValue: 'BUTTON' },
    { key: 'minWidth', label: 'Min Width', type: 'number', defaultValue: 180, min: 0 },
    { key: 'minHeight', label: 'Min Height', type: 'number', defaultValue: 56, min: 0 },
    {
      key: 'umgVisibility',
      label: 'Visibility',
      type: 'enum',
      options: ['Visible', 'Collapsed', 'Hidden'],
      defaultValue: 'Visible'
    }
  ],
  ButtonRegular: [
    { key: 'text', label: 'Text', type: 'string', fabricProp: 'text', defaultValue: 'BUTTON' }
  ],
  CanvasPanel: [
    {
      key: 'displayLabel',
      label: 'Display Label',
      type: 'string',
      defaultValue: ''
    }
  ],
  Overlay: [
    {
      key: 'displayLabel',
      label: 'Display Label',
      type: 'string',
      defaultValue: ''
    }
  ],
  StackBox: [
    {
      key: 'umgOrientation',
      label: 'Orientation',
      type: 'enum',
      options: ['Horizontal', 'Vertical'],
      defaultValue: 'Horizontal'
    },
    {
      key: 'displayLabel',
      label: 'Display Label',
      type: 'string',
      defaultValue: ''
    }
  ],
  SizeBox: [
    {
      key: 'sizeBoxWidth',
      label: 'Width Override',
      type: 'number',
      defaultValue: 0,
      min: 0
    },
    {
      key: 'bOverrideWidth',
      label: 'Enable Width',
      type: 'boolean',
      defaultValue: false
    },
    {
      key: 'sizeBoxHeight',
      label: 'Height Override',
      type: 'number',
      defaultValue: 0,
      min: 0
    },
    {
      key: 'bOverrideHeight',
      label: 'Enable Height',
      type: 'boolean',
      defaultValue: false
    }
  ],
  GridPanel: [],
  ScaleBox: []
}

// === Helpers ===

/**
 * Get widget type info. Handles the StackBox(H)/StackBox(V) naming from UMGWidgetConverter.
 */
export function getWidgetInfo(widgetType: string): WidgetTypeInfo | undefined {
  // Normalize StackBox(H) / StackBox(V) → StackBox
  const normalized = widgetType.replace(/\(H\)|\(V\)/, '')
  return WIDGET_TYPE_INFO[normalized]
}

/**
 * Get the slot properties for a given parent widget type.
 * Returns the slot property definitions children of that parent would use.
 */
export function getSlotProperties(parentWidgetType: string): SlotPropertyDef[] {
  const info = getWidgetInfo(parentWidgetType)
  if (!info?.slotType) return []
  return SLOT_PROPERTIES[info.slotType] || []
}

/**
 * Get the widget-specific editable properties for a given widget type.
 */
export function getWidgetProperties(widgetType: string): WidgetPropertyDef[] {
  const normalized = widgetType.replace(/\(H\)|\(V\)/, '')
  return WIDGET_PROPERTIES[normalized] || []
}

/**
 * Get the display name for a widget type (handles variant naming).
 */
export function getWidgetDisplayName(widgetType: string): string {
  if (widgetType.startsWith('StackBox(')) {
    const orient = widgetType.includes('(H)') ? 'Horizontal' : 'Vertical'
    return `Stack Box (${orient})`
  }
  const info = WIDGET_TYPE_INFO[widgetType]
  return info?.label || widgetType
}

/**
 * Check if a child type is allowed within a parent type.
 */
export function isAllowedChild(parentType: string, childType: string): boolean {
  const info = getWidgetInfo(parentType)
  if (!info) return false
  const normalChild = childType.replace(/\(H\)|\(V\)/, '') as WidgetNodeType
  return info.allowedChildren.includes(normalChild)
}
