import { useCallback, useEffect, useState } from 'react'
import { useCanvasStore } from '../../stores/canvasStore'
import {
  getWidgetInfo,
  getWidgetDisplayName,
  getSlotProperties,
  getWidgetProperties,
  WIDGET_TYPE_INFO,
  type WidgetPropertyDef,
  type SlotPropertyDef
} from '../../../shared/widget-rules'
import type { Canvas as FabricCanvas, FabricObject, FabricText } from 'fabric'

// === Icon map for widget type headers ===

function WidgetIcon({ icon }: { icon: string }) {
  const c = 'w-3.5 h-3.5'
  const sw = 1.8
  switch (icon) {
    case 'canvas':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <path d="M3 9h18M9 3v18" />
        </svg>
      )
    case 'overlay':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="2" y="2" width="12" height="12" rx="1" />
          <rect x="10" y="10" width="12" height="12" rx="1" />
        </svg>
      )
    case 'stack':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="3" width="18" height="5" rx="1" />
          <rect x="3" y="10" width="18" height="5" rx="1" />
          <rect x="3" y="17" width="18" height="5" rx="1" />
        </svg>
      )
    case 'grid':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <path d="M3 12h18M12 3v18" />
        </svg>
      )
    case 'size':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="4" y="4" width="16" height="16" rx="2" />
          <path d="M9 4v16M4 9h16" />
        </svg>
      )
    case 'scale':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" />
        </svg>
      )
    case 'image':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <path d="M21 15l-5-5L5 21" />
        </svg>
      )
    case 'text':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <path d="M4 7V4h16v3M9 20h6M12 4v16" />
        </svg>
      )
    case 'button':
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="7" width="18" height="10" rx="3" />
          <path d="M8 12h8" />
        </svg>
      )
    default:
      return (
        <svg className={c} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={sw}>
          <rect x="3" y="3" width="18" height="18" rx="2" />
        </svg>
      )
  }
}

// === Anchor preset grid ===

const ANCHOR_PRESETS = [
  ['TopLeft', 'TopCenter', 'TopRight'],
  ['CenterLeft', 'Center', 'CenterRight'],
  ['BottomLeft', 'BottomCenter', 'BottomRight']
] as const

function AnchorPresetGrid({
  value,
  onChange
}: {
  value: string
  onChange: (v: string) => void
}) {
  return (
    <div className="space-y-1">
      <div className="grid grid-cols-3 gap-0.5 w-[72px]">
        {ANCHOR_PRESETS.flat().map((preset) => (
          <button
            key={preset}
            className={`w-5 h-5 rounded-sm border transition-colors ${
              value === preset
                ? 'bg-fn-rare border-fn-rare'
                : 'bg-fn-darker border-fn-border hover:border-gray-500'
            }`}
            onClick={() => onChange(preset)}
            title={preset}
          >
            <span
              className={`block w-1.5 h-1.5 rounded-full mx-auto ${
                value === preset ? 'bg-white' : 'bg-gray-600'
              }`}
            />
          </button>
        ))}
      </div>
      <button
        className={`w-[72px] h-5 rounded-sm border text-[8px] font-mono transition-colors ${
          value === 'FullScreen'
            ? 'bg-fn-rare border-fn-rare text-white'
            : 'bg-fn-darker border-fn-border text-gray-500 hover:border-gray-500'
        }`}
        onClick={() => onChange('FullScreen')}
        title="Full Screen"
      >
        Full
      </button>
    </div>
  )
}

// === Justification button group ===

function JustificationButtons({
  value,
  onChange
}: {
  value: string
  onChange: (v: string) => void
}) {
  const opts = ['Left', 'Center', 'Right'] as const
  const icons = {
    Left: 'M4 6h16M4 12h10M4 18h14',
    Center: 'M4 6h16M7 12h10M5 18h14',
    Right: 'M4 6h16M10 12h10M6 18h14'
  }
  return (
    <div className="flex gap-0.5">
      {opts.map((opt) => (
        <button
          key={opt}
          className={`p-1.5 rounded border transition-colors ${
            value === opt
              ? 'bg-fn-rare/20 border-fn-rare text-fn-rare'
              : 'bg-fn-darker border-fn-border text-gray-500 hover:text-gray-300'
          }`}
          onClick={() => onChange(opt)}
          title={opt}
        >
          <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d={icons[opt]} />
          </svg>
        </button>
      ))}
    </div>
  )
}

// === Orientation toggle ===

function OrientationToggle({
  value,
  onChange
}: {
  value: string
  onChange: (v: 'Horizontal' | 'Vertical') => void
}) {
  return (
    <div className="flex gap-0.5">
      {(['Horizontal', 'Vertical'] as const).map((opt) => (
        <button
          key={opt}
          className={`px-2 py-1 rounded border text-[10px] transition-colors ${
            value === opt
              ? 'bg-fn-rare/20 border-fn-rare text-fn-rare'
              : 'bg-fn-darker border-fn-border text-gray-500 hover:text-gray-300'
          }`}
          onClick={() => onChange(opt)}
        >
          {opt === 'Horizontal' ? 'H' : 'V'}
        </button>
      ))}
    </div>
  )
}

// === Padding input (4 values) ===

function PaddingInput({
  value,
  onChange
}: {
  value: { left: number; top: number; right: number; bottom: number }
  onChange: (v: { left: number; top: number; right: number; bottom: number }) => void
}) {
  const update = (key: keyof typeof value, val: string) => {
    onChange({ ...value, [key]: parseInt(val) || 0 })
  }
  return (
    <div className="grid grid-cols-4 gap-1">
      {(['left', 'top', 'right', 'bottom'] as const).map((side) => (
        <div key={side}>
          <label className="text-[8px] text-gray-600 uppercase block text-center">
            {side[0]}
          </label>
          <input
            type="number"
            className="input-field text-center !px-1 !text-[10px]"
            value={value[side]}
            onChange={(e) => update(side, e.target.value)}
          />
        </div>
      ))}
    </div>
  )
}

// === Main component ===

interface UMGWidgetPropertiesProps {
  canvas: FabricCanvas
  selectedObjectId: string
  onRefresh: () => void
}

export function UMGWidgetProperties({
  canvas,
  selectedObjectId,
  onRefresh
}: UMGWidgetPropertiesProps) {
  const { pushHistory } = useCanvasStore()

  const [widgetType, setWidgetType] = useState<string | null>(null)
  const [parentWidgetType, setParentWidgetType] = useState<string | null>(null)
  const [umgProps, setUmgProps] = useState<Record<string, any>>({})

  // Read UMG properties from the selected Fabric object
  const refreshProps = useCallback(() => {
    if (!canvas || !selectedObjectId) return

    const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
    if (!obj) return

    const wt = (obj as any).widgetType as string | undefined
    setWidgetType(wt || null)

    // Find parent widget type by looking at all objects
    const parentId = (obj as any).parentId
    if (parentId) {
      const parentObj = canvas.getObjects().find((o) => (o as any).layerId === parentId)
      if (parentObj) {
        setParentWidgetType((parentObj as any).widgetType || null)
      } else {
        // parentId might match a widget name from the template hierarchy
        // Try reading it from any object that has that layerId
        setParentWidgetType(null)
      }
    } else {
      setParentWidgetType(null)
    }

    // Read all custom UMG props from the object
    const props: Record<string, any> = {}
    const customKeys = [
      'umgJustification',
      'umgVisibility',
      'umgOrientation',
      'displayLabel',
      'tintColor',
      'texturePath',
      'minWidth',
      'minHeight',
      'slotHAlign',
      'slotVAlign',
      'slotPadding',
      'slotSizeRule',
      'anchorPreset',
      'offsetLeft',
      'offsetTop',
      'offsetRight',
      'offsetBottom',
      'alignmentX',
      'alignmentY',
      'bAutoSize',
      'sizeBoxWidth',
      'sizeBoxHeight',
      'bOverrideWidth',
      'bOverrideHeight'
    ]
    for (const key of customKeys) {
      const val = (obj as any)[key]
      if (val !== undefined) {
        props[key] = val
      }
    }

    // Also read standard Fabric props that map to UMG
    props.text = (obj as any).text
    props.fill = (obj as any).fill
    props.fontSize = (obj as any).fontSize
    props.fontFamily = (obj as any).fontFamily

    setUmgProps(props)
  }, [canvas, selectedObjectId])

  useEffect(() => {
    refreshProps()
  }, [refreshProps])

  // Listen for canvas changes
  useEffect(() => {
    if (!canvas) return
    const handler = () => refreshProps()
    canvas.on('object:modified', handler)
    canvas.on('selection:updated', handler)
    return () => {
      canvas.off('object:modified', handler)
      canvas.off('selection:updated', handler)
    }
  }, [canvas, refreshProps])

  const updateUmgProp = useCallback(
    (key: string, value: any, fabricProp?: string) => {
      if (!canvas || !selectedObjectId) return

      const obj = canvas.getObjects().find((o) => (o as any).layerId === selectedObjectId)
      if (!obj) return

      // Store the UMG-specific value on the object
      ;(obj as any)[key] = value

      // If there's a direct Fabric property mapping, update the visual
      if (fabricProp) {
        if (fabricProp === 'text') {
          ;(obj as FabricText).set('text', value)
        } else if (fabricProp === 'fill') {
          obj.set('fill', value)
        } else {
          ;(obj as any).set(fabricProp, value)
        }
      }

      // Special handling for justification — updates textAlign
      if (key === 'umgJustification') {
        const align = (value as string).toLowerCase()
        ;(obj as any).set('textAlign', align)
      }

      // Special handling for visibility
      if (key === 'umgVisibility') {
        if (value === 'Collapsed' || value === 'Hidden') {
          obj.set('opacity', 0.15)
        } else {
          obj.set('opacity', 1)
        }
      }

      obj.setCoords()
      canvas.renderAll()
      setUmgProps((prev) => ({ ...prev, [key]: value }))
      onRefresh()
    },
    [canvas, selectedObjectId, onRefresh]
  )

  const commitHistory = useCallback(() => {
    if (!canvas) return
    pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  }, [canvas, pushHistory])

  if (!widgetType) return null

  const info = getWidgetInfo(widgetType)
  const displayName = getWidgetDisplayName(widgetType)
  const slotProps = parentWidgetType ? getSlotProperties(parentWidgetType) : []
  const widgetProps = getWidgetProperties(widgetType)

  return (
    <div className="space-y-3">
      {/* Widget Type Header */}
      <div className="bg-fn-darker/60 rounded-lg px-3 py-2 border border-fn-border/40">
        <div className="flex items-center gap-2">
          <div className="text-fn-rare">
            <WidgetIcon icon={info?.icon || 'canvas'} />
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-[11px] font-medium text-white truncate">{displayName}</p>
            <p className="text-[9px] text-gray-500 font-mono">
              {info?.isContainer ? 'Container' : 'Leaf'}{' '}
              {info?.isContainer && info.maxChildren === 1 && '(single child)'}
            </p>
          </div>
          {info?.isContainer && (
            <span className="text-[8px] px-1.5 py-0.5 rounded bg-fn-epic/15 text-fn-epic border border-fn-epic/30">
              Container
            </span>
          )}
        </div>
      </div>

      {/* Slot Properties — based on parent widget type */}
      {slotProps.length > 0 && (
        <UMGSection title={`Slot (${parentWidgetType})`}>
          {renderSlotProperties(slotProps, umgProps, updateUmgProp, commitHistory)}
        </UMGSection>
      )}

      {/* Widget-Specific Properties */}
      {widgetProps.length > 0 && (
        <UMGSection title={`${displayName} Properties`}>
          {renderWidgetProperties(widgetProps, umgProps, updateUmgProp, commitHistory)}
        </UMGSection>
      )}

      {/* Allowed Children — for containers */}
      {info?.isContainer && info.allowedChildren.length > 0 && (
        <UMGSection title="Allowed Children">
          <div className="flex flex-wrap gap-1">
            {info.allowedChildren.map((child) => {
              const childInfo = WIDGET_TYPE_INFO[child]
              return (
                <span
                  key={child}
                  className="inline-flex items-center gap-1 text-[9px] px-1.5 py-0.5 rounded bg-fn-border/30 text-gray-400 border border-fn-border/40"
                >
                  <span className="text-gray-500">
                    <WidgetIcon icon={childInfo?.icon || 'canvas'} />
                  </span>
                  {childInfo?.label || child}
                </span>
              )
            })}
          </div>
        </UMGSection>
      )}
    </div>
  )
}

// === Render helpers ===

function renderSlotProperties(
  slotProps: SlotPropertyDef[],
  values: Record<string, any>,
  onChange: (key: string, value: any) => void,
  onCommit: () => void
) {
  return (
    <div className="space-y-2">
      {slotProps.map((prop) => {
        if (prop.key === 'anchorPreset') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <AnchorPresetGrid
                value={values[prop.key] ?? prop.defaultValue ?? 'TopLeft'}
                onChange={(v) => {
                  onChange(prop.key, v)
                  onCommit()
                }}
              />
            </div>
          )
        }
        if (prop.type === 'padding') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <PaddingInput
                value={
                  values[prop.key] ?? { left: 0, top: 0, right: 0, bottom: 0 }
                }
                onChange={(v) => {
                  onChange(prop.key, v)
                  onCommit()
                }}
              />
            </div>
          )
        }
        return renderPropertyField(prop, values, onChange, onCommit)
      })}
    </div>
  )
}

function renderWidgetProperties(
  widgetProps: WidgetPropertyDef[],
  values: Record<string, any>,
  onChange: (key: string, value: any, fabricProp?: string) => void,
  onCommit: () => void
) {
  return (
    <div className="space-y-2">
      {widgetProps.map((prop) => {
        // Special renderers for specific property types
        if (prop.key === 'umgJustification') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <JustificationButtons
                value={values[prop.key] ?? prop.defaultValue ?? 'Center'}
                onChange={(v) => {
                  onChange(prop.key, v, prop.fabricProp)
                  onCommit()
                }}
              />
            </div>
          )
        }

        if (prop.key === 'umgOrientation') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <OrientationToggle
                value={values[prop.key] ?? prop.defaultValue ?? 'Horizontal'}
                onChange={(v) => {
                  onChange(prop.key, v)
                  onCommit()
                }}
              />
            </div>
          )
        }

        if (prop.type === 'color') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <UMGColorInput
                value={values[prop.key] ?? values.fill ?? prop.defaultValue ?? '#FFFFFF'}
                onChange={(v) => onChange(prop.key, v, prop.fabricProp)}
                onBlur={onCommit}
              />
            </div>
          )
        }

        if (prop.type === 'string') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <input
                type="text"
                className="input-field"
                value={values[prop.key] ?? values[prop.fabricProp || ''] ?? prop.defaultValue ?? ''}
                onChange={(e) => onChange(prop.key, e.target.value, prop.fabricProp)}
                onBlur={onCommit}
              />
            </div>
          )
        }

        if (prop.type === 'number') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <input
                type="number"
                className="input-field"
                value={values[prop.key] ?? prop.defaultValue ?? 0}
                min={prop.min}
                max={prop.max}
                step={prop.step}
                onChange={(e) => onChange(prop.key, parseInt(e.target.value) || 0)}
                onBlur={onCommit}
              />
            </div>
          )
        }

        if (prop.type === 'boolean') {
          return (
            <div key={prop.key} className="flex items-center gap-2">
              <input
                type="checkbox"
                className="rounded border-fn-border bg-fn-darker text-fn-rare focus:ring-fn-rare/30"
                checked={values[prop.key] ?? prop.defaultValue ?? false}
                onChange={(e) => {
                  onChange(prop.key, e.target.checked)
                  onCommit()
                }}
              />
              <UMGLabel>{prop.label}</UMGLabel>
            </div>
          )
        }

        if (prop.type === 'enum') {
          return (
            <div key={prop.key}>
              <UMGLabel>{prop.label}</UMGLabel>
              <select
                className="input-field"
                value={values[prop.key] ?? prop.defaultValue ?? ''}
                onChange={(e) => {
                  onChange(prop.key, e.target.value, prop.fabricProp)
                  onCommit()
                }}
              >
                {prop.options?.map((opt) => (
                  <option key={opt} value={opt}>
                    {opt}
                  </option>
                ))}
              </select>
            </div>
          )
        }

        return null
      })}
    </div>
  )
}

function renderPropertyField(
  prop: SlotPropertyDef,
  values: Record<string, any>,
  onChange: (key: string, value: any) => void,
  onCommit: () => void
) {
  if (prop.type === 'enum') {
    return (
      <div key={prop.key}>
        <UMGLabel>{prop.label}</UMGLabel>
        <select
          className="input-field"
          value={values[prop.key] ?? prop.defaultValue ?? ''}
          onChange={(e) => {
            onChange(prop.key, e.target.value)
            onCommit()
          }}
        >
          {prop.options?.map((opt) => (
            <option key={opt} value={opt}>
              {opt}
            </option>
          ))}
        </select>
      </div>
    )
  }

  if (prop.type === 'number') {
    return (
      <div key={prop.key}>
        <UMGLabel>{prop.label}</UMGLabel>
        <input
          type="number"
          className="input-field"
          value={values[prop.key] ?? prop.defaultValue ?? 0}
          onChange={(e) => onChange(prop.key, parseInt(e.target.value) || 0)}
          onBlur={onCommit}
        />
      </div>
    )
  }

  if (prop.type === 'boolean') {
    return (
      <div key={prop.key} className="flex items-center gap-2">
        <input
          type="checkbox"
          className="rounded border-fn-border bg-fn-darker text-fn-rare focus:ring-fn-rare/30"
          checked={values[prop.key] ?? prop.defaultValue ?? false}
          onChange={(e) => {
            onChange(prop.key, e.target.checked)
            onCommit()
          }}
        />
        <UMGLabel>{prop.label}</UMGLabel>
      </div>
    )
  }

  return null
}

// === Shared sub-components ===

function UMGSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="space-y-2">
      <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
        {title}
      </h4>
      {children}
    </div>
  )
}

function UMGLabel({ children }: { children: React.ReactNode }) {
  return <label className="text-[11px] text-gray-400 block mb-0.5">{children}</label>
}

function UMGColorInput({
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
