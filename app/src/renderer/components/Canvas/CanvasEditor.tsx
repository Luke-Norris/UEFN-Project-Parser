import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useFabricCanvas } from '../../hooks/useFabricCanvas'
import { useTemplateStore } from '../../stores/templateStore'
import { useCanvasStore } from '../../stores/canvasStore'
import { useAssetStore } from '../../stores/assetStore'
import { useContextMenu } from '../ContextMenu/ContextMenu'
import type { MenuEntry } from '../ContextMenu/ContextMenu'
import type { Canvas as FabricCanvas } from 'fabric'

interface Props {
  fontsLoaded: boolean
}

export function CanvasEditor({ fontsLoaded }: Props) {
  const { canvasRef, loadTemplate } = useFabricCanvas()
  const { activeTemplate, templateWidth, templateHeight } = useTemplateStore()
  const {
    canvas,
    zoom,
    setZoom,
    gridEnabled,
    gridSize,
    canvasBgColor,
    workspaceBgColor,
    pushHistory,
    selectedObjectId,
    bringToFront,
    sendToBack,
    bringForward,
    sendBackward,
    copyObject,
    pasteObject,
    deleteSelected,
    duplicateSelected,
    groupSelected,
    ungroupSelected
  } = useCanvasStore()
  const contextMenu = useContextMenu()

  const containerRef = useRef<HTMLDivElement>(null)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const isPanning = useRef(false)
  const lastPanPoint = useRef({ x: 0, y: 0 })
  const prevZoomRef = useRef(zoom)
  const initialCentered = useRef(false)

  // Center canvas when template dimensions change or on first mount
  useEffect(() => {
    if (!containerRef.current) return
    const rect = containerRef.current.getBoundingClientRect()
    const currentZoom = useCanvasStore.getState().zoom
    setPan({
      x: (rect.width - templateWidth * currentZoom) / 2,
      y: (rect.height - templateHeight * currentZoom) / 2
    })
    initialCentered.current = true
  }, [templateWidth, templateHeight])

  // Center canvas on initial mount after container is sized
  useEffect(() => {
    if (initialCentered.current) return
    const timer = setTimeout(() => {
      if (!containerRef.current) return
      const rect = containerRef.current.getBoundingClientRect()
      const currentZoom = useCanvasStore.getState().zoom
      setPan({
        x: (rect.width - templateWidth * currentZoom) / 2,
        y: (rect.height - templateHeight * currentZoom) / 2
      })
      initialCentered.current = true
    }, 50)
    return () => clearTimeout(timer)
  }, [templateWidth, templateHeight])

  // Re-center canvas when container resizes (e.g. sidebar collapse/expand)
  const prevContainerSize = useRef<{ w: number; h: number } | null>(null)
  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const observer = new ResizeObserver((entries) => {
      const entry = entries[0]
      if (!entry) return
      const newW = entry.contentRect.width
      const newH = entry.contentRect.height
      const prev = prevContainerSize.current
      if (prev) {
        // Shift pan by half the size delta to keep the canvas centered
        const dx = (newW - prev.w) / 2
        const dy = (newH - prev.h) / 2
        setPan((p) => ({ x: p.x + dx, y: p.y + dy }))
      }
      prevContainerSize.current = { w: newW, h: newH }
    })
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // Handle external zoom changes (toolbar zoom buttons) - zoom from center of view
  useEffect(() => {
    if (prevZoomRef.current === zoom) return
    if (!containerRef.current) {
      prevZoomRef.current = zoom
      return
    }
    const rect = containerRef.current.getBoundingClientRect()
    const centerX = rect.width / 2
    const centerY = rect.height / 2
    const ratio = zoom / prevZoomRef.current
    setPan((prev) => ({
      x: centerX - (centerX - prev.x) * ratio,
      y: centerY - (centerY - prev.y) * ratio
    }))
    prevZoomRef.current = zoom
  }, [zoom])

  // Load template only after fonts are loaded
  useEffect(() => {
    if (activeTemplate && fontsLoaded) {
      loadTemplate(activeTemplate)
    }
  }, [activeTemplate, fontsLoaded, loadTemplate])

  // Handle zoom with mouse wheel - zoom to cursor position
  const handleWheel = useCallback(
    (e: React.WheelEvent) => {
      e.preventDefault()
      if (!containerRef.current) return

      const rect = containerRef.current.getBoundingClientRect()
      const mouseX = e.clientX - rect.left
      const mouseY = e.clientY - rect.top

      const oldZoom = zoom
      const delta = e.deltaY > 0 ? -0.05 : 0.05
      const newZoom = Math.max(0.1, Math.min(5, oldZoom + delta))

      // Zoom toward cursor: keep the point under the cursor stationary
      const ratio = newZoom / oldZoom
      const newPanX = mouseX - (mouseX - pan.x) * ratio
      const newPanY = mouseY - (mouseY - pan.y) * ratio

      setPan({ x: newPanX, y: newPanY })
      prevZoomRef.current = newZoom
      setZoom(newZoom)
    },
    [zoom, pan, setZoom]
  )

  // Middle mouse button panning
  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    // Middle mouse button (button === 1) or space+left click
    if (e.button === 1) {
      e.preventDefault()
      isPanning.current = true
      lastPanPoint.current = { x: e.clientX, y: e.clientY }
    }
  }, [])

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    if (!isPanning.current) return
    const dx = e.clientX - lastPanPoint.current.x
    const dy = e.clientY - lastPanPoint.current.y
    lastPanPoint.current = { x: e.clientX, y: e.clientY }
    setPan((prev) => ({ x: prev.x + dx, y: prev.y + dy }))
  }, [])

  const handleMouseUp = useCallback((e: React.MouseEvent) => {
    if (e.button === 1) {
      isPanning.current = false
    }
  }, [])

  // Space bar + left click panning
  const spaceHeld = useRef(false)
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.code === 'Space' && !spaceHeld.current) {
        const tag = (e.target as HTMLElement)?.tagName
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
        e.preventDefault()
        spaceHeld.current = true
        if (containerRef.current) {
          containerRef.current.style.cursor = 'grab'
        }
      }
    }
    const onKeyUp = (e: KeyboardEvent) => {
      if (e.code === 'Space') {
        spaceHeld.current = false
        isPanning.current = false
        if (containerRef.current) {
          containerRef.current.style.cursor = ''
        }
      }
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
    }
  }, [])

  const handleMouseDownWithSpace = useCallback(
    (e: React.MouseEvent) => {
      if (spaceHeld.current && e.button === 0) {
        e.preventDefault()
        e.stopPropagation()
        isPanning.current = true
        lastPanPoint.current = { x: e.clientX, y: e.clientY }
        if (containerRef.current) {
          containerRef.current.style.cursor = 'grabbing'
        }
        return
      }
      handleMouseDown(e)
    },
    [handleMouseDown]
  )

  const handleMouseUpWithSpace = useCallback(
    (e: React.MouseEvent) => {
      if (isPanning.current && (e.button === 0 || e.button === 1)) {
        isPanning.current = false
        if (containerRef.current) {
          containerRef.current.style.cursor = spaceHeld.current ? 'grab' : ''
        }
      }
      handleMouseUp(e)
    },
    [handleMouseUp]
  )

  // Reset view - double click zoom indicator to recenter
  const resetView = useCallback(() => {
    if (!containerRef.current) return
    const rect = containerRef.current.getBoundingClientRect()
    setZoom(1)
    prevZoomRef.current = 1
    setPan({
      x: (rect.width - templateWidth) / 2,
      y: (rect.height - templateHeight) / 2
    })
  }, [setZoom, templateWidth, templateHeight])

  // Add shape to canvas
  const addShape = useCallback(
    async (type: 'rect' | 'circle' | 'text') => {
      if (!canvas) return
      const { Rect, Circle, FabricText } = await import('fabric')
      const id = `${type}-${Date.now()}`
      const center = { left: templateWidth / 2 - 50, top: templateHeight / 2 - 50 }

      let obj: any
      if (type === 'rect') {
        obj = new Rect({
          ...center,
          width: 100,
          height: 100,
          fill: '#3d85e0',
          stroke: null,
          strokeWidth: 0,
          rx: 4,
          ry: 4
        })
      } else if (type === 'circle') {
        obj = new Circle({
          left: templateWidth / 2 - 40,
          top: templateHeight / 2 - 40,
          radius: 40,
          fill: '#a34ee1',
          stroke: null,
          strokeWidth: 0
        })
      } else {
        obj = new FabricText('Text', {
          ...center,
          fontSize: 32,
          fill: '#ffffff',
          fontFamily: 'BurbankBigCondensed-Black',
          stroke: null,
          strokeWidth: 0
        })
      }

      ;(obj as any).layerId = id
      ;(obj as any).layerName = type.charAt(0).toUpperCase() + type.slice(1)
      canvas.add(obj)
      canvas.setActiveObject(obj)
      canvas.renderAll()
      pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
    },
    [canvas, templateWidth, templateHeight, pushHistory]
  )

  // Import image to assets folder (not directly to canvas)
  const handleImportToAssets = useCallback(async () => {
    const result = await window.electronAPI.importToAssets('')
    if (result.success && result.index) {
      useAssetStore.getState().setIndex(result.index)
    }
  }, [])

  // Grid overlay SVG — always rendered to avoid DOM conflicts with Fabric.js
  // (Fabric wraps the <canvas> in its own container, so conditionally inserting
  // siblings can break React's DOM reconciliation). We toggle visibility via CSS.
  const gridOverlay = useMemo(() => {
    return (
      <svg
        className="absolute pointer-events-none"
        width={templateWidth}
        height={templateHeight}
        style={{
          top: 0,
          left: 0,
          opacity: gridEnabled ? 0.2 : 0,
          display: gridEnabled ? 'block' : 'none'
        }}
      >
        <defs>
          <pattern
            id="uefn-canvas-grid"
            width={gridSize}
            height={gridSize}
            patternUnits="userSpaceOnUse"
          >
            <path
              d={`M ${gridSize} 0 L 0 0 0 ${gridSize}`}
              fill="none"
              stroke="#fff"
              strokeWidth="0.5"
            />
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill="url(#uefn-canvas-grid)" />
      </svg>
    )
  }, [gridEnabled, gridSize, templateWidth, templateHeight])

  return (
    <div
      ref={containerRef}
      className="flex-1 overflow-hidden relative"
      style={{ backgroundColor: workspaceBgColor }}
      onWheel={handleWheel}
      onMouseDown={handleMouseDownWithSpace}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUpWithSpace}
      onMouseLeave={() => {
        isPanning.current = false
      }}
      onContextMenu={(e) => {
        e.preventDefault()
        // Don't show context menu during pan
        if (isPanning.current || spaceHeld.current) return

        const hasSelection = !!selectedObjectId
        const hasClipboard = !!useCanvasStore.getState().clipboard
        const activeObj = canvas?.getActiveObject()
        const isGroup = activeObj && (activeObj as any).type === 'group'
        const isMultiSelect = activeObj && (activeObj as any).type === 'activeSelection'

        const items: MenuEntry[] = hasSelection
          ? [
              {
                label: 'Copy',
                shortcut: 'Ctrl+C',
                onClick: () => copyObject(),
                icon: <CopyIcon />
              },
              {
                label: 'Paste',
                shortcut: 'Ctrl+V',
                onClick: () => pasteObject(),
                disabled: !hasClipboard,
                icon: <PasteIcon />
              },
              {
                label: 'Duplicate',
                shortcut: 'Ctrl+D',
                onClick: () => duplicateSelected(),
                icon: <DuplicateIcon />
              },
              { type: 'divider' as const },
              {
                label: 'Bring to Front',
                onClick: () => bringToFront(),
                icon: <BringFrontIcon />
              },
              {
                label: 'Bring Forward',
                onClick: () => bringForward(),
                icon: <BringForwardIcon />
              },
              {
                label: 'Send Backward',
                onClick: () => sendBackward(),
                icon: <SendBackwardIcon />
              },
              {
                label: 'Send to Back',
                onClick: () => sendToBack(),
                icon: <SendBackIcon />
              },
              { type: 'divider' as const },
              ...(isMultiSelect
                ? [
                    {
                      label: 'Group',
                      shortcut: 'Ctrl+G',
                      onClick: () => groupSelected(),
                      icon: <GroupIcon />
                    } as MenuEntry,
                    { type: 'divider' as const } as MenuEntry
                  ]
                : []),
              ...(isGroup
                ? [
                    {
                      label: 'Ungroup',
                      shortcut: 'Ctrl+Shift+G',
                      onClick: () => ungroupSelected(),
                      icon: <UngroupIcon />
                    } as MenuEntry,
                    { type: 'divider' as const } as MenuEntry
                  ]
                : []),
              {
                label: 'Delete',
                shortcut: 'Del',
                danger: true,
                onClick: () => deleteSelected(),
                icon: <DeleteIcon />
              }
            ]
          : [
              {
                label: 'Add Rectangle',
                onClick: () => addShape('rect'),
                icon: <RectIcon />
              },
              {
                label: 'Add Circle',
                onClick: () => addShape('circle'),
                icon: <CircleIcon />
              },
              {
                label: 'Add Text',
                onClick: () => addShape('text'),
                icon: <TextIcon />
              },
              { type: 'divider' as const },
              {
                label: 'Import to Assets',
                onClick: () => handleImportToAssets(),
                icon: <ImportIcon />
              },
              { type: 'divider' as const },
              {
                label: 'Paste',
                shortcut: 'Ctrl+V',
                onClick: () => pasteObject(),
                disabled: !hasClipboard,
                icon: <PasteIcon />
              }
            ]

        contextMenu.show(e.clientX, e.clientY, items)
      }}
    >
      <div
        className="absolute"
        style={{
          transform: `translate(${Math.round(pan.x)}px, ${Math.round(pan.y)}px) scale(${zoom})`,
          transformOrigin: '0 0',
          imageRendering: zoom > 2 ? 'pixelated' : 'auto',
          willChange: 'transform'
        }}
      >
        {/* Checkerboard background for transparency */}
        <div
          className="absolute inset-0 rounded"
          style={{
            width: templateWidth,
            height: templateHeight,
            backgroundImage: `
              linear-gradient(45deg, #222 25%, transparent 25%),
              linear-gradient(-45deg, #222 25%, transparent 25%),
              linear-gradient(45deg, transparent 75%, #222 75%),
              linear-gradient(-45deg, transparent 75%, #222 75%)
            `,
            backgroundSize: '16px 16px',
            backgroundPosition: '0 0, 0 8px, 8px -8px, -8px 0px'
          }}
        />
        {/* Canvas background color — separate element so grid renders behind objects */}
        <div
          className="absolute"
          style={{
            width: templateWidth,
            height: templateHeight,
            backgroundColor: canvasBgColor
          }}
        />
        {/* Grid overlay — renders between background and canvas objects */}
        {gridOverlay}
        {/* Fabric canvas — transparent bg, only renders objects */}
        <canvas ref={canvasRef} />
      </div>

      {/* Floating shape bar - centered bottom */}
      <div className="absolute bottom-6 left-1/2 -translate-x-1/2 flex items-center gap-1 bg-fn-dark/95 backdrop-blur-md border border-fn-border/60 rounded-xl px-2 py-1.5 shadow-xl shadow-black/40">
        <ShapeBtn
          label="Rectangle"
          onClick={() => addShape('rect')}
          icon={
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <rect x="3" y="3" width="18" height="18" rx="2" />
            </svg>
          }
        />
        <ShapeBtn
          label="Circle"
          onClick={() => addShape('circle')}
          icon={
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <circle cx="12" cy="12" r="9" />
            </svg>
          }
        />
        <ShapeBtn
          label="Text"
          onClick={() => addShape('text')}
          icon={
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path d="M4 7V4h16v3M9 20h6M12 4v16" />
            </svg>
          }
        />

        <div className="h-5 w-px bg-fn-border/50 mx-0.5" />

        <ShapeBtn
          label="Import to Assets"
          onClick={handleImportToAssets}
          icon={
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
            </svg>
          }
        />

        {/* Z-order controls — shown when an element is selected */}
        {selectedObjectId && (
          <>
            <div className="h-5 w-px bg-fn-border/50 mx-0.5" />
            <ShapeBtn
              label="Bring to Front"
              onClick={bringToFront}
              icon={
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path d="M7 11l5-5 5 5M7 17l5-5 5 5" />
                </svg>
              }
            />
            <ShapeBtn
              label="Bring Forward"
              onClick={bringForward}
              icon={
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path d="M7 14l5-5 5 5" />
                </svg>
              }
            />
            <ShapeBtn
              label="Send Backward"
              onClick={sendBackward}
              icon={
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path d="M7 10l5 5 5-5" />
                </svg>
              }
            />
            <ShapeBtn
              label="Send to Back"
              onClick={sendToBack}
              icon={
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path d="M7 7l5 5 5-5M7 13l5 5 5-5" />
                </svg>
              }
            />
          </>
        )}
      </div>

      {/* Zoom indicator - double click to reset view */}
      <div
        className="absolute bottom-6 right-4 bg-fn-panel/90 px-3 py-1.5 rounded-lg text-xs text-gray-400 backdrop-blur-sm border border-fn-border/50 cursor-pointer hover:text-white transition-colors select-none"
        onDoubleClick={resetView}
        title="Double-click to reset view"
      >
        {Math.round(zoom * 100)}%
      </div>

      {!fontsLoaded && (
        <div className="absolute inset-0 flex items-center justify-center bg-fn-darker/80 backdrop-blur-sm">
          <p className="text-gray-400 text-sm">Loading fonts...</p>
        </div>
      )}
    </div>
  )
}

/** Single shape tool button in the floating bar */
function ShapeBtn({
  label,
  onClick,
  icon
}: {
  label: string
  onClick: () => void
  icon: React.ReactNode
}) {
  return (
    <button
      className="p-2 rounded-lg text-gray-400 hover:text-white hover:bg-white/10 transition-colors"
      onClick={onClick}
      title={label}
    >
      {icon}
    </button>
  )
}

// ─── Context Menu Icons ──────────────────────────────────────────────────────

const i = "w-3.5 h-3.5"
const s = 2

function CopyIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <rect x="9" y="9" width="13" height="13" rx="2" />
      <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
    </svg>
  )
}
function PasteIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2" />
      <rect x="8" y="2" width="8" height="4" rx="1" />
    </svg>
  )
}
function DuplicateIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <rect x="9" y="9" width="13" height="13" rx="2" />
      <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
    </svg>
  )
}
function DeleteIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2" />
    </svg>
  )
}
function BringFrontIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M7 11l5-5 5 5M7 17l5-5 5 5" />
    </svg>
  )
}
function BringForwardIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M7 14l5-5 5 5" />
    </svg>
  )
}
function SendBackwardIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M7 10l5 5 5-5" />
    </svg>
  )
}
function SendBackIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M7 7l5 5 5-5M7 13l5 5 5-5" />
    </svg>
  )
}
function GroupIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <rect x="2" y="2" width="9" height="9" rx="1" />
      <rect x="13" y="13" width="9" height="9" rx="1" />
      <path d="M13 7h4M7 13v4" />
    </svg>
  )
}
function UngroupIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <rect x="2" y="2" width="8" height="8" rx="1" />
      <rect x="14" y="14" width="8" height="8" rx="1" />
    </svg>
  )
}
function RectIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <rect x="3" y="3" width="18" height="18" rx="2" />
    </svg>
  )
}
function CircleIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <circle cx="12" cy="12" r="9" />
    </svg>
  )
}
function TextIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M4 7V4h16v3M9 20h6M12 4v16" />
    </svg>
  )
}
function ImportIcon() {
  return (
    <svg className={i} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={s}>
      <path d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
    </svg>
  )
}
