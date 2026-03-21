import { useState } from 'react'
import { useTemplateStore } from '../../stores/templateStore'
import { useCanvasStore } from '../../stores/canvasStore'
import type { TemplateVariable } from '../../../shared/types'

export function VariablesPanel() {
  const {
    activeTemplate,
    variableValues,
    setVariableValue,
    addVariable,
    removeVariable
  } = useTemplateStore()
  const { canvas, pushHistory, swapLayerImage } = useCanvasStore()

  const [showAdd, setShowAdd] = useState(false)
  const [newVar, setNewVar] = useState({
    name: '',
    type: 'text' as TemplateVariable['type'],
    defaultValue: '',
    layerId: '',
    layerProperty: 'text'
  })

  const variables = activeTemplate?.variables || []

  // Get canvas layer IDs for the dropdown
  const canvasLayers: Array<{ id: string; name: string }> = []
  if (canvas) {
    for (const obj of canvas.getObjects()) {
      const layerId = (obj as any).layerId
      const layerName = (obj as any).layerName
      if (layerId && !layerId.endsWith('-label')) {
        canvasLayers.push({ id: layerId, name: layerName || layerId })
      }
    }
  }

  const handleAddVariable = () => {
    if (!newVar.name.trim()) return
    const variable: TemplateVariable = {
      id: `var-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`,
      name: newVar.name.trim(),
      type: newVar.type,
      defaultValue: newVar.defaultValue,
      layerId: newVar.layerId,
      layerProperty: newVar.layerProperty
    }
    addVariable(variable)
    setNewVar({ name: '', type: 'text', defaultValue: '', layerId: '', layerProperty: 'text' })
    setShowAdd(false)
  }

  // Apply variable value to canvas (non-image properties)
  const applyVariable = (variable: TemplateVariable, value: string) => {
    if (!canvas) return
    setVariableValue(variable.id, value)

    const obj = canvas.getObjects().find((o) => (o as any).layerId === variable.layerId)
    if (!obj) return

    const prop = variable.layerProperty
    if (prop === 'text') {
      ;(obj as any).set('text', value)
    } else if (prop === 'fill') {
      obj.set('fill', value)
    } else if (prop === 'fontSize') {
      ;(obj as any).set('fontSize', parseInt(value) || 24)
    } else if (prop === 'fontFamily') {
      ;(obj as any).set('fontFamily', value)
    } else if (prop === 'stroke') {
      obj.set('stroke', value)
    } else if (prop === 'opacity') {
      obj.set('opacity', parseFloat(value))
    }

    obj.setCoords()
    canvas.renderAll()
    pushHistory(JSON.stringify(canvas.toJSON(['layerId', 'layerName', 'widgetType'])))
  }

  // Browse for an image file and swap onto the target layer
  const handleImageBrowse = async (variable: TemplateVariable) => {
    const files = await window.electronAPI.importFiles()
    if (files.length === 0) return
    const file = files[0]

    setVariableValue(variable.id, file.name)
    await swapLayerImage(variable.layerId, file.dataUrl)
  }

  // Swap image using an asset from the asset store (by path)
  const handleImageFromAsset = async (variable: TemplateVariable, assetPath: string) => {
    const dataUrl = await window.electronAPI.getAssetData(assetPath)
    if (!dataUrl) return

    // Extract just the filename for display
    const name = assetPath.split(/[/\\]/).pop() || assetPath
    setVariableValue(variable.id, name)
    await swapLayerImage(variable.layerId, dataUrl)
  }

  const propertyOptions = [
    { label: 'Text', value: 'text' },
    { label: 'Fill Color', value: 'fill' },
    { label: 'Font Size', value: 'fontSize' },
    { label: 'Font Family', value: 'fontFamily' },
    { label: 'Stroke', value: 'stroke' },
    { label: 'Opacity', value: 'opacity' },
    { label: 'Image Source', value: 'src' }
  ]

  return (
    <div className="p-3 space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
          Variables
        </h4>
        <button
          className="text-[10px] px-2 py-0.5 rounded bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 transition-colors"
          onClick={() => setShowAdd(!showAdd)}
        >
          + Add
        </button>
      </div>

      {/* Info text */}
      {variables.length === 0 && !showAdd && (
        <div className="text-center py-4">
          <p className="text-xs text-gray-500">No variables defined</p>
          <p className="text-[10px] text-gray-600 mt-1">
            Variables let you bind controls to layer properties.
            Change a variable value and it updates the canvas.
          </p>
        </div>
      )}

      {/* Add variable form */}
      {showAdd && (
        <div className="p-2 border border-fn-border rounded bg-fn-darker space-y-2">
          <div>
            <label className="text-[10px] text-gray-400 block mb-0.5">Variable Name</label>
            <input
              type="text"
              className="input-field"
              placeholder="e.g. itemName"
              value={newVar.name}
              onChange={(e) => setNewVar({ ...newVar, name: e.target.value })}
              autoFocus
            />
          </div>
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="text-[10px] text-gray-400 block mb-0.5">Type</label>
              <select
                className="input-field"
                value={newVar.type}
                onChange={(e) => setNewVar({ ...newVar, type: e.target.value as TemplateVariable['type'] })}
              >
                <option value="text">Text</option>
                <option value="color">Color</option>
                <option value="number">Number</option>
                <option value="image">Image</option>
              </select>
            </div>
            <div>
              <label className="text-[10px] text-gray-400 block mb-0.5">Property</label>
              <select
                className="input-field"
                value={newVar.layerProperty}
                onChange={(e) => setNewVar({ ...newVar, layerProperty: e.target.value })}
              >
                {propertyOptions.map((opt) => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>
          </div>
          <div>
            <label className="text-[10px] text-gray-400 block mb-0.5">Target Layer</label>
            <select
              className="input-field"
              value={newVar.layerId}
              onChange={(e) => setNewVar({ ...newVar, layerId: e.target.value })}
            >
              <option value="">— Select a layer —</option>
              {canvasLayers.map((layer) => (
                <option key={layer.id} value={layer.id}>{layer.name}</option>
              ))}
            </select>
          </div>
          {newVar.type !== 'image' && (
            <div>
              <label className="text-[10px] text-gray-400 block mb-0.5">Default Value</label>
              <input
                type="text"
                className="input-field"
                placeholder="Default value"
                value={newVar.defaultValue}
                onChange={(e) => setNewVar({ ...newVar, defaultValue: e.target.value })}
              />
            </div>
          )}
          <div className="flex gap-1">
            <button
              className="flex-1 py-1 text-[10px] bg-fn-rare text-white rounded hover:bg-fn-rare/80 transition-colors disabled:opacity-40"
              onClick={handleAddVariable}
              disabled={!newVar.name.trim() || !newVar.layerId}
            >
              Add Variable
            </button>
            <button
              className="flex-1 py-1 text-[10px] text-gray-400 bg-fn-panel border border-fn-border rounded hover:text-white transition-colors"
              onClick={() => setShowAdd(false)}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Variable controls */}
      {variables.length > 0 && (
        <div className="space-y-2">
          {variables.map((v) => (
            <div key={v.id} className="p-2 border border-fn-border rounded bg-fn-darker/50">
              <div className="flex items-center justify-between mb-1">
                <span className="text-[11px] text-gray-300 font-medium">{v.name}</span>
                <div className="flex items-center gap-1">
                  <span className="text-[9px] text-gray-600 font-mono">{v.layerProperty}</span>
                  <button
                    className="text-gray-600 hover:text-red-400 transition-colors"
                    onClick={() => removeVariable(v.id)}
                    title="Remove variable"
                  >
                    <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path d="M6 18L18 6M6 6l12 12"/>
                    </svg>
                  </button>
                </div>
              </div>

              {/* Image variable control */}
              {v.type === 'image' || v.layerProperty === 'src' ? (
                <div className="space-y-1.5">
                  <div className="flex items-center gap-1.5">
                    <span className="text-[10px] text-gray-500 flex-1 truncate min-w-0">
                      {variableValues[v.id] || 'No image selected'}
                    </span>
                    <button
                      className="text-[10px] px-2 py-0.5 rounded bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 transition-colors shrink-0"
                      onClick={() => handleImageBrowse(v)}
                    >
                      Browse
                    </button>
                  </div>
                  {/* Asset path input for quick swapping */}
                  <div className="flex items-center gap-1">
                    <input
                      type="text"
                      className="input-field flex-1"
                      placeholder="Or paste asset path..."
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          const val = (e.target as HTMLInputElement).value.trim()
                          if (val) {
                            handleImageFromAsset(v, val)
                            ;(e.target as HTMLInputElement).value = ''
                          }
                        }
                      }}
                    />
                  </div>
                </div>
              ) : v.type === 'color' ? (
                /* Color variable control */
                <div className="flex items-center gap-1.5">
                  <input
                    type="color"
                    className="w-6 h-6 rounded cursor-pointer border border-fn-border bg-transparent p-0.5"
                    value={variableValues[v.id] || v.defaultValue || '#ffffff'}
                    onChange={(e) => applyVariable(v, e.target.value)}
                  />
                  <input
                    type="text"
                    className="input-field flex-1"
                    value={variableValues[v.id] || v.defaultValue || ''}
                    onChange={(e) => applyVariable(v, e.target.value)}
                  />
                </div>
              ) : v.type === 'number' ? (
                /* Number variable control */
                <input
                  type="number"
                  className="input-field"
                  value={variableValues[v.id] || v.defaultValue || '0'}
                  onChange={(e) => applyVariable(v, e.target.value)}
                />
              ) : (
                /* Text variable control */
                <input
                  type="text"
                  className="input-field"
                  value={variableValues[v.id] || v.defaultValue || ''}
                  onChange={(e) => applyVariable(v, e.target.value)}
                />
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
