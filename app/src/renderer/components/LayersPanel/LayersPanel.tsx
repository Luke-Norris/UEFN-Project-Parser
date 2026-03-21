import { useEffect, useState, useRef, useCallback, useMemo } from 'react'
import { useCanvasStore } from '../../stores/canvasStore'
import { useTemplateStore } from '../../stores/templateStore'
import { useContextMenu } from '../ContextMenu/ContextMenu'
import type { MenuEntry } from '../ContextMenu/ContextMenu'

interface LayerEntry {
  layerId: string
  layerName: string
  type: string
  zIndex: number
  visible: boolean
  locked: boolean
  children?: LayerEntry[]
  // UMG hierarchy
  widgetType?: string
  depth?: number
  parentId?: string
  isContainer?: boolean
}

export function LayersPanel() {
  const {
    canvas,
    selectedObjectId,
    selectObject,
    deleteObject,
    duplicateSelected,
    renameObject,
    moveLayerTo,
    lockObject,
    unlockObject,
    bringToFront,
    sendToBack,
    bringForward,
    sendBackward,
    copyObject,
    pasteObject,
    groupSelected,
    ungroupSelected
  } = useCanvasStore()
  const contextMenu = useContextMenu()
  const { activeTemplate } = useTemplateStore()
  const [layers, setLayers] = useState<LayerEntry[]>([])
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set())
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingName, setEditingName] = useState('')
  const [dragOverId, setDragOverId] = useState<string | null>(null)
  const [dragPosition, setDragPosition] = useState<'above' | 'below' | null>(null)
  const draggedId = useRef<string | null>(null)

  // Refresh layer list whenever the canvas changes
  useEffect(() => {
    if (!canvas) {
      setLayers([])
      return
    }

    const refresh = () => {
      const objects = canvas.getObjects()

      // Build a lookup from template layer metadata (for UMG hierarchy)
      const templateLayers = activeTemplate?.layers || []
      const templateMeta = new Map<string, { widgetType?: string; depth?: number; parentId?: string; isContainer?: boolean }>()
      for (const tl of templateLayers) {
        templateMeta.set(tl.id, {
          widgetType: tl.widgetType,
          depth: tl.depth,
          parentId: tl.parentId,
          isContainer: tl.isContainer
        })
      }

      const entries: LayerEntry[] = objects.map((obj, idx) => {
        const layerId = (obj as any).layerId || `obj-${idx}`
        const meta = templateMeta.get(layerId)
        const entry: LayerEntry = {
          layerId,
          layerName: (obj as any).layerName || (obj as any).type || 'Object',
          type: (obj as any).type || 'object',
          zIndex: idx,
          visible: obj.visible !== false,
          locked: !!(obj as any).isLocked,
          widgetType: meta?.widgetType,
          depth: meta?.depth ?? 0,
          parentId: meta?.parentId,
          isContainer: meta?.isContainer
        }
        // For groups, collect child info
        if ((obj as any).type === 'group') {
          const group = obj as any
          const children = group.getObjects ? group.getObjects() : []
          entry.children = children.map((child: any, ci: number) => ({
            layerId: child.layerId || `child-${idx}-${ci}`,
            layerName: child.layerName || child.type || 'Object',
            type: child.type || 'object',
            zIndex: ci,
            visible: child.visible !== false,
            locked: !!child.isLocked
          }))
        }
        return entry
      })
      setLayers(entries)
    }

    refresh()

    canvas.on('object:added', refresh)
    canvas.on('object:removed', refresh)
    canvas.on('object:modified', refresh)
    canvas.on('selection:created', refresh)
    canvas.on('selection:updated', refresh)
    canvas.on('selection:cleared', refresh)

    return () => {
      canvas.off('object:added', refresh)
      canvas.off('object:removed', refresh)
      canvas.off('object:modified', refresh)
      canvas.off('selection:created', refresh)
      canvas.off('selection:updated', refresh)
      canvas.off('selection:cleared', refresh)
    }
  }, [canvas, activeTemplate])

  const toggleVisibility = useCallback(
    (layerId: string) => {
      if (!canvas) return
      const obj = canvas.getObjects().find((o) => (o as any).layerId === layerId)
      if (!obj) return
      obj.visible = !obj.visible
      canvas.renderAll()
      setLayers((prev) =>
        prev.map((l) => (l.layerId === layerId ? { ...l, visible: !l.visible } : l))
      )
    },
    [canvas]
  )

  // Toggle container collapse
  const toggleCollapse = useCallback((layerId: string) => {
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(layerId)) next.delete(layerId)
      else next.add(layerId)
      return next
    })
  }, [])

  // Build a tree from flat layers using parentId
  const layerTree = useMemo(() => {
    // If no UMG hierarchy, just return flat list reversed (z-order)
    const hasHierarchy = layers.some((l) => l.parentId)
    if (!hasHierarchy) {
      return [...layers].reverse()
    }

    // Index by id
    const byId = new Map<string, LayerEntry & { treeChildren: LayerEntry[] }>()
    for (const l of layers) {
      byId.set(l.layerId, { ...l, treeChildren: [] })
    }

    // Build parent→children links
    const roots: LayerEntry[] = []
    for (const l of layers) {
      const node = byId.get(l.layerId)!
      if (l.parentId && byId.has(l.parentId)) {
        byId.get(l.parentId)!.treeChildren.push(node)
      } else {
        roots.push(node)
      }
    }

    // Flatten tree into display order (parent then children, respecting collapse)
    const result: LayerEntry[] = []
    const walk = (nodes: LayerEntry[]) => {
      // Reverse so highest z-index (last added) appears first
      for (const node of [...nodes].reverse()) {
        result.push(node)
        const treeNode = byId.get(node.layerId)
        if (treeNode && treeNode.treeChildren.length > 0 && !collapsed.has(node.layerId)) {
          walk(treeNode.treeChildren)
        }
      }
    }
    walk(roots)
    return result
  }, [layers, collapsed])

  // Start inline rename
  const startRename = useCallback((layerId: string, currentName: string) => {
    setEditingId(layerId)
    setEditingName(currentName)
  }, [])

  // Commit rename
  const commitRename = useCallback(() => {
    if (editingId && editingName.trim()) {
      renameObject(editingId, editingName.trim())
    }
    setEditingId(null)
    setEditingName('')
  }, [editingId, editingName, renameObject])

  // Build context menu for a layer
  const showLayerContextMenu = useCallback(
    (e: React.MouseEvent, layer: LayerEntry) => {
      e.preventDefault()
      e.stopPropagation()

      // Select the object first
      selectObject(layer.layerId)

      const items: MenuEntry[] = [
        {
          label: 'Rename',
          shortcut: 'F2',
          onClick: () => startRename(layer.layerId, layer.layerName),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" />
              <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />
            </svg>
          )
        },
        {
          label: 'Duplicate',
          shortcut: 'Ctrl+D',
          onClick: () => duplicateSelected(),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="9" y="9" width="13" height="13" rx="2" />
              <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
            </svg>
          )
        },
        { type: 'divider' as const },
        {
          label: 'Copy',
          shortcut: 'Ctrl+C',
          onClick: () => copyObject(),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="9" y="9" width="13" height="13" rx="2" />
              <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
            </svg>
          )
        },
        {
          label: 'Paste',
          shortcut: 'Ctrl+V',
          onClick: () => pasteObject(),
          disabled: !useCanvasStore.getState().clipboard,
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2" />
              <rect x="8" y="2" width="8" height="4" rx="1" />
            </svg>
          )
        },
        { type: 'divider' as const },
        {
          label: layer.locked ? 'Unlock' : 'Lock',
          onClick: () => (layer.locked ? unlockObject(layer.layerId) : lockObject(layer.layerId)),
          icon: layer.locked ? (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="3" y="11" width="18" height="11" rx="2" />
              <path d="M7 11V7a5 5 0 0110 0v4" />
            </svg>
          ) : (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="3" y="11" width="18" height="11" rx="2" />
              <path d="M7 11V7a5 5 0 019.9-1" />
            </svg>
          )
        },
        {
          label: layer.visible ? 'Hide' : 'Show',
          onClick: () => toggleVisibility(layer.layerId),
          icon: layer.visible ? (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94" />
              <line x1="1" y1="1" x2="23" y2="23" />
            </svg>
          ) : (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
              <circle cx="12" cy="12" r="3" />
            </svg>
          )
        },
        { type: 'divider' as const },
        {
          label: 'Bring to Front',
          onClick: () => bringToFront(),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M7 11l5-5 5 5M7 17l5-5 5 5" />
            </svg>
          )
        },
        {
          label: 'Send to Back',
          onClick: () => sendToBack(),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M7 7l5 5 5-5M7 13l5 5 5-5" />
            </svg>
          )
        },
        { type: 'divider' as const },
        ...(layer.type === 'group'
          ? [
              {
                label: 'Ungroup',
                shortcut: 'Ctrl+Shift+G',
                onClick: () => ungroupSelected(),
                icon: (
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <rect x="2" y="2" width="8" height="8" rx="1" />
                    <rect x="14" y="14" width="8" height="8" rx="1" />
                  </svg>
                )
              } as MenuEntry
            ]
          : []),
        { type: 'divider' as const },
        {
          label: 'Delete',
          shortcut: 'Del',
          danger: true,
          onClick: () => deleteObject(layer.layerId),
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2" />
            </svg>
          )
        }
      ]

      contextMenu.show(e.clientX, e.clientY, items)
    },
    [
      selectObject,
      startRename,
      duplicateSelected,
      copyObject,
      pasteObject,
      lockObject,
      unlockObject,
      toggleVisibility,
      bringToFront,
      sendToBack,
      ungroupSelected,
      deleteObject,
      contextMenu
    ]
  )

  // Show context menu for empty area
  const showEmptyContextMenu = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault()
      const items: MenuEntry[] = [
        {
          label: 'Paste',
          shortcut: 'Ctrl+V',
          onClick: () => pasteObject(),
          disabled: !useCanvasStore.getState().clipboard,
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2" />
              <rect x="8" y="2" width="8" height="4" rx="1" />
            </svg>
          )
        },
        { type: 'divider' as const },
        {
          label: 'Select All',
          shortcut: 'Ctrl+A',
          onClick: () => {
            if (!canvas) return
            const objs = canvas.getObjects().filter((o) => o.selectable !== false)
            if (objs.length === 0) return
            import('fabric').then(({ ActiveSelection }) => {
              const sel = new ActiveSelection(objs, { canvas: canvas as any })
              canvas.setActiveObject(sel)
              canvas.renderAll()
            })
          },
          disabled: layers.length === 0,
          icon: (
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="3" y="3" width="18" height="18" rx="2" strokeDasharray="4 2" />
            </svg>
          )
        }
      ]
      contextMenu.show(e.clientX, e.clientY, items)
    },
    [pasteObject, canvas, layers, contextMenu]
  )

  // ─── Drag and Drop ──────────────────────────────────────────────────────

  const handleDragStart = useCallback((e: React.DragEvent, layerId: string) => {
    draggedId.current = layerId
    e.dataTransfer.effectAllowed = 'move'
    // Use transparent drag image
    const img = new Image()
    img.src = 'data:image/gif;base64,R0lGODlhAQABAIAAAAUEBAAAACwAAAAAAQABAAACAkQBADs='
    e.dataTransfer.setDragImage(img, 0, 0)
  }, [])

  const handleDragOver = useCallback((e: React.DragEvent, layerId: string) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'

    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect()
    const midY = rect.top + rect.height / 2

    setDragOverId(layerId)
    setDragPosition(e.clientY < midY ? 'above' : 'below')
  }, [])

  const handleDragLeave = useCallback(() => {
    setDragOverId(null)
    setDragPosition(null)
  }, [])

  const handleDrop = useCallback(
    (e: React.DragEvent, targetLayerId: string) => {
      e.preventDefault()
      const srcId = draggedId.current
      if (!srcId || srcId === targetLayerId) {
        setDragOverId(null)
        setDragPosition(null)
        draggedId.current = null
        return
      }

      // Find the target layer to get its z-index
      const target = layers.find((l) => l.layerId === targetLayerId)
      if (!target) return

      // layers is reversed (highest z-index first), so "above" in the UI
      // means higher z-index, "below" means lower z-index
      let newZIndex = target.zIndex
      if (dragPosition === 'above') {
        newZIndex = target.zIndex + 1
      }

      moveLayerTo(srcId, Math.max(0, newZIndex))

      setDragOverId(null)
      setDragPosition(null)
      draggedId.current = null
    },
    [layers, dragPosition, moveLayerTo]
  )

  const handleDragEnd = useCallback(() => {
    setDragOverId(null)
    setDragPosition(null)
    draggedId.current = null
  }, [])

  // Keyboard: F2 to rename, Delete to delete
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (editingId) return // Don't intercept while editing
      const tag = (e.target as HTMLElement)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA') return

      if (e.key === 'F2' && selectedObjectId) {
        const layer = layers.find((l) => l.layerId === selectedObjectId)
        if (layer) startRename(layer.layerId, layer.layerName)
      }
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [selectedObjectId, layers, startRename, editingId])

  return (
    <div
      className="flex flex-col h-full"
      onContextMenu={(e) => {
        // Only show empty context menu if clicking on the panel background
        if ((e.target as HTMLElement).closest('[data-layer-row]')) return
        showEmptyContextMenu(e)
      }}
    >
      {/* Header */}
      <div className="p-3 border-b border-fn-border shrink-0">
        <div className="flex items-center justify-between">
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
            Layers
          </h3>
          <span className="text-[9px] text-gray-600 font-mono">{layerTree.length} items</span>
        </div>
      </div>

      {/* Layer list */}
      <div className="flex-1 overflow-y-auto">
        {layerTree.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 px-4">
            <svg className="w-8 h-8 text-gray-700 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" />
            </svg>
            <p className="text-xs text-gray-600 text-center">
              No objects on canvas
            </p>
            <p className="text-[10px] text-gray-700 text-center mt-1">
              Add shapes from the toolbar below
            </p>
          </div>
        ) : (
          <div className="py-0.5">
            {layerTree.map((layer) => (
              <LayerRow
                key={layer.layerId}
                layer={layer}
                depth={layer.depth ?? 0}
                isCollapsed={collapsed.has(layer.layerId)}
                hasChildren={layer.isContainer && layers.some((l) => l.parentId === layer.layerId)}
                onToggleCollapse={() => toggleCollapse(layer.layerId)}
                isSelected={layer.layerId === selectedObjectId}
                isEditing={editingId === layer.layerId}
                editingName={editingName}
                dragOverId={dragOverId}
                dragPosition={dragPosition}
                onSelect={() => selectObject(layer.layerId)}
                onContextMenu={(e) => showLayerContextMenu(e, layer)}
                onToggleVisibility={() => toggleVisibility(layer.layerId)}
                onStartRename={() => startRename(layer.layerId, layer.layerName)}
                onEditingNameChange={setEditingName}
                onCommitRename={commitRename}
                onCancelRename={() => {
                  setEditingId(null)
                  setEditingName('')
                }}
                onDragStart={(e) => handleDragStart(e, layer.layerId)}
                onDragOver={(e) => handleDragOver(e, layer.layerId)}
                onDragLeave={handleDragLeave}
                onDrop={(e) => handleDrop(e, layer.layerId)}
                onDragEnd={handleDragEnd}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// ─── Layer Row ──────────────────────────────────────────────────────────────

function LayerRow({
  layer,
  depth,
  isCollapsed,
  hasChildren,
  onToggleCollapse,
  isSelected,
  isEditing,
  editingName,
  dragOverId,
  dragPosition,
  onSelect,
  onContextMenu,
  onToggleVisibility,
  onStartRename,
  onEditingNameChange,
  onCommitRename,
  onCancelRename,
  onDragStart,
  onDragOver,
  onDragLeave,
  onDrop,
  onDragEnd
}: {
  layer: LayerEntry
  depth: number
  isCollapsed: boolean
  hasChildren: boolean
  onToggleCollapse: () => void
  isSelected: boolean
  isEditing: boolean
  editingName: string
  dragOverId: string | null
  dragPosition: 'above' | 'below' | null
  onSelect: () => void
  onContextMenu: (e: React.MouseEvent) => void
  onToggleVisibility: () => void
  onStartRename: () => void
  onEditingNameChange: (name: string) => void
  onCommitRename: () => void
  onCancelRename: () => void
  onDragStart: (e: React.DragEvent) => void
  onDragOver: (e: React.DragEvent) => void
  onDragLeave: () => void
  onDrop: (e: React.DragEvent) => void
  onDragEnd: () => void
}) {
  const renameRef = useRef<HTMLInputElement>(null)

  // Focus rename input when editing starts
  useEffect(() => {
    if (isEditing && renameRef.current) {
      renameRef.current.focus()
      renameRef.current.select()
    }
  }, [isEditing])

  const isDragTarget = dragOverId === layer.layerId

  return (
    <>
      <div
        data-layer-row
        className={`relative w-full flex items-center gap-1.5 px-2 py-1 text-left transition-colors group cursor-pointer ${
          isSelected
            ? 'bg-fn-rare/15 text-white'
            : 'text-gray-400 hover:bg-white/5 hover:text-gray-200'
        } ${layer.locked ? 'opacity-60' : ''}`}
        style={{ paddingLeft: `${8 + depth * 16}px` }}
        onClick={onSelect}
        onContextMenu={onContextMenu}
        onDoubleClick={onStartRename}
        draggable={!isEditing}
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        onDragEnd={onDragEnd}
      >
        {/* Drop indicator line */}
        {isDragTarget && dragPosition === 'above' && (
          <div className="absolute top-0 left-2 right-2 h-[2px] bg-fn-rare rounded-full -translate-y-px z-10" />
        )}
        {isDragTarget && dragPosition === 'below' && (
          <div className="absolute bottom-0 left-2 right-2 h-[2px] bg-fn-rare rounded-full translate-y-px z-10" />
        )}

        {/* Collapse toggle or drag handle */}
        {hasChildren ? (
          <button
            className={`shrink-0 p-0.5 rounded transition-colors ${
              layer.isContainer ? 'text-fn-epic/70 hover:text-fn-epic' : 'text-gray-500 hover:text-white'
            }`}
            onClick={(e) => {
              e.stopPropagation()
              onToggleCollapse()
            }}
            title={isCollapsed ? 'Expand' : 'Collapse'}
          >
            <svg
              className={`w-3 h-3 transition-transform ${isCollapsed ? '' : 'rotate-90'}`}
              fill="currentColor"
              viewBox="0 0 20 20"
            >
              <path d="M6 4l8 6-8 6V4z" />
            </svg>
          </button>
        ) : (
          <span className="shrink-0 w-4 text-center text-gray-700 group-hover:text-gray-500 cursor-grab active:cursor-grabbing transition-colors">
            <svg className="w-2 h-2 inline" fill="currentColor" viewBox="0 0 8 8">
              <circle cx="4" cy="4" r="2" />
            </svg>
          </span>
        )}

        {/* Visibility toggle */}
        <button
          className={`shrink-0 p-0.5 rounded transition-colors ${
            layer.visible
              ? 'text-gray-500 hover:text-white'
              : 'text-gray-700 hover:text-gray-400'
          }`}
          onClick={(e) => {
            e.stopPropagation()
            onToggleVisibility()
          }}
          title={layer.visible ? 'Hide' : 'Show'}
        >
          {layer.visible ? (
            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
              <circle cx="12" cy="12" r="3" />
            </svg>
          ) : (
            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24" />
              <line x1="1" y1="1" x2="23" y2="23" />
            </svg>
          )}
        </button>

        {/* Type icon */}
        <span className={`shrink-0 ${isSelected ? 'text-fn-rare' : 'text-gray-600'}`}>
          {getTypeIcon(layer.type)}
        </span>

        {/* Name (editable) */}
        {isEditing ? (
          <input
            ref={renameRef}
            className="flex-1 text-[11px] bg-fn-darker border border-fn-rare/50 rounded px-1.5 py-0.5 text-white outline-none min-w-0"
            value={editingName}
            onChange={(e) => onEditingNameChange(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') onCommitRename()
              if (e.key === 'Escape') onCancelRename()
            }}
            onBlur={onCommitRename}
            onClick={(e) => e.stopPropagation()}
          />
        ) : (
          <span className="text-[11px] truncate flex-1 min-w-0">{layer.layerName}</span>
        )}

        {/* Lock indicator */}
        {layer.locked && (
          <span className="shrink-0 text-gray-600" title="Locked">
            <svg className="w-2.5 h-2.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="3" y="11" width="18" height="11" rx="2" />
              <path d="M7 11V7a5 5 0 0110 0v4" />
            </svg>
          </span>
        )}

        {/* Type badge */}
        <span
          className={`text-[9px] font-mono px-1 py-0.5 rounded shrink-0 ${
            layer.isContainer
              ? isSelected ? 'bg-fn-epic/20 text-fn-epic' : 'text-fn-epic/50'
              : isSelected ? 'bg-fn-rare/20 text-fn-rare' : 'text-gray-600'
          }`}
        >
          {layer.widgetType || (layer.type === 'group' ? 'GRP' : layer.type)}
        </span>
      </div>

      {/* Group children */}
      {layer.children && layer.children.length > 0 && (
        <div className="border-l border-fn-border/30 ml-4">
          {layer.children.map((child) => (
            <div
              key={child.layerId}
              className={`flex items-center gap-1.5 px-2 py-0.5 text-left transition-colors text-gray-500 hover:bg-white/3`}
              style={{ paddingLeft: `${8 + (depth + 1) * 16}px` }}
            >
              <span className="shrink-0 text-gray-700">
                {getTypeIcon(child.type)}
              </span>
              <span className="text-[10px] truncate flex-1 text-gray-500">
                {child.layerName}
              </span>
            </div>
          ))}
        </div>
      )}
    </>
  )
}

// ─── Type Icon ──────────────────────────────────────────────────────────────

function getTypeIcon(type: string) {
  switch (type) {
    case 'rect':
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
        </svg>
      )
    case 'circle':
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <circle cx="12" cy="12" r="9" />
        </svg>
      )
    case 'text':
    case 'i-text':
    case 'textbox':
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M4 7V4h16v3M9 20h6M12 4v16" />
        </svg>
      )
    case 'image':
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <path d="M21 15l-5-5L5 21" />
        </svg>
      )
    case 'group':
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <rect x="2" y="2" width="9" height="9" rx="1" />
          <rect x="13" y="13" width="9" height="9" rx="1" />
          <path d="M13 7h4M7 13v4" strokeDasharray="2 2" />
        </svg>
      )
    default:
      return (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" />
        </svg>
      )
  }
}
