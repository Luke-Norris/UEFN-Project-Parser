import { create } from 'zustand'
import type { Canvas as FabricCanvas, FabricObject } from 'fabric'

interface CopiedPropSet {
  label: string // e.g. 'Transform', 'Position', 'Fill', 'All'
  values: Record<string, any>
}

interface CanvasState {
  canvas: FabricCanvas | null
  selectedObjectId: string | null
  zoom: number
  history: string[]
  historyIndex: number
  // Grid
  gridEnabled: boolean
  gridSize: number
  snapToGrid: boolean
  snapSize: number
  resizeSnapSize: number
  elementSnap: boolean
  showGuides: boolean
  // Canvas appearance
  canvasBgColor: string
  workspaceBgColor: string
  // Copy/paste
  clipboard: string | null
  copiedPropSet: CopiedPropSet | null
  // Actions
  setCanvas: (canvas: FabricCanvas | null) => void
  setSelectedObjectId: (id: string | null) => void
  setZoom: (zoom: number) => void
  pushHistory: (json: string) => void
  undo: () => void
  redo: () => void
  setGridEnabled: (enabled: boolean) => void
  setGridSize: (size: number) => void
  setSnapToGrid: (snap: boolean) => void
  setSnapSize: (size: number) => void
  setResizeSnapSize: (size: number) => void
  setElementSnap: (snap: boolean) => void
  setShowGuides: (show: boolean) => void
  setCanvasBgColor: (color: string) => void
  setWorkspaceBgColor: (color: string) => void
  copyObject: () => void
  pasteObject: () => void
  copyPropSet: (propSet: CopiedPropSet) => void
  pastePropSet: () => void
  deleteSelected: () => void
  resetRotation: () => void
  swapLayerImage: (layerId: string, dataUrl: string) => Promise<void>
  // Layer z-order
  bringToFront: () => void
  sendToBack: () => void
  bringForward: () => void
  sendBackward: () => void
  selectObject: (layerId: string) => void
  // Project overview actions
  duplicateSelected: () => void
  renameObject: (layerId: string, newName: string) => void
  moveLayerTo: (layerId: string, newIndex: number) => void
  lockObject: (layerId: string) => void
  unlockObject: (layerId: string) => void
  groupSelected: () => void
  ungroupSelected: () => void
  deleteObject: (layerId: string) => void
}

export const useCanvasStore = create<CanvasState>((set, get) => ({
  canvas: null,
  selectedObjectId: null,
  zoom: 1,
  history: [],
  historyIndex: -1,
  gridEnabled: false,
  gridSize: 16,
  snapToGrid: false,
  snapSize: 8,
  resizeSnapSize: 8,
  elementSnap: true,
  showGuides: true,
  canvasBgColor: '', // empty = use theme (resolved at render time)
  workspaceBgColor: '',
  clipboard: null,
  copiedPropSet: null,

  setCanvas: (canvas) => set({ canvas }),
  setSelectedObjectId: (id) => set({ selectedObjectId: id }),
  setZoom: (zoom) => set({ zoom }),

  pushHistory: (json) => {
    const { history, historyIndex } = get()
    const newHistory = history.slice(0, historyIndex + 1)
    newHistory.push(json)
    if (newHistory.length > 50) newHistory.shift()
    set({ history: newHistory, historyIndex: newHistory.length - 1 })
  },

  undo: () => {
    const { canvas, history, historyIndex } = get()
    if (!canvas || historyIndex <= 0) return
    const newIndex = historyIndex - 1
    canvas.loadFromJSON(history[newIndex]).then(() => {
      canvas.renderAll()
      set({ historyIndex: newIndex })
    })
  },

  redo: () => {
    const { canvas, history, historyIndex } = get()
    if (!canvas || historyIndex >= history.length - 1) return
    const newIndex = historyIndex + 1
    canvas.loadFromJSON(history[newIndex]).then(() => {
      canvas.renderAll()
      set({ historyIndex: newIndex })
    })
  },

  setGridEnabled: (enabled) => set({ gridEnabled: enabled }),
  setGridSize: (size) => set({ gridSize: Math.max(4, Math.min(256, size)) }),
  setSnapToGrid: (snap) => set({ snapToGrid: snap }),
  setSnapSize: (size) => set({ snapSize: Math.max(1, Math.min(128, size)) }),
  setResizeSnapSize: (size) => set({ resizeSnapSize: Math.max(1, Math.min(128, size)) }),
  setElementSnap: (snap) => set({ elementSnap: snap }),
  setShowGuides: (show) => set({ showGuides: show }),
  setCanvasBgColor: (color) => {
    // Canvas background is rendered via a separate DOM element so the grid
    // can appear between the background and the Fabric objects.
    // The Fabric canvas itself stays transparent.
    set({ canvasBgColor: color })
  },
  setWorkspaceBgColor: (color) => set({ workspaceBgColor: color }),

  copyObject: () => {
    const { canvas } = get()
    if (!canvas) return
    const active = canvas.getActiveObject()
    if (!active) return
    const json = JSON.stringify(active.toJSON(['layerId', 'layerName', 'widgetType']))
    set({ clipboard: json })
  },

  pasteObject: () => {
    const { canvas, clipboard, pushHistory: push } = get()
    if (!canvas || !clipboard) return
    try {
      const parsed = JSON.parse(clipboard)
      parsed.left = (parsed.left || 0) + 20
      parsed.top = (parsed.top || 0) + 20
      parsed.layerId = `pasted-${Date.now()}`

      import('fabric').then(({ util }) => {
        util.enlivenObjects([parsed]).then((objects: FabricObject[]) => {
          for (const obj of objects) {
            canvas.add(obj)
            canvas.setActiveObject(obj)
          }
          canvas.renderAll()
          push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
        })
      })
    } catch (err) {
      console.warn('Paste failed:', err)
    }
  },

  copyPropSet: (propSet) => set({ copiedPropSet: propSet }),

  pastePropSet: () => {
    const { canvas, selectedObjectId, copiedPropSet, pushHistory: push } = get()
    if (!canvas || !selectedObjectId || !copiedPropSet) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return

    for (const [key, value] of Object.entries(copiedPropSet.values)) {
      if (value !== undefined) {
        // Handle size via scaleX/scaleY
        if (key === '_width') {
          obj.set('scaleX', value / (obj.width || 1))
        } else if (key === '_height') {
          obj.set('scaleY', value / (obj.height || 1))
        } else {
          obj.set(key as keyof FabricObject, value as any)
        }
      }
    }
    obj.setCoords()
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  deleteSelected: () => {
    const { canvas, pushHistory: push } = get()
    if (!canvas) return
    const active = canvas.getActiveObject()
    if (active) {
      canvas.remove(active)
      canvas.renderAll()
      push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    }
  },

  resetRotation: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    obj.set('angle', 0)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  swapLayerImage: async (layerId, dataUrl) => {
    const { canvas, pushHistory: push } = get()
    if (!canvas) return

    const { FabricImage } = await import('fabric')
    const objects = canvas.getObjects()
    const target = objects.find((obj) => (obj as any).layerId === layerId)
    if (!target) return

    const img = await FabricImage.fromURL(dataUrl)
    img.set({
      left: target.left,
      top: target.top,
      scaleX: (target.width! * (target.scaleX || 1)) / img.width!,
      scaleY: (target.height! * (target.scaleY || 1)) / img.height!,
      selectable: target.selectable
    })
    ;(img as any).layerId = layerId
    ;(img as any).layerName = (target as any).layerName

    const idx = objects.indexOf(target)
    canvas.remove(target)
    canvas.insertAt(idx, img)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  bringToFront: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    canvas.bringObjectToFront(obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  sendToBack: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    canvas.sendObjectToBack(obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  bringForward: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    canvas.bringObjectForward(obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  sendBackward: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    canvas.sendObjectBackward(obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  selectObject: (layerId) => {
    const { canvas } = get()
    if (!canvas) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
    if (obj) {
      canvas.setActiveObject(obj)
      canvas.renderAll()
      set({ selectedObjectId: layerId })
    }
  },

  // ─── Project overview actions ──────────────────────────────────────────

  duplicateSelected: () => {
    const { canvas, selectedObjectId, pushHistory: push } = get()
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return
    const json = JSON.stringify(obj.toJSON(['layerId', 'layerName', 'widgetType']))
    try {
      const parsed = JSON.parse(json)
      parsed.left = (parsed.left || 0) + 20
      parsed.top = (parsed.top || 0) + 20
      const newId = `dup-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`
      parsed.layerId = newId
      parsed.layerName = (parsed.layerName || 'Object') + ' Copy'

      import('fabric').then(({ util }) => {
        util.enlivenObjects([parsed]).then((objects: FabricObject[]) => {
          for (const o of objects) {
            ;(o as any).layerId = newId
            ;(o as any).layerName = parsed.layerName
            canvas.add(o)
            canvas.setActiveObject(o)
          }
          canvas.renderAll()
          set({ selectedObjectId: newId })
          push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
        })
      })
    } catch (err) {
      console.warn('Duplicate failed:', err)
    }
  },

  renameObject: (layerId, newName) => {
    const { canvas } = get()
    if (!canvas) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
    if (!obj) return
    ;(obj as any).layerName = newName
    canvas.renderAll()
  },

  moveLayerTo: (layerId, newIndex) => {
    const { canvas, pushHistory: push } = get()
    if (!canvas) return
    const objects = canvas.getObjects()
    const obj = objects.find((o) => (o as any).layerId === layerId)
    if (!obj) return
    const currentIdx = objects.indexOf(obj)
    if (currentIdx === newIndex) return
    canvas.remove(obj)
    canvas.insertAt(Math.min(newIndex, canvas.getObjects().length), obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  },

  lockObject: (layerId) => {
    const { canvas } = get()
    if (!canvas) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
    if (!obj) return
    obj.set({
      selectable: false,
      evented: false,
      lockMovementX: true,
      lockMovementY: true,
      lockRotation: true,
      lockScalingX: true,
      lockScalingY: true
    })
    ;(obj as any).isLocked = true
    canvas.discardActiveObject()
    canvas.renderAll()
  },

  unlockObject: (layerId) => {
    const { canvas } = get()
    if (!canvas) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
    if (!obj) return
    obj.set({
      selectable: true,
      evented: true,
      lockMovementX: false,
      lockMovementY: false,
      lockRotation: false,
      lockScalingX: false,
      lockScalingY: false
    })
    ;(obj as any).isLocked = false
    canvas.renderAll()
  },

  groupSelected: () => {
    const { canvas, pushHistory: push } = get()
    if (!canvas) return
    const active = canvas.getActiveObject()
    if (!active) return

    // Need multiple selected objects (ActiveSelection)
    if ((active as any).type !== 'activeSelection') return
    const selection = active as any
    const objects = selection.getObjects() as FabricObject[]
    if (objects.length < 2) return

    import('fabric').then(({ Group }) => {
      // Remove objects from canvas temporarily
      canvas.discardActiveObject()
      for (const o of objects) {
        canvas.remove(o)
      }

      const groupId = `group-${Date.now()}`
      const group = new Group(objects, {
        left: selection.left,
        top: selection.top
      })
      ;(group as any).layerId = groupId
      ;(group as any).layerName = 'Group'

      canvas.add(group)
      canvas.setActiveObject(group)
      canvas.renderAll()
      set({ selectedObjectId: groupId })
      push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    })
  },

  ungroupSelected: () => {
    const { canvas, pushHistory: push } = get()
    if (!canvas) return
    const active = canvas.getActiveObject()
    if (!active || (active as any).type !== 'group') return

    const group = active as any
    const items = group.getObjects() as FabricObject[]

    // Calculate each item's absolute position before ungrouping
    const positions = items.map((item: any) => {
      const matrix = item.calcTransformMatrix()
      return {
        left: matrix[4],
        top: matrix[5],
        angle: item.angle + (group.angle || 0)
      }
    })

    canvas.remove(active)

    items.forEach((item: any, i: number) => {
      item.set({
        left: positions[i].left,
        top: positions[i].top,
        angle: positions[i].angle
      })
      item.setCoords()
      canvas.add(item)
    })

    canvas.discardActiveObject()
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    set({ selectedObjectId: null })
  },

  deleteObject: (layerId) => {
    const { canvas, pushHistory: push, selectedObjectId } = get()
    if (!canvas) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
    if (!obj) return
    canvas.remove(obj)
    canvas.renderAll()
    push(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    if (selectedObjectId === layerId) {
      set({ selectedObjectId: null })
    }
  }
}))
