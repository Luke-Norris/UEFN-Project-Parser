import { useEffect, useRef, useState, useCallback } from 'react'
import { AssetBrowser } from '../components/AssetBrowser/AssetBrowser'
import { CanvasEditor } from '../components/Canvas/CanvasEditor'
import { TemplatePanel } from '../components/TemplatePanel/TemplatePanel'
import { PropertyPanel } from '../components/PropertyPanel/PropertyPanel'
import { VariablesPanel } from '../components/VariablesPanel/VariablesPanel'
import { LayersPanel } from '../components/LayersPanel/LayersPanel'
import { Toolbar } from '../components/Toolbar/Toolbar'
import { useCanvasStore } from '../stores/canvasStore'

type LeftTab = 'assets' | 'layers'
type RightTab = 'design' | 'properties' | 'variables'

// Minimum / maximum widths for the side panels (in px)
const PANEL_MIN = 180
const PANEL_MAX = 500
const LEFT_DEFAULT = 288 // w-72
const RIGHT_DEFAULT = 320 // w-80

export function WidgetEditorPage({
  fontsLoaded,
  fontsError
}: {
  fontsLoaded: boolean
  fontsError: string | null
}) {
  const [leftTab, setLeftTab] = useState<LeftTab>('layers')
  const [rightTab, setRightTab] = useState<RightTab>('design')
  const { canvas, selectedObjectId } = useCanvasStore()

  // Resizable panel widths
  const [leftWidth, setLeftWidth] = useState(LEFT_DEFAULT)
  const [rightWidth, setRightWidth] = useState(RIGHT_DEFAULT)
  const [leftCollapsed, setLeftCollapsed] = useState(false)
  const [rightCollapsed, setRightCollapsed] = useState(false)

  // Auto-switch to properties tab when selecting an object
  useEffect(() => {
    if (selectedObjectId) {
      setRightTab('properties')
    }
  }, [selectedObjectId])

  // Force re-render text objects after fonts load
  useEffect(() => {
    if (fontsLoaded && canvas) {
      canvas.getObjects().forEach((obj) => {
        if (
          (obj as any).type === 'text' ||
          (obj as any).type === 'i-text' ||
          (obj as any).type === 'textbox'
        ) {
          obj.dirty = true
        }
      })
      canvas.renderAll()
    }
  }, [fontsLoaded, canvas])

  const leftTabs: Array<{ id: LeftTab; label: string }> = [
    { id: 'layers', label: 'Layers' },
    { id: 'assets', label: 'Assets' }
  ]

  const rightTabs: Array<{ id: RightTab; label: string }> = [
    { id: 'design', label: 'Design' },
    { id: 'properties', label: 'Properties' },
    { id: 'variables', label: 'Variables' }
  ]

  return (
    <div className="flex flex-col flex-1 overflow-hidden">
      <Toolbar />

      <div className="flex flex-1 overflow-hidden">
        {/* Left panel */}
        {!leftCollapsed ? (
          <div
            className="bg-fn-dark border-r border-fn-border flex flex-col overflow-hidden shrink-0"
            style={{ width: leftWidth }}
          >
            {/* Tab bar */}
            <div className="flex border-b border-fn-border shrink-0">
              {leftTabs.map((tab) => (
                <button
                  key={tab.id}
                  className={`flex-1 py-2 text-[11px] font-medium transition-colors relative ${
                    leftTab === tab.id
                      ? 'text-white'
                      : 'text-gray-500 hover:text-gray-300'
                  }`}
                  onClick={() => setLeftTab(tab.id)}
                >
                  {tab.label}
                  {leftTab === tab.id && (
                    <div className="absolute bottom-0 left-2 right-2 h-[2px] bg-fn-rare rounded-full" />
                  )}
                </button>
              ))}
              {/* Collapse button */}
              <button
                className="px-1.5 text-gray-600 hover:text-gray-300 transition-colors"
                onClick={() => setLeftCollapsed(true)}
                title="Collapse panel"
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M11 19l-7-7 7-7M18 19l-7-7 7-7" />
                </svg>
              </button>
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-y-auto min-h-0">
              {leftTab === 'layers' && <LayersPanel />}
              {leftTab === 'assets' && <AssetBrowser />}
            </div>
          </div>
        ) : (
          /* Collapsed left panel - thin strip */
          <div className="w-6 bg-fn-dark border-r border-fn-border flex flex-col items-center shrink-0">
            <button
              className="mt-2 p-1 text-gray-600 hover:text-white transition-colors rounded"
              onClick={() => setLeftCollapsed(false)}
              title="Expand panel"
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M13 5l7 7-7 7M6 5l7 7-7 7" />
              </svg>
            </button>
          </div>
        )}

        {/* Left resize handle */}
        {!leftCollapsed && (
          <ResizeHandle
            side="left"
            onResize={(delta) =>
              setLeftWidth((w) => Math.min(PANEL_MAX, Math.max(PANEL_MIN, w + delta)))
            }
            onDoubleClick={() => setLeftCollapsed(true)}
          />
        )}

        {/* Center: Canvas */}
        <CanvasEditor fontsLoaded={fontsLoaded} />

        {/* Right resize handle */}
        {!rightCollapsed && (
          <ResizeHandle
            side="right"
            onResize={(delta) =>
              setRightWidth((w) => Math.min(PANEL_MAX, Math.max(PANEL_MIN, w - delta)))
            }
            onDoubleClick={() => setRightCollapsed(true)}
          />
        )}

        {/* Right panel */}
        {!rightCollapsed ? (
          <div
            className="bg-fn-dark border-l border-fn-border flex flex-col overflow-hidden shrink-0"
            style={{ width: rightWidth }}
          >
            {/* Tab bar */}
            <div className="flex border-b border-fn-border shrink-0">
              {/* Collapse button */}
              <button
                className="px-1.5 text-gray-600 hover:text-gray-300 transition-colors"
                onClick={() => setRightCollapsed(true)}
                title="Collapse panel"
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M13 5l7 7-7 7M6 5l7 7-7 7" />
                </svg>
              </button>
              {rightTabs.map((tab) => (
                <button
                  key={tab.id}
                  className={`flex-1 py-2 text-[11px] font-medium transition-colors relative ${
                    rightTab === tab.id
                      ? 'text-white'
                      : 'text-gray-500 hover:text-gray-300'
                  }`}
                  onClick={() => setRightTab(tab.id)}
                >
                  {tab.label}
                  {rightTab === tab.id && (
                    <div className="absolute bottom-0 left-2 right-2 h-[2px] bg-fn-rare rounded-full" />
                  )}
                </button>
              ))}
            </div>

            {/* Tab content */}
            <div className="flex-1 overflow-y-auto min-h-0">
              {rightTab === 'design' && <TemplatePanel />}
              {rightTab === 'properties' && <PropertyPanel />}
              {rightTab === 'variables' && <VariablesPanel />}
            </div>
          </div>
        ) : (
          /* Collapsed right panel - thin strip */
          <div className="w-6 bg-fn-dark border-l border-fn-border flex flex-col items-center shrink-0">
            <button
              className="mt-2 p-1 text-gray-600 hover:text-white transition-colors rounded"
              onClick={() => setRightCollapsed(false)}
              title="Expand panel"
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M11 19l-7-7 7-7M18 19l-7-7 7-7" />
              </svg>
            </button>
          </div>
        )}
      </div>

      {fontsError && (
        <div className="absolute bottom-4 left-1/2 -translate-x-1/2 bg-yellow-900/80 text-yellow-200 px-4 py-2 rounded text-xs">
          Font warning: {fontsError}
        </div>
      )}
    </div>
  )
}

// ─── Resize Handle ──────────────────────────────────────────────────────────

function ResizeHandle({
  side,
  onResize,
  onDoubleClick
}: {
  side: 'left' | 'right'
  onResize: (delta: number) => void
  onDoubleClick: () => void
}) {
  const dragging = useRef(false)
  const lastX = useRef(0)

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault()
      dragging.current = true
      lastX.current = e.clientX

      const handleMove = (ev: MouseEvent) => {
        if (!dragging.current) return
        const delta = ev.clientX - lastX.current
        lastX.current = ev.clientX
        onResize(delta)
      }

      const handleUp = () => {
        dragging.current = false
        document.removeEventListener('mousemove', handleMove)
        document.removeEventListener('mouseup', handleUp)
        document.body.style.cursor = ''
        document.body.style.userSelect = ''
      }

      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
      document.addEventListener('mousemove', handleMove)
      document.addEventListener('mouseup', handleUp)
    },
    [onResize]
  )

  return (
    <div
      className="w-1 cursor-col-resize bg-transparent hover:bg-fn-rare/30 active:bg-fn-rare/50 transition-colors shrink-0"
      onMouseDown={handleMouseDown}
      onDoubleClick={onDoubleClick}
      title="Drag to resize, double-click to collapse"
    />
  )
}
