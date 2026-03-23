/**
 * Widget Spec v1 — shared JSON format between FortniteForge and the UEFN UI Component Creator.
 *
 * FortniteForge generates this JSON from WidgetSpec (C#) and can consume it back to build .uasset files.
 * The Component Creator imports this JSON, converts it to a visual template with variable bindings,
 * and can export the edited state back to this format.
 */

// === WidgetSpec JSON types ===

export interface WidgetSpecJson {
  $schema: 'widget-spec-v1'
  name: string
  width: number
  height: number
  variables: WidgetSpecVariable[]
  root: WidgetSpecNode
}

export interface WidgetSpecVariable {
  id: string
  name: string
  type: 'text' | 'color' | 'image' | 'number'
  defaultValue: string
  /** Which widget node this variable targets (by name) */
  widgetName: string
  /** Which property on the widget: "text", "tintColor", "texturePath", "visibility" */
  widgetProperty: string
}

export type WidgetNodeType =
  | 'CanvasPanel'
  | 'Image'
  | 'TextBlock'
  | 'ButtonLoud'
  | 'ButtonQuiet'
  | 'ButtonRegular'
  | 'Overlay'
  | 'StackBox'
  | 'SizeBox'
  | 'ScaleBox'
  | 'GridPanel'

export interface WidgetSpecNode {
  type: WidgetNodeType
  name: string
  anchor?: string
  offsetLeft?: number
  offsetTop?: number
  offsetRight?: number
  offsetBottom?: number
  autoSize?: boolean
  tintColor?: string
  text?: string
  texturePath?: string
  buttonStyle?: string
  minWidth?: number
  minHeight?: number
  orientation?: 'Horizontal' | 'Vertical'
  padding?: number
  visibility?: string

  // Raw anchor values (0-1)
  anchorMinX?: number
  anchorMinY?: number
  anchorMaxX?: number
  anchorMaxY?: number

  // Slot alignment
  slotHAlign?: string
  slotVAlign?: string
  slotPadLeft?: number
  slotPadTop?: number
  slotPadRight?: number
  slotPadBottom?: number

  // Text visuals
  fontSize?: number
  fontWeight?: string
  textColor?: string
  justification?: string
  letterSpacing?: number
  outlineSize?: number
  outlineColor?: string

  // Image brush
  imageWidth?: number
  imageHeight?: number
  drawAs?: string
  cornerRadiusTL?: number
  cornerRadiusTR?: number
  cornerRadiusBL?: number
  cornerRadiusBR?: number

  // RenderTransform
  translateX?: number
  translateY?: number
  angle?: number
  scaleX?: number
  scaleY?: number

  // Rendering
  renderOpacity?: number

  children?: WidgetSpecNode[]
}
