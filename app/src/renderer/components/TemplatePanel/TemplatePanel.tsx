import { useState, useEffect } from 'react'
import { useTemplateStore } from '../../stores/templateStore'
import { useCanvasStore } from '../../stores/canvasStore'
import { RARITY_TIERS, RARITY_COLORS } from '../../../shared/types'
import type { UserAssetEntry, AssetInspectResult } from '../../../shared/types'
import { RARITY_LABELS } from '../../styles/fortnite-theme'
import { widgetSpecToTemplate, templateToWidgetSpec } from '../../templates/UMGWidgetConverter'
import type { WidgetSpecJson } from '../../../shared/widget-spec'

export function TemplatePanel() {
  const {
    activeTemplate,
    activeRarity,
    templateWidth,
    templateHeight,
    setActiveTemplate,
    setActiveRarity,
    setTemplateDimensions,
    getAllTemplates,
    saveCurrentAsTemplate,
    deleteCustomTemplate,
    variableValues
  } = useTemplateStore()

  const {
    canvasBgColor,
    setCanvasBgColor,
    workspaceBgColor,
    setWorkspaceBgColor,
    gridEnabled,
    gridSize,
    setGridSize
  } = useCanvasStore()

  const [showSaveDialog, setShowSaveDialog] = useState(false)
  const [newTemplateName, setNewTemplateName] = useState('')
  const [newTemplateDesc, setNewTemplateDesc] = useState('')

  // Project Widgets state — loaded via sidecar copy-on-read
  const [projectWidgets, setProjectWidgets] = useState<UserAssetEntry[]>([])
  const [widgetsLoading, setWidgetsLoading] = useState(false)
  const [widgetsError, setWidgetsError] = useState<string | null>(null)
  const [inspectingWidget, setInspectingWidget] = useState<string | null>(null) // filePath being inspected
  const [inspectedResult, setInspectedResult] = useState<AssetInspectResult | null>(null)
  const [inspectError, setInspectError] = useState<string | null>(null)

  // Load project widget blueprints via sidecar (copy-on-read through SafeFileAccess)
  useEffect(() => {
    let cancelled = false
    setWidgetsLoading(true)
    setWidgetsError(null)
    window.electronAPI
      .forgeListUserAssets()
      .then((result) => {
        if (cancelled) return
        // Filter to only widget blueprints
        const widgets = result.assets.filter(
          (a) => a.assetClass.toLowerCase().includes('widgetblueprint')
        )
        setProjectWidgets(widgets)
      })
      .catch((err) => {
        if (cancelled) return
        setWidgetsError(String(err))
      })
      .finally(() => {
        if (!cancelled) setWidgetsLoading(false)
      })
    return () => { cancelled = true }
  }, [])

  // Inspect a widget via sidecar (copy-on-read — never opens .uasset directly)
  const handleInspectWidget = async (widget: UserAssetEntry) => {
    if (inspectingWidget === widget.filePath) {
      // Toggle off
      setInspectingWidget(null)
      setInspectedResult(null)
      setInspectError(null)
      return
    }
    setInspectingWidget(widget.filePath)
    setInspectedResult(null)
    setInspectError(null)
    try {
      const result = await window.electronAPI.forgeInspectAsset(widget.filePath)
      setInspectedResult(result)
    } catch (err) {
      setInspectError(String(err))
    }
  }

  const allTemplates = getAllTemplates()
  const builtInTemplates = allTemplates.filter((t) => !t.isCustom)
  const customTemplates = allTemplates.filter((t) => t.isCustom)

  const handleSaveTemplate = () => {
    if (!newTemplateName.trim()) return
    saveCurrentAsTemplate(newTemplateName.trim(), newTemplateDesc.trim())
    setNewTemplateName('')
    setNewTemplateDesc('')
    setShowSaveDialog(false)
  }

  const handleImportWidgetSpec = async () => {
    const result = await window.electronAPI.importWidgetSpec()
    console.log('[UMG Import] IPC result:', result)
    if (result.success && result.spec) {
      const spec = result.spec as WidgetSpecJson
      console.log('[UMG Import] Parsed spec:', spec.name, spec.width, 'x', spec.height, 'vars:', spec.variables?.length)
      const template = widgetSpecToTemplate(spec)
      console.log('[UMG Import] Template layers:', template.layers.length, template.layers.map(l => `${l.type}:${l.id}@(${l.left},${l.top},${l.width}x${l.height})`))
      console.log('[UMG Import] Template variables:', template.variables?.length)
      setActiveTemplate(template)
    }
  }

  const handleExportWidgetSpec = async () => {
    if (!activeTemplate) return
    const spec = templateToWidgetSpec(activeTemplate, variableValues)
    const json = JSON.stringify(spec, null, 2)
    await window.electronAPI.exportWidgetSpec(json)
  }

  return (
    <div className="p-3 space-y-4">
      {/* Templates */}
      <div>
        <div className="flex items-center justify-between mb-1.5">
          <SectionHeader>Templates</SectionHeader>
          <button
            className="text-[10px] px-2 py-0.5 rounded bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 transition-colors"
            onClick={() => setShowSaveDialog(!showSaveDialog)}
          >
            + Save As
          </button>
        </div>

        {showSaveDialog && (
          <div className="mb-2 p-2 border border-fn-border rounded bg-fn-darker space-y-1.5">
            <input
              type="text"
              className="input-field"
              placeholder="Template name"
              value={newTemplateName}
              onChange={(e) => setNewTemplateName(e.target.value)}
              autoFocus
            />
            <input
              type="text"
              className="input-field"
              placeholder="Description (optional)"
              value={newTemplateDesc}
              onChange={(e) => setNewTemplateDesc(e.target.value)}
            />
            <div className="flex gap-1">
              <button
                className="flex-1 py-1 text-[10px] bg-fn-rare text-white rounded hover:bg-fn-rare/80 transition-colors"
                onClick={handleSaveTemplate}
              >
                Save
              </button>
              <button
                className="flex-1 py-1 text-[10px] text-gray-400 bg-fn-panel border border-fn-border rounded hover:text-white transition-colors"
                onClick={() => setShowSaveDialog(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        )}

        <div className="space-y-1">
          {builtInTemplates.map((template) => (
            <button
              key={template.id}
              className={`w-full text-left p-2 rounded border text-xs transition-colors ${
                activeTemplate?.id === template.id
                  ? 'border-fn-rare bg-fn-rare/10 text-white'
                  : 'border-fn-border text-gray-400 hover:border-gray-500 hover:bg-fn-panel'
              }`}
              onClick={() => setActiveTemplate(template)}
            >
              <div className="font-medium">{template.name}</div>
              <div className="text-gray-500 text-[10px] mt-0.5">{template.description}</div>
            </button>
          ))}

          {customTemplates.length > 0 && (
            <>
              <div className="text-[9px] text-gray-600 uppercase tracking-wider pt-2 pb-0.5">Custom</div>
              {customTemplates.map((template) => (
                <div key={template.id} className="flex items-center gap-1">
                  <button
                    className={`flex-1 text-left p-2 rounded border text-xs transition-colors ${
                      activeTemplate?.id === template.id
                        ? 'border-fn-rare bg-fn-rare/10 text-white'
                        : 'border-fn-border text-gray-400 hover:border-gray-500 hover:bg-fn-panel'
                    }`}
                    onClick={() => setActiveTemplate(template)}
                  >
                    <div className="font-medium">{template.name}</div>
                    {template.description && (
                      <div className="text-gray-500 text-[10px] mt-0.5">{template.description}</div>
                    )}
                  </button>
                  <button
                    className="p-1 text-gray-600 hover:text-red-400 transition-colors"
                    onClick={() => deleteCustomTemplate(template.id)}
                    title="Delete template"
                  >
                    <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path d="M6 18L18 6M6 6l12 12"/>
                    </svg>
                  </button>
                </div>
              ))}
            </>
          )}

          {/* Blank canvas option */}
          <button
            className={`w-full text-left p-2 rounded border text-xs transition-colors ${
              !activeTemplate
                ? 'border-fn-rare bg-fn-rare/10 text-white'
                : 'border-fn-border text-gray-400 hover:border-gray-500 hover:bg-fn-panel'
            }`}
            onClick={() => setActiveTemplate(null)}
          >
            <div className="font-medium">Blank Canvas</div>
            <div className="text-gray-500 text-[10px] mt-0.5">Start from scratch</div>
          </button>
        </div>
      </div>

      {/* UMG Widget Spec */}
      <div>
        <SectionHeader>UMG Widget</SectionHeader>
        <div className="space-y-1">
          <button
            className="w-full text-left p-2 rounded border border-fn-border text-xs text-gray-400 hover:border-fn-epic hover:bg-fn-epic/10 hover:text-white transition-colors"
            onClick={handleImportWidgetSpec}
          >
            <div className="font-medium">Import Widget Spec</div>
            <div className="text-gray-500 text-[10px] mt-0.5">Load a .json from FortniteForge</div>
          </button>
          {activeTemplate?.category === 'umg' && (
            <button
              className="w-full text-left p-2 rounded border border-fn-epic/30 bg-fn-epic/5 text-xs text-fn-epic hover:bg-fn-epic/15 transition-colors"
              onClick={handleExportWidgetSpec}
            >
              <div className="font-medium">Export Widget Spec</div>
              <div className="text-fn-epic/60 text-[10px] mt-0.5">Save .json for FortniteForge to build .uasset</div>
            </button>
          )}
        </div>
      </div>

      {/* Project Widgets — loaded via sidecar copy-on-read */}
      <div>
        <SectionHeader>Project Widgets</SectionHeader>
        {widgetsLoading && (
          <p className="text-[10px] text-gray-500">Loading widgets...</p>
        )}
        {widgetsError && (
          <p className="text-[10px] text-red-400">Failed to load: {widgetsError}</p>
        )}
        {!widgetsLoading && !widgetsError && projectWidgets.length === 0 && (
          <p className="text-[10px] text-gray-600">No widget blueprints found in project</p>
        )}
        {projectWidgets.length > 0 && (
          <div className="space-y-1 max-h-48 overflow-y-auto">
            {projectWidgets.map((widget) => {
              const isActive = inspectingWidget === widget.filePath
              return (
                <div key={widget.filePath}>
                  <button
                    className={`w-full text-left p-2 rounded border text-xs transition-colors ${
                      isActive
                        ? 'border-fn-epic bg-fn-epic/10 text-white'
                        : 'border-fn-border text-gray-400 hover:border-gray-500 hover:bg-fn-panel'
                    }`}
                    onClick={() => handleInspectWidget(widget)}
                  >
                    <div className="flex items-center gap-1.5">
                      <svg className="w-3 h-3 text-gray-500 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                        <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                      </svg>
                      <span className="font-medium truncate">{widget.name}</span>
                    </div>
                    <div className="flex items-center gap-1 mt-0.5">
                      <span className="text-[9px] text-gray-600 truncate">{widget.relativePath}</span>
                    </div>
                  </button>

                  {/* Inspect result — read-only preview */}
                  {isActive && inspectedResult && (
                    <div className="mt-1 p-2 rounded border border-fn-epic/20 bg-fn-darker text-[10px] space-y-1.5">
                      <div className="flex items-center gap-1 text-fn-epic/70">
                        <svg className="w-3 h-3 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                        </svg>
                        <span>Read-only copy</span>
                      </div>
                      {inspectedResult.assetClass && (
                        <div className="text-gray-500">
                          Class: <span className="text-gray-300">{inspectedResult.assetClass}</span>
                        </div>
                      )}
                      {inspectedResult.exports.length > 0 && (
                        <div>
                          <div className="text-gray-500 mb-0.5">Exports ({inspectedResult.exports.length}):</div>
                          {inspectedResult.exports.slice(0, 8).map((exp, i) => (
                            <div key={i} className="text-gray-400 pl-2 truncate">
                              {exp.objectName} <span className="text-gray-600">({exp.exportType})</span>
                            </div>
                          ))}
                          {inspectedResult.exports.length > 8 && (
                            <div className="text-gray-600 pl-2">+{inspectedResult.exports.length - 8} more</div>
                          )}
                        </div>
                      )}
                      {inspectedResult.properties.length > 0 && (
                        <div>
                          <div className="text-gray-500 mb-0.5">Properties ({inspectedResult.properties.length}):</div>
                          {inspectedResult.properties.slice(0, 6).map((prop, i) => (
                            <div key={i} className="text-gray-400 pl-2 truncate">
                              {prop.name}: <span className="text-gray-300">{prop.value}</span>
                            </div>
                          ))}
                          {inspectedResult.properties.length > 6 && (
                            <div className="text-gray-600 pl-2">+{inspectedResult.properties.length - 6} more</div>
                          )}
                        </div>
                      )}
                    </div>
                  )}
                  {isActive && inspectError && (
                    <div className="mt-1 p-2 rounded border border-red-500/20 bg-fn-darker text-[10px] text-red-400">
                      Inspect failed: {inspectError}
                    </div>
                  )}
                  {isActive && !inspectedResult && !inspectError && (
                    <div className="mt-1 p-2 rounded border border-fn-border bg-fn-darker text-[10px] text-gray-500">
                      Inspecting...
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>

      {/* Rarity selector */}
      {activeTemplate?.category === 'item' && (
        <div>
          <SectionHeader>Rarity</SectionHeader>
          <div className="flex flex-wrap gap-1">
            {RARITY_TIERS.map((tier) => (
              <button
                key={tier}
                className={`px-2 py-0.5 rounded text-[11px] font-medium border transition-colors ${
                  activeRarity === tier
                    ? 'border-white/30 text-white'
                    : 'border-transparent text-gray-400 hover:text-white'
                }`}
                style={{
                  backgroundColor:
                    activeRarity === tier ? RARITY_COLORS[tier] + '40' : 'transparent'
                }}
                onClick={() => setActiveRarity(tier)}
              >
                {RARITY_LABELS[tier]}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Canvas */}
      <div>
        <SectionHeader>Canvas</SectionHeader>
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <input
              type="number"
              className="input-field w-[72px]"
              value={templateWidth}
              onChange={(e) =>
                setTemplateDimensions(parseInt(e.target.value) || 512, templateHeight)
              }
            />
            <span className="text-gray-600 text-xs">&times;</span>
            <input
              type="number"
              className="input-field w-[72px]"
              value={templateHeight}
              onChange={(e) =>
                setTemplateDimensions(templateWidth, parseInt(e.target.value) || 512)
              }
            />
          </div>
          <div className="flex gap-1">
            {[
              { label: '256', w: 256, h: 256 },
              { label: '512', w: 512, h: 512 },
              { label: '1024', w: 1024, h: 1024 },
              { label: '16:9', w: 1920, h: 1080 }
            ].map((preset) => (
              <button
                key={preset.label}
                className="px-2 py-0.5 text-[10px] text-gray-500 hover:text-white bg-fn-darker border border-fn-border rounded hover:border-gray-500 transition-colors"
                onClick={() => setTemplateDimensions(preset.w, preset.h)}
              >
                {preset.label}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Appearance */}
      <div>
        <SectionHeader>Appearance</SectionHeader>
        <div className="space-y-2">
          <ColorRow label="Canvas" value={canvasBgColor} onChange={setCanvasBgColor} />
          <ColorRow label="Workspace" value={workspaceBgColor} onChange={setWorkspaceBgColor} />
        </div>
      </div>

      {/* Grid settings */}
      {gridEnabled && (
        <div>
          <SectionHeader>Grid</SectionHeader>
          <div className="flex items-center gap-2">
            <label className="text-[11px] text-gray-400 w-14">Size</label>
            <input
              type="number"
              className="input-field w-16"
              value={gridSize}
              min={4}
              max={128}
              onChange={(e) => setGridSize(parseInt(e.target.value) || 16)}
            />
            <span className="text-[10px] text-gray-600">px</span>
          </div>
        </div>
      )}
    </div>
  )
}

function SectionHeader({ children }: { children: React.ReactNode }) {
  return (
    <h4 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-1.5">
      {children}
    </h4>
  )
}

function ColorRow({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="flex items-center gap-1.5">
      <label className="text-[11px] text-gray-400 w-14 shrink-0">{label}</label>
      <input
        type="color"
        className="w-6 h-6 rounded cursor-pointer border border-fn-border bg-transparent p-0.5 shrink-0"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <input
        type="text"
        className="input-field flex-1"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  )
}
