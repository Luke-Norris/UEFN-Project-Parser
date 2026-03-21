import { useEffect, useRef, useCallback } from 'react'
import { Canvas, FabricImage, Rect, FabricText, Circle, config as fabricConfig, FabricObject } from 'fabric'
import { useCanvasStore } from '../stores/canvasStore'
import { useTemplateStore } from '../stores/templateStore'
import { useAssetStore } from '../stores/assetStore'
import type { ComponentTemplate, TemplateLayer } from '../templates/types'

// Ensure Fabric uses the real device pixel ratio for crisp rendering
fabricConfig.devicePixelRatio = window.devicePixelRatio || 1

// Disable object caching globally — cached objects render at a fixed
// resolution and look blurry / show colored fringe on high-DPI displays.
// For a design tool with a modest number of objects this is fine perf-wise.
FabricObject.ownDefaults.objectCaching = false

// Force all objects to render without anti-aliased edges on the transform border
FabricObject.ownDefaults.noScaleCache = true
FabricObject.ownDefaults.strokeUniform = true

// Snap guide state — shared between event handlers and render hook
let snapGuides: { x: number | null; y: number | null } = { x: null, y: null }

// Track the snap offset between frames so we can recover the raw mouse-driven
// position on the next move event.  This prevents the classic "snap jitter"
// where the previous frame's snap delta gets baked into the next move delta.
let moveSnapOffset = { x: 0, y: 0 }

// Hysteresis snap targets — once locked to a guide line, we stay snapped
// until the raw position moves beyond SNAP_RELEASE distance. This prevents
// oscillation when multiple edges compete for different snap targets.
let stickySnapX: number | null = null
let stickySnapY: number | null = null

const SNAP_ENGAGE = 6 // Distance to start snapping
const SNAP_RELEASE = 14 // Distance to release snap (must be > ENGAGE)

export function useFabricCanvas() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const fabricRef = useRef<Canvas | null>(null)
  const {
    setCanvas,
    pushHistory,
    setSelectedObjectId,
    undo,
    redo,
    copyObject,
    pasteObject,
    deleteSelected,
    duplicateSelected,
    groupSelected,
    ungroupSelected
  } = useCanvasStore()
  const { templateWidth, templateHeight } = useTemplateStore()

  // Initialize canvas
  useEffect(() => {
    if (!canvasRef.current) return

    const canvas = new Canvas(canvasRef.current, {
      width: templateWidth,
      height: templateHeight,
      backgroundColor: 'transparent',
      selection: true,
      preserveObjectStacking: true,
      enableRetinaScaling: true,
      imageSmoothingEnabled: false
    })

    canvas.on('selection:created', (e) => {
      const obj = e.selected?.[0]
      if (obj) setSelectedObjectId((obj as any).layerId || null)
    })

    canvas.on('selection:updated', (e) => {
      const obj = e.selected?.[0]
      if (obj) setSelectedObjectId((obj as any).layerId || null)
    })

    canvas.on('selection:cleared', () => {
      setSelectedObjectId(null)
    })

    canvas.on('object:modified', () => {
      // Clear guides, snap offset & hysteresis when interaction ends
      snapGuides = { x: null, y: null }
      moveSnapOffset = { x: 0, y: 0 }
      stickySnapX = null
      stickySnapY = null
      canvas.renderAll()
      pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    })

    // ----------------------------------------------------------
    // Snapping during move: element snap + grid snap
    //
    // Uses a "snap offset" technique to prevent jitter:
    //   1. Undo previous frame's snap to recover the raw mouse position
    //   2. Calculate new snap from the clean position
    //   3. Apply snap and store the offset for next frame
    // This matches how resize snapping feels — smooth and predictable.
    // ----------------------------------------------------------
    canvas.on('object:moving', (e) => {
      const state = useCanvasStore.getState()
      const obj = e.target
      if (!obj) return

      // Recover the raw (unsnapped) position by undoing previous snap
      const rawLeft = (obj.left ?? 0) - moveSnapOffset.x
      const rawTop = (obj.top ?? 0) - moveSnapOffset.y

      // Reset to raw position for edge calculations
      obj.set({ left: rawLeft, top: rawTop })
      obj.setCoords()

      let newSnapX = 0
      let newSnapY = 0
      let guideX: number | null = null
      let guideY: number | null = null

      // --- Element-to-element + canvas-edge snapping with hysteresis ---
      if (state.elementSnap) {
        const bound = obj.getBoundingRect()
        const objEdgesX = [bound.left, bound.left + bound.width / 2, bound.left + bound.width]
        const objEdgesY = [bound.top, bound.top + bound.height / 2, bound.top + bound.height]

        // Collect all snap target lines (other objects + canvas edges)
        const targetLinesX: number[] = []
        const targetLinesY: number[] = []

        for (const other of canvas.getObjects()) {
          if (other === obj) continue
          const ob = other.getBoundingRect()
          targetLinesX.push(ob.left, ob.left + ob.width / 2, ob.left + ob.width)
          targetLinesY.push(ob.top, ob.top + ob.height / 2, ob.top + ob.height)
        }

        // Canvas edges
        const cw = canvas.width!
        const ch = canvas.height!
        targetLinesX.push(0, cw / 2, cw)
        targetLinesY.push(0, ch / 2, ch)

        // --- X axis: hysteresis snap ---
        if (stickySnapX !== null) {
          // We're currently snapped — check if any edge is still within RELEASE
          let closestDist = Infinity
          let closestDelta = 0
          for (const edge of objEdgesX) {
            const d = Math.abs(edge - stickySnapX)
            if (d < closestDist) {
              closestDist = d
              closestDelta = stickySnapX - edge
            }
          }
          if (closestDist < SNAP_RELEASE) {
            // Stay snapped to this target
            newSnapX = closestDelta
            guideX = stickySnapX
          } else {
            // Release — too far from the sticky target
            stickySnapX = null
          }
        }

        if (stickySnapX === null) {
          // Not snapped — search for a new snap with ENGAGE threshold
          let bestDx: number | null = null
          let bestDxDist = Infinity
          let bestTargetX: number | null = null

          for (const edge of objEdgesX) {
            for (const target of targetLinesX) {
              const d = Math.abs(edge - target)
              if (d < SNAP_ENGAGE && d < bestDxDist) {
                bestDx = target - edge
                bestDxDist = d
                bestTargetX = target
              }
            }
          }

          if (bestDx !== null && bestTargetX !== null) {
            newSnapX = bestDx
            guideX = bestTargetX
            stickySnapX = bestTargetX
          }
        }

        // --- Y axis: hysteresis snap ---
        if (stickySnapY !== null) {
          let closestDist = Infinity
          let closestDelta = 0
          for (const edge of objEdgesY) {
            const d = Math.abs(edge - stickySnapY)
            if (d < closestDist) {
              closestDist = d
              closestDelta = stickySnapY - edge
            }
          }
          if (closestDist < SNAP_RELEASE) {
            newSnapY = closestDelta
            guideY = stickySnapY
          } else {
            stickySnapY = null
          }
        }

        if (stickySnapY === null) {
          let bestDy: number | null = null
          let bestDyDist = Infinity
          let bestTargetY: number | null = null

          for (const edge of objEdgesY) {
            for (const target of targetLinesY) {
              const d = Math.abs(edge - target)
              if (d < SNAP_ENGAGE && d < bestDyDist) {
                bestDy = target - edge
                bestDyDist = d
                bestTargetY = target
              }
            }
          }

          if (bestDy !== null && bestTargetY !== null) {
            newSnapY = bestDy
            guideY = bestTargetY
            stickySnapY = bestTargetY
          }
        }
      }

      // --- Grid snapping (only for axes not already element-snapped) ---
      if (state.snapToGrid && state.snapSize) {
        const sz = state.snapSize
        if (newSnapX === 0) {
          newSnapX = Math.round(rawLeft / sz) * sz - rawLeft
        }
        if (newSnapY === 0) {
          newSnapY = Math.round(rawTop / sz) * sz - rawTop
        }
      }

      // Apply snapped position
      obj.set({ left: rawLeft + newSnapX, top: rawTop + newSnapY })
      obj.setCoords()

      // Store offset for next frame
      moveSnapOffset = { x: newSnapX, y: newSnapY }
      snapGuides = { x: guideX, y: guideY }
    })

    // ----------------------------------------------------------
    // Snapping during resize / scale
    // ----------------------------------------------------------
    canvas.on('object:scaling', (e) => {
      const state = useCanvasStore.getState()
      if (!state.snapToGrid || !state.resizeSnapSize) return
      const obj = e.target
      if (!obj || !obj.width || !obj.height) return
      const sz = state.resizeSnapSize
      const w = obj.width * (obj.scaleX || 1)
      const h = obj.height * (obj.scaleY || 1)
      const snappedW = Math.max(sz, Math.round(w / sz) * sz)
      const snappedH = Math.max(sz, Math.round(h / sz) * sz)
      obj.set({
        scaleX: snappedW / obj.width,
        scaleY: snappedH / obj.height
      })
      obj.setCoords()
    })

    // ----------------------------------------------------------
    // Clear guides on mouse up
    // ----------------------------------------------------------
    canvas.on('mouse:up', () => {
      moveSnapOffset = { x: 0, y: 0 }
      stickySnapX = null
      stickySnapY = null
      if (snapGuides.x !== null || snapGuides.y !== null) {
        snapGuides = { x: null, y: null }
        canvas.renderAll()
      }
    })

    // ----------------------------------------------------------
    // Draw snap guide lines after each render
    // ----------------------------------------------------------
    canvas.on('after:render', () => {
      if (snapGuides.x === null && snapGuides.y === null) return
      const ctx = (canvas as any).contextContainer as CanvasRenderingContext2D | undefined
      if (!ctx) return

      ctx.save()
      ctx.strokeStyle = '#ff6b6b'
      ctx.lineWidth = 1
      ctx.setLineDash([3, 3])

      if (snapGuides.x !== null) {
        ctx.beginPath()
        ctx.moveTo(snapGuides.x + 0.5, 0)
        ctx.lineTo(snapGuides.x + 0.5, canvas.height!)
        ctx.stroke()
      }
      if (snapGuides.y !== null) {
        ctx.beginPath()
        ctx.moveTo(0, snapGuides.y + 0.5)
        ctx.lineTo(canvas.width!, snapGuides.y + 0.5)
        ctx.stroke()
      }
      ctx.restore()
    })

    fabricRef.current = canvas
    setCanvas(canvas)

    return () => {
      canvas.dispose()
      fabricRef.current = null
      setCanvas(null)
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return

      const ctrl = e.ctrlKey || e.metaKey

      if (ctrl && e.key === 'z') {
        e.preventDefault()
        undo()
      } else if (ctrl && (e.key === 'y' || (e.shiftKey && e.key === 'Z'))) {
        e.preventDefault()
        redo()
      } else if (ctrl && e.key === 'c') {
        e.preventDefault()
        copyObject()
      } else if (ctrl && e.key === 'v') {
        e.preventDefault()
        pasteObject()
      } else if (ctrl && e.key === 'd') {
        e.preventDefault()
        duplicateSelected()
      } else if (ctrl && e.key === 'g' && !e.shiftKey) {
        e.preventDefault()
        groupSelected()
      } else if (ctrl && e.key === 'g' && e.shiftKey) {
        e.preventDefault()
        ungroupSelected()
      } else if (ctrl && e.shiftKey && e.key === 'G') {
        e.preventDefault()
        ungroupSelected()
      } else if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault()
        deleteSelected()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [undo, redo, copyObject, pasteObject, deleteSelected, duplicateSelected, groupSelected, ungroupSelected])

  // Update canvas size when template dimensions change
  useEffect(() => {
    if (!fabricRef.current) return
    fabricRef.current.setDimensions({ width: templateWidth, height: templateHeight })
    fabricRef.current.renderAll()
  }, [templateWidth, templateHeight])

  // Load a template onto the canvas
  const loadTemplate = useCallback(
    async (template: ComponentTemplate) => {
      const canvas = fabricRef.current
      if (!canvas) return

      canvas.clear()
      canvas.setDimensions({ width: template.width, height: template.height })
      canvas.backgroundColor = 'transparent'

      for (const layer of template.layers) {
        await addLayerToCanvas(canvas, layer)
      }

      canvas.renderAll()
      pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    },
    [pushHistory]
  )

  // Swap an image on a specific layer (by asset file path)
  const swapLayerImage = useCallback(
    async (layerId: string, imagePath: string) => {
      const canvas = fabricRef.current
      if (!canvas) return

      const dataUrl = await window.electronAPI.getAssetData(imagePath)
      if (!dataUrl) return

      const objects = canvas.getObjects()
      const target = objects.find((obj) => (obj as any).layerId === layerId)
      if (!target) return

      const img = await FabricImage.fromURL(dataUrl)
      img.set({
        left: target.left,
        top: target.top,
        scaleX: (target.width! * (target.scaleX || 1)) / img.width!,
        scaleY: (target.height! * (target.scaleY || 1)) / img.height!,
        selectable: (target as any).selectable
      })
      ;(img as any).layerId = layerId
      ;(img as any).layerName = (target as any).layerName

      const idx = objects.indexOf(target)
      canvas.remove(target)
      canvas.insertAt(idx, img)
      canvas.renderAll()
      pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    },
    [pushHistory]
  )

  return { canvasRef, fabricRef, loadTemplate, swapLayerImage }
}

async function addLayerToCanvas(canvas: Canvas, layer: TemplateLayer): Promise<void> {
  if (layer.type === 'rect') {
    const rect = new Rect({
      left: Math.round(layer.left),
      top: Math.round(layer.top),
      width: Math.round(layer.width),
      height: Math.round(layer.height),
      fill: layer.rectFill || '#000000',
      stroke: null,
      strokeWidth: 0,
      rx: layer.cornerRadius || 0,
      ry: layer.cornerRadius || 0,
      opacity: layer.opacity ?? 1,
      selectable: !layer.locked,
      objectCaching: false,
      noScaleCache: true
    })
    ;(rect as any).layerId = layer.id
    ;(rect as any).layerName = layer.name
    ;(rect as any).widgetType = layer.widgetType
    canvas.add(rect)
  } else if (layer.type === 'text') {
    const text = new FabricText(layer.text || '', {
      left: layer.left,
      top: layer.top,
      fontSize: layer.fontSize || 24,
      fontFamily: layer.fontFamily || 'sans-serif',
      fill: layer.fill || '#FFFFFF',
      stroke: layer.stroke || null,
      strokeWidth: layer.strokeWidth || 0,
      textAlign: layer.textAlign || 'left',
      originX: layer.textAlign === 'center' ? 'center' : 'left',
      selectable: !layer.locked
    })
    ;(text as any).layerId = layer.id
    ;(text as any).layerName = layer.name
    ;(text as any).widgetType = layer.widgetType
    canvas.add(text)
  } else if (layer.type === 'image' && layer.defaultAsset) {
    try {
      const assetPath = findAssetPath(layer.assetCategory || '', layer.defaultAsset)
      if (assetPath) {
        const dataUrl = await window.electronAPI.getAssetData(assetPath)
        if (dataUrl) {
          const img = await FabricImage.fromURL(dataUrl)
          img.set({
            left: layer.left,
            top: layer.top,
            scaleX: layer.width / img.width!,
            scaleY: layer.height / img.height!,
            selectable: !layer.locked
          })
          ;(img as any).layerId = layer.id
          ;(img as any).layerName = layer.name
          ;(img as any).widgetType = layer.widgetType
          canvas.add(img)
          return
        }
      }
    } catch {
      // Fall through to placeholder
    }
    addPlaceholder(canvas, layer)
  } else {
    addPlaceholder(canvas, layer)
  }
}

function addPlaceholder(canvas: Canvas, layer: TemplateLayer): void {
  const rect = new Rect({
    left: layer.left,
    top: layer.top,
    width: layer.width,
    height: layer.height,
    fill: 'rgba(255,255,255,0.05)',
    stroke: '#444',
    strokeWidth: 1,
    strokeDashArray: [5, 5],
    selectable: !layer.locked
  })
  ;(rect as any).layerId = layer.id
  ;(rect as any).layerName = layer.name
  ;(rect as any).widgetType = layer.widgetType
  ;(rect as any).isPlaceholder = true
  canvas.add(rect)

  const label = new FabricText(layer.name, {
    left: layer.left + layer.width / 2,
    top: layer.top + layer.height / 2,
    fontSize: 14,
    fill: '#666',
    originX: 'center',
    originY: 'center',
    selectable: false,
    evented: false
  })
  ;(label as any).layerId = layer.id + '-label'
  ;(label as any).isPlaceholderLabel = true
  canvas.add(label)
}

function findAssetPath(category: string, filename: string): string | null {
  const store = useAssetStore.getState()
  if (!store.index) return null

  const assets = store.findAssetsByPattern(category, filename.replace(/\.\w+$/, ''))
  return assets.length > 0 ? assets[0].path : null
}
