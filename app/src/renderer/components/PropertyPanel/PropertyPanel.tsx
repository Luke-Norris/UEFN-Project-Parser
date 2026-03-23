import { useEffect, useState, useCallback } from 'react'
import { useCanvasStore } from '../../stores/canvasStore'
import { FORTNITE_FONTS } from '../../styles/fortnite-theme'
import { UMGWidgetProperties } from './UMGWidgetProperties'
import type { FabricObject, FabricText } from 'fabric'

interface ObjectProps {
  left: number
  top: number
  width: number
  height: number
  scaleX: number
  scaleY: number
  angle: number
  fill: string
  text?: string
  fontSize?: number
  fontFamily?: string
  stroke?: string
  strokeWidth?: number
  opacity: number
  rx?: number
  ry?: number
  layerId: string
  layerName: string
  type: string
}

function getObjectProps(obj: FabricObject): ObjectProps {
  return {
    left: Math.round(obj.left || 0),
    top: Math.round(obj.top || 0),
    width: Math.round((obj.width || 0) * (obj.scaleX || 1)),
    height: Math.round((obj.height || 0) * (obj.scaleY || 1)),
    scaleX: obj.scaleX || 1,
    scaleY: obj.scaleY || 1,
    angle: Math.round(obj.angle || 0),
    fill: (obj.fill as string) || '#FFFFFF',
    text: (obj as any).text,
    fontSize: (obj as any).fontSize,
    fontFamily: (obj as any).fontFamily,
    stroke: (obj.stroke as string) || '',
    strokeWidth: obj.strokeWidth || 0,
    opacity: obj.opacity ?? 1,
    rx: (obj as any).rx || 0,
    ry: (obj as any).ry || 0,
    layerId: (obj as any).layerId || '',
    layerName: (obj as any).layerName || 'Object',
    type: (obj as any).type || 'object'
  }
}

const FONT_OPTIONS = [
  { label: 'Burbank Black', value: FORTNITE_FONTS.black },
  { label: 'Burbank Bold', value: FORTNITE_FONTS.bold },
  { label: 'Arial', value: 'Arial' },
  { label: 'Helvetica', value: 'Helvetica' },
  { label: 'Georgia', value: 'Georgia' },
  { label: 'Times New Roman', value: 'Times New Roman' },
  { label: 'Courier New', value: 'Courier New' },
  { label: 'Impact', value: 'Impact' },
  { label: 'Verdana', value: 'Verdana' }
]

export function PropertyPanel() {
  const { canvas, selectedObjectId, copyPropSet, pastePropSet, copiedPropSet, pushHistory } =
    useCanvasStore()
  const [props, setProps] = useState<ObjectProps | null>(null)
  const [hasWidgetType, setHasWidgetType] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleUmgRefresh = useCallback(() => {
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (obj) setProps(getObjectProps(obj))
    setRefreshKey((k) => k + 1)
  }, [canvas, selectedObjectId])

  useEffect(() => {
    if (!canvas || !selectedObjectId) {
      setProps(null)
      setHasWidgetType(false)
      return
    }

    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (obj) {
      setProps(getObjectProps(obj))
      setHasWidgetType(!!(obj as any).widgetType)
    }

    const onModified = () => {
      const o = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
      if (o) setProps(getObjectProps(o))
    }
    canvas.on('object:modified', onModified)
    canvas.on('object:scaling', onModified)
    canvas.on('object:moving', onModified)
    canvas.on('object:rotating', onModified)

    return () => {
      canvas.off('object:modified', onModified)
      canvas.off('object:scaling', onModified)
      canvas.off('object:moving', onModified)
      canvas.off('object:rotating', onModified)
    }
  }, [canvas, selectedObjectId])

  const updateProp = (key: string, value: any) => {
    if (!canvas || !selectedObjectId) return
    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return

    if (key === 'text') {
      ;(obj as FabricText).set('text', value)
    } else if (key === 'fontSize') {
      ;(obj as any).set('fontSize', parseInt(value) || 24)
    } else if (key === 'fontFamily') {
      ;(obj as any).set('fontFamily', value)
    } else if (key === 'fill') {
      obj.set('fill', value)
    } else if (key === 'stroke') {
      obj.set('stroke', value)
    } else if (key === 'strokeWidth') {
      obj.set('strokeWidth', parseFloat(value) || 0)
    } else if (key === 'left') {
      obj.set('left', parseInt(value) || 0)
    } else if (key === 'top') {
      obj.set('top', parseInt(value) || 0)
    } else if (key === 'opacity') {
      obj.set('opacity', parseFloat(value))
    } else if (key === 'angle') {
      obj.set('angle', parseInt(value) || 0)
    } else if (key === 'rx') {
      ;(obj as any).set('rx', parseInt(value) || 0)
      ;(obj as any).set('ry', parseInt(value) || 0)
    } else if (key === 'width') {
      const w = parseInt(value) || 1
      obj.set('scaleX', w / (obj.width || 1))
    } else if (key === 'height') {
      const h = parseInt(value) || 1
      obj.set('scaleY', h / (obj.height || 1))
    }

    obj.setCoords()
    canvas.renderAll()
    setProps(getObjectProps(obj))
  }

  // Commit history after user finishes typing (on blur)
  const commitHistory = () => {
    if (!canvas) return
    pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  }

  // --- Granular copy presets ---
  const handleCopy = (label: string, values: Record<string, any>) => {
    copyPropSet({ label, values })
  }

  const handlePaste = () => {
    pastePropSet()
    if (canvas && selectedObjectId) {
      const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
      if (obj) setProps(getObjectProps(obj))
    }
  }

  if (!props) {
    return (
      <div className="p-4">
        <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
          Properties
        </h3>
        <p className="text-xs text-gray-600">Select an element to edit</p>
      </div>
    )
  }

  const isText = props.text !== undefined
  const isRect = props.type === 'rect'
  const isImage = props.type === 'image'
  const isCircle = props.type === 'circle'

  return (
    <div className="p-4 space-y-4">
      {/* Header */}
      <div>
        <div className="flex items-center justify-between mb-1">
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
            Properties
          </h3>
          {copiedPropSet && (
            <button
              className="px-2 py-0.5 text-[10px] rounded bg-fn-rare/15 text-fn-rare border border-fn-rare/30 hover:bg-fn-rare/25 transition-colors"
              onClick={handlePaste}
              title={`Paste ${copiedPropSet.label}`}
            >
              Paste {copiedPropSet.label}
            </button>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-[9px] uppercase px-1.5 py-0.5 rounded bg-fn-border/50 text-gray-400 font-mono">
            {props.type}
          </span>
          <p className="text-sm font-medium text-white">{props.layerName}</p>
        </div>
        <p className="text-[10px] text-gray-600 font-mono mt-0.5">{props.layerId}</p>
      </div>

      {/* UMG Widget Properties — shown when the selected object has a widgetType */}
      {hasWidgetType && canvas && selectedObjectId && (
        <UMGWidgetProperties
          key={refreshKey}
          canvas={canvas}
          selectedObjectId={selectedObjectId}
          onRefresh={handleUmgRefresh}
        />
      )}

      {/* Text section */}
      {isText && (
        <Section
          title="Text"
          onCopy={() =>
            handleCopy('Text', {
              fontSize: props.fontSize,
              fontFamily: props.fontFamily,
              fill: props.fill
            })
          }
        >
          <div>
            <Label>Content</Label>
            <input
              type="text"
              className="input-field"
              value={props.text || ''}
              onChange={(e) => updateProp('text', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <Label>Font Size</Label>
              <input
                type="number"
                className="input-field"
                value={props.fontSize || 24}
                onChange={(e) => updateProp('fontSize', e.target.value)}
                onBlur={commitHistory}
              />
            </div>
            <div>
              <Label>Font</Label>
              <select
                className="input-field"
                value={props.fontFamily || 'sans-serif'}
                onChange={(e) => {
                  updateProp('fontFamily', e.target.value)
                  commitHistory()
                }}
              >
                {FONT_OPTIONS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </Section>
      )}

      {/* Transform section */}
      <Section
        title="Transform"
        onCopy={() =>
          handleCopy('Transform', {
            left: props.left,
            top: props.top,
            _width: props.width,
            _height: props.height,
            angle: props.angle
          })
        }
      >
        <div className="grid grid-cols-2 gap-2">
          <div>
            <div className="flex items-center justify-between">
              <Label>X</Label>
              <CopyBtn
                onClick={() => handleCopy('Position X', { left: props.left })}
              />
            </div>
            <input
              type="number"
              className="input-field"
              value={props.left}
              onChange={(e) => updateProp('left', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
          <div>
            <div className="flex items-center justify-between">
              <Label>Y</Label>
              <CopyBtn
                onClick={() => handleCopy('Position Y', { top: props.top })}
              />
            </div>
            <input
              type="number"
              className="input-field"
              value={props.top}
              onChange={(e) => updateProp('top', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-2">
          <div>
            <div className="flex items-center justify-between">
              <Label>W</Label>
              <CopyBtn
                onClick={() => handleCopy('Width', { _width: props.width })}
              />
            </div>
            <input
              type="number"
              className="input-field"
              value={props.width}
              onChange={(e) => updateProp('width', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
          <div>
            <div className="flex items-center justify-between">
              <Label>H</Label>
              <CopyBtn
                onClick={() => handleCopy('Height', { _height: props.height })}
              />
            </div>
            <input
              type="number"
              className="input-field"
              value={props.height}
              onChange={(e) => updateProp('height', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
        </div>
        <div className="grid grid-cols-2 gap-2">
          <div>
            <div className="flex items-center justify-between">
              <Label>Rotation</Label>
              <CopyBtn
                onClick={() => handleCopy('Rotation', { angle: props.angle })}
              />
            </div>
            <input
              type="number"
              className="input-field"
              value={props.angle}
              onChange={(e) => updateProp('angle', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
          {isRect && (
            <div>
              <Label>Corner Radius</Label>
              <input
                type="number"
                className="input-field"
                value={props.rx || 0}
                min={0}
                onChange={(e) => updateProp('rx', e.target.value)}
                onBlur={commitHistory}
              />
            </div>
          )}
        </div>
      </Section>

      {/* Appearance section — fill color only for shapes and text, not images */}
      <Section
        title="Appearance"
        onCopy={() =>
          handleCopy('Appearance', {
            ...(isImage ? {} : { fill: props.fill }),
            opacity: props.opacity
          })
        }
      >
        {!isImage && (
          <div>
            <div className="flex items-center justify-between">
              <Label>Fill Color</Label>
              <CopyBtn onClick={() => handleCopy('Fill', { fill: props.fill })} />
            </div>
            <ColorInput
              value={props.fill}
              onChange={(v) => updateProp('fill', v)}
              onBlur={commitHistory}
            />
          </div>
        )}
        <div>
          <div className="flex items-center justify-between">
            <Label>Opacity</Label>
            <CopyBtn
              onClick={() => handleCopy('Opacity', { opacity: props.opacity })}
            />
          </div>
          <div className="flex items-center gap-2">
            <input
              type="range"
              className="flex-1 accent-fn-rare"
              min={0}
              max={1}
              step={0.01}
              value={props.opacity}
              onChange={(e) => updateProp('opacity', e.target.value)}
              onMouseUp={commitHistory}
            />
            <span className="text-[10px] text-gray-500 w-8 text-right font-mono">
              {Math.round(props.opacity * 100)}%
            </span>
          </div>
        </div>
      </Section>

      {/* Border section — only for shapes and text, not images */}
      {!isImage && (
        <Section
          title="Border"
          onCopy={() =>
            handleCopy('Border', {
              stroke: props.stroke,
              strokeWidth: props.strokeWidth
            })
          }
        >
          <div>
            <Label>Color</Label>
            <ColorInput
              value={props.stroke || '#000000'}
              onChange={(v) => updateProp('stroke', v)}
              onBlur={commitHistory}
            />
          </div>
          <div>
            <Label>Weight</Label>
            <input
              type="number"
              className="input-field"
              value={props.strokeWidth}
              step={0.5}
              min={0}
              onChange={(e) => updateProp('strokeWidth', e.target.value)}
              onBlur={commitHistory}
            />
          </div>
        </Section>
      )}
    </div>
  )
}

/** Section with optional copy button on the header */
function Section({
  title,
  children,
  onCopy
}: {
  title: string
  children: React.ReactNode
  onCopy?: () => void
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
          {title}
        </h4>
        {onCopy && (
          <button
            className="text-[9px] text-gray-600 hover:text-gray-300 transition-colors px-1.5 py-0.5 rounded hover:bg-white/5"
            onClick={onCopy}
            title={`Copy ${title}`}
          >
            Copy
          </button>
        )}
      </div>
      {children}
    </div>
  )
}

/** Tiny inline copy button next to individual properties */
function CopyBtn({ onClick }: { onClick: () => void }) {
  return (
    <button
      className="text-gray-600 hover:text-gray-400 transition-colors p-0.5"
      onClick={onClick}
      title="Copy value"
    >
      <svg className="w-2.5 h-2.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <rect x="9" y="9" width="13" height="13" rx="2" />
        <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
      </svg>
    </button>
  )
}

function Label({ children }: { children: React.ReactNode }) {
  return <label className="text-[11px] text-gray-400 block mb-0.5">{children}</label>
}

function ColorInput({
  value,
  onChange,
  onBlur
}: {
  value: string
  onChange: (v: string) => void
  onBlur?: () => void
}) {
  return (
    <div className="flex items-center gap-1.5">
      <input
        type="color"
        className="w-7 h-7 rounded cursor-pointer border border-fn-border bg-transparent p-0.5"
        value={value && /^#[0-9a-fA-F]{6}$/.test(value) ? value : '#000000'}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
      />
      <input
        type="text"
        className="input-field flex-1"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onBlur={onBlur}
      />
    </div>
  )
}
