/**
 * Converts a WidgetSpec JSON (from FortniteForge) into a ComponentTemplate
 * that the UEFN UI Component Creator can render on the Fabric.js canvas.
 *
 * Mapping:
 *   WidgetSpecNode  →  TemplateLayer
 *   WidgetSpecVariable  →  TemplateVariable (with layerId = widgetName)
 *
 * Layout strategy:
 *   - CanvasPanel children use absolute positioning (offsetLeft/Top)
 *   - StackBox children are auto-laid-out vertically or horizontally
 *   - Overlay children are stacked at the same position
 *   - Containers (CanvasPanel, StackBox, Overlay) become 'rect' layers with no fill
 */

import type { ComponentTemplate, TemplateLayer, TemplateVariable } from '../../shared/types'
import type { WidgetSpecJson, WidgetSpecNode, WidgetSpecVariable } from '../../shared/widget-spec'

// Fortnite UI defaults
const DEFAULT_FONT = 'Burbank Big Condensed'
const DEFAULT_TEXT_COLOR = '#FFFFFF'
const DEFAULT_TEXT_SIZE = 24
const BUTTON_BG_COLORS: Record<string, string> = {
  Loud: '#f5a623',
  Quiet: '#333333',
  Regular: '#555555'
}
const BUTTON_TEXT_COLORS: Record<string, string> = {
  Loud: '#000000',
  Quiet: '#FFFFFF',
  Regular: '#FFFFFF'
}

/**
 * Convert a WidgetSpec JSON to a ComponentTemplate.
 */
export function widgetSpecToTemplate(spec: WidgetSpecJson): ComponentTemplate {
  const layers: TemplateLayer[] = []
  const variables: TemplateVariable[] = []

  // Flatten the widget tree into layers with computed positions and hierarchy
  flattenNode(spec.root, 0, 0, spec.width, spec.height, layers, undefined, 0)

  // Convert WidgetSpec variables to TemplateVariables
  for (const v of spec.variables) {
    variables.push(specVarToTemplateVar(v))
  }

  return {
    id: `umg-${spec.name.toLowerCase().replace(/\s+/g, '-')}-${Date.now()}`,
    name: spec.name,
    description: `UMG Widget Blueprint: ${spec.name}`,
    category: 'umg',
    width: spec.width,
    height: spec.height,
    layers,
    variables
  }
}

/**
 * Convert a ComponentTemplate back to WidgetSpec JSON.
 * Used when exporting the edited canvas back to FortniteForge.
 */
export function templateToWidgetSpec(
  template: ComponentTemplate,
  variableValues: Record<string, string>
): WidgetSpecJson {
  // Rebuild from the original spec structure if available,
  // otherwise create a flat CanvasPanel with all layers as children
  const variables: WidgetSpecVariable[] = (template.variables || []).map((v) => ({
    id: v.id,
    name: v.name,
    type: v.type as WidgetSpecVariable['type'],
    defaultValue: variableValues[v.id] ?? v.defaultValue,
    widgetName: v.layerId,
    widgetProperty: templatePropToWidgetProp(v.layerProperty)
  }))

  // Build nodes from layers
  const children: WidgetSpecNode[] = template.layers.map((layer) =>
    layerToSpecNode(layer, variableValues, template.variables || [])
  )

  return {
    $schema: 'widget-spec-v1',
    name: template.name,
    width: template.width,
    height: template.height,
    variables,
    root: {
      type: 'CanvasPanel',
      name: 'Root',
      children
    }
  }
}

// === Internal helpers ===

interface LayoutContext {
  x: number
  y: number
  availableWidth: number
  availableHeight: number
}

/** Spacing constants for auto-layout */
const PAD = 16
const GAP = 10
const TEXT_H = 36
const BUTTON_H = 48
const ICON_SIZE = 80

/** Is this node a background fill? (tinted image, no texture, no children) */
function isBgFill(node: WidgetSpecNode): boolean {
  return node.type === 'Image' && !!node.tintColor && !node.texturePath
}

/** Is this node a content leaf (not a container)? */
function isLeaf(node: WidgetSpecNode): boolean {
  return !node.children || node.children.length === 0
}

/**
 * Estimate intrinsic height of a node (ignoring background fills).
 */
function estimateH(node: WidgetSpecNode): number {
  if (node.minHeight) return node.minHeight

  switch (node.type) {
    case 'TextBlock':
      return TEXT_H
    case 'ButtonLoud':
    case 'ButtonQuiet':
    case 'ButtonRegular':
      return BUTTON_H
    case 'Image':
      if (isBgFill(node)) return 0 // backgrounds don't contribute to content height
      return ICON_SIZE
    case 'Overlay': {
      // We stack content vertically inside overlays, so sum their heights
      const contentChildren = (node.children || []).filter((c) => !isBgFill(c))
      if (contentChildren.length === 0) return ICON_SIZE
      const innerPad = 6
      return contentChildren.reduce((sum, c) => sum + estimateH(c) + 4, innerPad * 2 - 4)
    }
    case 'StackBox': {
      const children = node.children || []
      if (children.length === 0) return 40
      if (node.orientation === 'Horizontal') {
        return children.reduce((max, c) => Math.max(max, estimateH(c)), 0)
      }
      return children.reduce((sum, c) => sum + estimateH(c) + GAP, -GAP)
    }
    default:
      return 60
  }
}

/**
 * Recursively flatten a WidgetSpecNode tree into positioned TemplateLayer[].
 * Tracks parentId and depth for hierarchy display in the Layers panel.
 */
function flattenNode(
  node: WidgetSpecNode,
  x: number,
  y: number,
  w: number,
  h: number,
  layers: TemplateLayer[],
  parentId: string | undefined,
  depth: number
): void {
  switch (node.type) {
    case 'CanvasPanel': {
      const myId = node.name
      if (node.name !== 'Root') {
        const l = makeRectLayer(node, x, y, w, h, 'transparent')
        l.parentId = parentId
        l.depth = depth
        l.widgetType = 'CanvasPanel'
        l.isContainer = true
        layers.push(l)
      } else {
        // Root doesn't get a visual layer but is the hierarchy root
      }

      const children = node.children || []
      const bgs = children.filter(isBgFill)
      const content = children.filter((c) => !isBgFill(c))

      for (const bg of bgs) {
        const l = makeImageLayer(bg, x, y, w, h)
        l.parentId = myId
        l.depth = depth + 1
        l.widgetType = 'Image'
        layers.push(l)
      }

      let cy = y + PAD
      for (const child of content) {
        const ch = estimateH(child)
        flattenNode(child, x + PAD, cy, w - PAD * 2, ch, layers, myId, depth + 1)
        cy += ch + GAP
      }
      break
    }

    case 'StackBox': {
      const myId = node.name
      const orient = node.orientation === 'Horizontal' ? 'H' : 'V'
      const l = makeRectLayer(node, x, y, w, h, 'transparent')
      l.parentId = parentId
      l.depth = depth
      l.widgetType = `StackBox(${orient})`
      l.isContainer = true
      l.name = `${node.name}`
      layers.push(l)

      const isH = node.orientation === 'Horizontal'
      const children = node.children || []
      if (children.length === 0) break

      if (isH) {
        const childW = (w - GAP * (children.length - 1)) / children.length
        let cx = x
        for (const child of children) {
          flattenNode(child, cx, y, childW, h, layers, myId, depth + 1)
          cx += childW + GAP
        }
      } else {
        let cy = y
        for (const child of children) {
          const ch = estimateH(child)
          flattenNode(child, x, cy, w, ch, layers, myId, depth + 1)
          cy += ch + GAP
        }
      }
      break
    }

    case 'Overlay': {
      const myId = node.name
      const l = makeRectLayer(node, x, y, w, h, 'transparent')
      l.parentId = parentId
      l.depth = depth
      l.widgetType = 'Overlay'
      l.isContainer = true
      layers.push(l)

      const children = node.children || []
      const bgs = children.filter(isBgFill)
      const content = children.filter((c) => !isBgFill(c))

      for (const bg of bgs) {
        const bl = makeImageLayer(bg, x, y, w, h)
        bl.parentId = myId
        bl.depth = depth + 1
        bl.widgetType = 'Image'
        layers.push(bl)
      }

      const innerPad = 6
      let cy = y + innerPad
      for (const child of content) {
        const ch = estimateH(child)
        flattenNode(child, x + innerPad, cy, w - innerPad * 2, ch, layers, myId, depth + 1)
        cy += ch + 4
      }
      break
    }

    case 'SizeBox': {
      const sw = node.minWidth || w
      const sh = node.minHeight || h
      for (const child of node.children || []) {
        flattenNode(child, x, y, sw, sh, layers, node.name, depth + 1)
      }
      break
    }

    case 'Image': {
      const l = makeImageLayer(node, x, y, w, h)
      l.parentId = parentId
      l.depth = depth
      l.widgetType = 'Image'
      layers.push(l)
      break
    }

    case 'TextBlock': {
      const l = makeTextLayer(node, x, y, w)
      l.parentId = parentId
      l.depth = depth
      l.widgetType = 'TextBlock'
      layers.push(l)
      break
    }

    case 'ButtonLoud':
    case 'ButtonQuiet':
    case 'ButtonRegular':
      makeButtonLayers(node, x, y, w, layers, parentId, depth)
      break
  }
}

function makeRectLayer(
  node: WidgetSpecNode,
  x: number,
  y: number,
  w: number,
  h: number,
  fill: string
): TemplateLayer {
  return {
    id: node.name,
    type: 'rect',
    name: `${node.type}: ${node.name}`,
    left: x,
    top: y,
    width: w,
    height: h,
    rectFill: fill,
    opacity: fill === 'transparent' ? 0.05 : 1, // faint outline for containers
    editable: true,
    swappable: false,
    locked: false
  }
}

function makeImageLayer(node: WidgetSpecNode, x: number, y: number, w: number, h: number): TemplateLayer {
  const isBg = isBgFill(node)
  const isIcon = !isBg && !node.texturePath

  // Icons get a reasonable square size, centered in their space
  let lx = x, ly = y, lw = w, lh = h
  if (isIcon) {
    const iconSize = Math.min(ICON_SIZE, w, h || ICON_SIZE)
    lx = x + (w - iconSize) / 2
    lw = iconSize
    lh = iconSize
  }

  return {
    id: node.name,
    type: isBg ? 'rect' : 'image',
    name: node.name,
    left: Math.round(lx),
    top: Math.round(ly),
    width: Math.round(lw || 100),
    height: Math.round(lh || 100),
    rectFill: node.tintColor || undefined,
    defaultAsset: node.texturePath || undefined,
    opacity: 1,
    cornerRadius: isBg ? 0 : 4,
    editable: true,
    swappable: !isBg,
    locked: false
  }
}

function makeTextLayer(node: WidgetSpecNode, x: number, y: number, w: number): TemplateLayer {
  // textAlign 'center' uses originX 'center' in Fabric, so left must be the midpoint
  return {
    id: node.name,
    type: 'text',
    name: node.name,
    left: Math.round(x + w / 2),
    top: Math.round(y),
    width: Math.round(w || 200),
    height: TEXT_H,
    text: node.text || '',
    fontFamily: DEFAULT_FONT,
    fontSize: DEFAULT_TEXT_SIZE,
    fill: DEFAULT_TEXT_COLOR,
    textAlign: 'center',
    editable: true,
    swappable: false,
    locked: false
  }
}

function makeButtonLayers(
  node: WidgetSpecNode,
  x: number,
  y: number,
  parentWidth: number,
  layers: TemplateLayer[],
  parentId?: string,
  depth?: number
): void {
  const style = node.buttonStyle || 'Quiet'
  const bw = node.minWidth || 150
  const bh = node.minHeight || BUTTON_H
  // Center button horizontally in parent
  const bx = Math.round(x + (parentWidth - bw) / 2)

  const d = depth ?? 0
  const btnType = node.type === 'ButtonLoud' ? 'ButtonLoud' : node.type === 'ButtonRegular' ? 'ButtonRegular' : 'ButtonQuiet'

  // Background rect
  layers.push({
    id: node.name + '_bg',
    type: 'rect',
    name: node.name + ' Bg',
    left: bx,
    top: Math.round(y),
    width: bw,
    height: bh,
    rectFill: BUTTON_BG_COLORS[style] || '#333333',
    cornerRadius: 6,
    opacity: 1,
    editable: true,
    swappable: false,
    locked: false,
    parentId: parentId,
    depth: d,
    widgetType: btnType
  })

  // Text label (centered on button)
  layers.push({
    id: node.name,
    type: 'text',
    name: node.name,
    left: bx + Math.round(bw / 2),
    top: Math.round(y + (bh - 24) / 2),
    width: bw,
    height: 24,
    text: node.text || 'BUTTON',
    fontFamily: DEFAULT_FONT,
    fontSize: 20,
    fill: BUTTON_TEXT_COLORS[style] || '#FFFFFF',
    textAlign: 'center',
    editable: true,
    swappable: false,
    locked: false,
    parentId: node.name + '_bg',
    depth: d + 1,
    widgetType: 'ButtonText'
  })
}

/**
 * Map a WidgetSpec variable to a TemplateVariable.
 * The key mapping: widgetName → layerId, widgetProperty → layerProperty.
 */
function specVarToTemplateVar(v: WidgetSpecVariable): TemplateVariable {
  return {
    id: v.id,
    name: v.name,
    type: v.type,
    defaultValue: v.defaultValue,
    layerId: v.widgetName, // Widget name IS the layer ID
    layerProperty: widgetPropToTemplateProp(v.widgetProperty)
  }
}

/**
 * Map widget property names to Fabric.js/template layer property names.
 */
function widgetPropToTemplateProp(widgetProp: string): string {
  switch (widgetProp) {
    case 'text':
      return 'text'
    case 'tintColor':
      return 'fill' // Maps to Fabric.js fill or rectFill
    case 'texturePath':
      return 'src'
    case 'visibility':
      return 'opacity'
    default:
      return widgetProp
  }
}

/**
 * Reverse mapping: template property → widget property.
 */
function templatePropToWidgetProp(templateProp: string): string {
  switch (templateProp) {
    case 'text':
      return 'text'
    case 'fill':
    case 'rectFill':
      return 'tintColor'
    case 'src':
      return 'texturePath'
    case 'opacity':
      return 'visibility'
    default:
      return templateProp
  }
}

/**
 * Convert a TemplateLayer back to a WidgetSpecNode.
 */
function layerToSpecNode(
  layer: TemplateLayer,
  variableValues: Record<string, string>,
  variables: TemplateVariable[]
): WidgetSpecNode {
  // Find bound variables for this layer
  const boundVars = variables.filter((v) => v.layerId === layer.id)

  // Determine node type from layer
  const isButton = layer.cornerRadius && layer.rectFill && layer.type === 'text'
  const type: WidgetSpecNode['type'] = isButton
    ? 'ButtonQuiet'
    : layer.type === 'text'
      ? 'TextBlock'
      : layer.type === 'image' || (layer.type === 'rect' && layer.swappable)
        ? 'Image'
        : 'Image' // Rect with fill → Image with tintColor

  const node: WidgetSpecNode = {
    type,
    name: layer.id,
    offsetLeft: layer.left,
    offsetTop: layer.top
  }

  // Apply current variable values or layer defaults
  if (type === 'TextBlock' || isButton) {
    const textVar = boundVars.find((v) => v.layerProperty === 'text')
    node.text = textVar ? (variableValues[textVar.id] ?? textVar.defaultValue) : layer.text
  }

  if (type === 'Image' && layer.rectFill) {
    const colorVar = boundVars.find((v) => v.layerProperty === 'fill')
    node.tintColor = colorVar ? (variableValues[colorVar.id] ?? colorVar.defaultValue) : layer.rectFill
  }

  if (layer.width) node.minWidth = Math.round(layer.width)
  if (layer.height) node.minHeight = Math.round(layer.height)

  return node
}
