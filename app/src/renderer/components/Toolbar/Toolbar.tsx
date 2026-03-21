import { applyZoom } from '../../lib/zoom'
import { useCanvasStore } from '../../stores/canvasStore'
import { useTemplateStore } from '../../stores/templateStore'
import { useSettingsStore } from '../../stores/settingsStore'

export function Toolbar() {
  const {
    canvas,
    zoom,
    setZoom,
    undo,
    redo,
    deleteSelected,
    copyObject,
    pasteObject,
    gridEnabled,
    setGridEnabled,
    gridSize,
    setGridSize,
    snapToGrid,
    setSnapToGrid,
    snapSize,
    setSnapSize,
    elementSnap,
    setElementSnap,
    resetRotation,
    selectedObjectId
  } = useCanvasStore()
  const { activeTemplate } = useTemplateStore()

  const hasSelection = !!selectedObjectId

  const canvasBgColor = useCanvasStore((s) => s.canvasBgColor)

  const handleExport = async () => {
    if (!canvas) return
    // Temporarily set background color for export (canvas bg is transparent for grid layering)
    const prevBg = canvas.backgroundColor
    canvas.backgroundColor = canvasBgColor
    canvas.renderAll()
    const dataUrl = canvas.toDataURL({
      format: 'png',
      multiplier: 2,
      quality: 1
    })
    // Restore transparent background
    canvas.backgroundColor = prevBg
    canvas.renderAll()
    const name = activeTemplate
      ? `${activeTemplate.id}-${Date.now()}.png`
      : `component-${Date.now()}.png`
    const result = await window.electronAPI.exportPng(dataUrl, name)
    if (result) {
      console.log('Exported to:', result)
    }
  }

  return (
    <div className="h-10 bg-fn-dark border-b border-fn-border flex items-center px-3 gap-0.5 shrink-0">
      {/* Brand */}
      <span className="text-xs font-bold text-fn-rare tracking-wider mr-2 select-none">WellVersed</span>

      <Divider />

      {/* Undo/Redo */}
      <Btn onClick={undo} title="Undo (Ctrl+Z)">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M3 10h10a5 5 0 015 5v2M3 10l4-4m-4 4l4 4"/></svg>
      </Btn>
      <Btn onClick={redo} title="Redo (Ctrl+Y)">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M21 10H11a5 5 0 00-5 5v2m15-7l-4-4m4 4l-4 4"/></svg>
      </Btn>

      <Divider />

      {/* Zoom */}
      <Btn onClick={() => setZoom(Math.max(0.1, zoom - 0.25))} title="Zoom Out">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3M8 11h6"/></svg>
      </Btn>
      <button
        className="px-1.5 py-0.5 text-[10px] text-gray-400 hover:text-white font-mono min-w-[40px] text-center rounded hover:bg-white/5 transition-colors"
        onClick={() => setZoom(1)}
        title="Reset to 100%"
      >
        {Math.round(zoom * 100)}%
      </button>
      <Btn onClick={() => setZoom(Math.min(5, zoom + 0.25))} title="Zoom In">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3M8 11h6M11 8v6"/></svg>
      </Btn>

      <Divider />

      {/* Snap controls */}
      <Toggle active={elementSnap} onClick={() => setElementSnap(!elementSnap)} title="Smart Guides">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M12 2v4m0 12v4m10-10h-4M6 12H2m15.07-5.07l-2.83 2.83M9.76 14.24l-2.83 2.83m0-10.14l2.83 2.83m4.48 4.48l2.83 2.83"/></svg>
      </Toggle>

      <Toggle active={gridEnabled} onClick={() => setGridEnabled(!gridEnabled)} title="Toggle Grid">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M3 3h18v18H3zM3 9h18M3 15h18M9 3v18M15 3v18"/></svg>
      </Toggle>

      {gridEnabled && (
        <>
          <NumInput
            value={gridSize}
            onChange={(v) => setGridSize(v || 16)}
            min={4}
            max={256}
            title="Grid Size"
            label="G"
          />
          <Toggle active={snapToGrid} onClick={() => setSnapToGrid(!snapToGrid)} title="Grid Snap">
            <svg className="w-3 h-3" viewBox="0 0 16 16" fill="currentColor"><path d="M8 1a1 1 0 011 1v2.586l1.293-1.293a1 1 0 111.414 1.414L8 8.414 4.293 4.707a1 1 0 011.414-1.414L7 4.586V2a1 1 0 011-1zM8 15a1 1 0 01-1-1v-2.586l-1.293 1.293a1 1 0 01-1.414-1.414L8 7.586l3.707 3.707a1 1 0 01-1.414 1.414L9 11.414V14a1 1 0 01-1 1z"/></svg>
          </Toggle>
          {snapToGrid && (
            <NumInput
              value={snapSize}
              onChange={(v) => setSnapSize(v || 8)}
              min={1}
              max={128}
              title="Snap Size"
              label="S"
            />
          )}
        </>
      )}

      {/* Selection-only actions */}
      {hasSelection && (
        <>
          <Divider />
          <Btn onClick={copyObject} title="Copy (Ctrl+C)">
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>
          </Btn>
          <Btn onClick={pasteObject} title="Paste (Ctrl+V)">
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2"/><rect x="8" y="2" width="8" height="4" rx="1"/></svg>
          </Btn>
          <Btn onClick={resetRotation} title="Reset Rotation">
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M1 4v6h6M23 20v-6h-6"/><path d="M20.49 9A9 9 0 005.64 5.64L1 10m22 4l-4.64 4.36A9 9 0 013.51 15"/></svg>
          </Btn>
          <Btn onClick={deleteSelected} title="Delete (Del)" danger>
            <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
          </Btn>
        </>
      )}

      <div className="flex-1" />

      {/* App UI Scale (persisted to settings) */}
      <UiZoomControls />

      <Divider />

      {/* Clear Canvas */}
      <Btn
        onClick={() => {
          if (!canvas) return
          canvas.clear()
          canvas.backgroundColor = 'transparent'
          canvas.renderAll()
        }}
        title="Clear Canvas"
      >
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M18.36 6.64A9 9 0 115.64 18.36 9 9 0 0118.36 6.64zM12 2v4m0 12v4"/><path d="m15 9-6 6M9 9l6 6"/></svg>
      </Btn>

      {/* Reload App */}
      <Btn
        onClick={() => window.location.reload()}
        title="Reload App"
      >
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M1 4v6h6"/><path d="M3.51 15a9 9 0 102.13-9.36L1 10"/></svg>
      </Btn>

      <Divider />

      <button
        className="px-4 py-1.5 text-xs bg-fn-rare text-white rounded-md hover:bg-fn-rare/80 font-medium transition-colors"
        onClick={handleExport}
      >
        Export PNG
      </button>
    </div>
  )
}

function Btn({
  onClick,
  title,
  children,
  danger
}: {
  onClick: () => void
  title: string
  children: React.ReactNode
  danger?: boolean
}) {
  return (
    <button
      className={`p-1.5 rounded-md transition-colors ${
        danger
          ? 'text-gray-400 hover:text-red-400 hover:bg-red-400/10'
          : 'text-gray-400 hover:text-white hover:bg-white/5'
      }`}
      onClick={onClick}
      title={title}
    >
      {children}
    </button>
  )
}

function Toggle({
  active,
  onClick,
  title,
  children
}: {
  active: boolean
  onClick: () => void
  title: string
  children: React.ReactNode
}) {
  return (
    <button
      className={`p-1.5 rounded-md transition-colors ${
        active
          ? 'bg-fn-rare/20 text-fn-rare'
          : 'text-gray-400 hover:text-white hover:bg-white/5'
      }`}
      onClick={onClick}
      title={title}
    >
      {children}
    </button>
  )
}

function NumInput({
  value,
  onChange,
  min,
  max,
  title,
  label
}: {
  value: number
  onChange: (v: number) => void
  min: number
  max: number
  title: string
  label: string
}) {
  return (
    <div className="flex items-center gap-0.5" title={title}>
      <span className="text-[9px] text-gray-500 font-medium">{label}</span>
      <input
        type="number"
        className="w-9 text-[10px] text-center bg-fn-darker border border-fn-border rounded px-0.5 py-0.5 text-gray-300 focus:border-fn-rare/50 focus:outline-none [appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none"
        value={value}
        onChange={(e) => onChange(parseInt(e.target.value) || min)}
        min={min}
        max={max}
      />
    </div>
  )
}

function Divider() {
  return <div className="h-5 w-px bg-fn-border mx-1" />
}

function UiZoomControls() {
  const uiZoom = useSettingsStore((s) => s.uiZoom)
  const setSetting = useSettingsStore((s) => s.setSetting)

  const setZoom = (z: number) => {
    const clamped = Math.round(Math.max(0.5, Math.min(3, z)) * 100) / 100
    setSetting('uiZoom', clamped)
    applyZoom(clamped)
  }

  // Apply saved zoom on mount
  if (document.body.style.zoom !== String(uiZoom)) {
    applyZoom(uiZoom)
  }

  return (
    <>
      <Btn onClick={() => setZoom(uiZoom - 0.1)} title="UI Scale Down">
        <span className="text-[10px] font-bold">A-</span>
      </Btn>
      <button
        className="px-1 py-0.5 text-[10px] text-gray-400 hover:text-white font-mono min-w-[32px] text-center rounded hover:bg-white/5 transition-colors"
        onClick={() => setZoom(1)}
        title="Reset UI Scale"
      >
        {Math.round(uiZoom * 100)}%
      </button>
      <Btn onClick={() => setZoom(uiZoom + 0.1)} title="UI Scale Up">
        <span className="text-[10px] font-bold">A+</span>
      </Btn>
    </>
  )
}
