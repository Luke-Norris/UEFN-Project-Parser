import { useEffect, useState, useMemo } from 'react'
import { useRecipeStore } from '../stores/recipeStore'
import { useForgeStore } from '../stores/forgeStore'
import { ErrorMessage } from '../components/ErrorMessage'
import type { DeviceEntry } from '../../shared/types'
import type { DeviceRecipe, RecipeDevice, RecipeWiring, RecipeCategory } from '../../shared/recipes'
import {
  generateRecipeId,
  computeCenter,
  inferCategory,
  RECIPE_CATEGORIES,
  validateRecipe,
} from '../../shared/recipes'

// ─── Recipe Card ─────────────────────────────────────────────────────────────

function RecipeCard({
  recipe,
  isActive,
  onSelect,
  onDelete,
}: {
  recipe: DeviceRecipe
  isActive: boolean
  onSelect: () => void
  onDelete: () => void
}) {
  const catInfo = RECIPE_CATEGORIES.find((c) => c.id === recipe.category)
  return (
    <div
      onClick={onSelect}
      className={`p-3 rounded-lg border cursor-pointer transition-all ${
        isActive
          ? 'border-blue-500/50 bg-blue-500/10'
          : 'border-fn-border bg-fn-dark hover:bg-white/[0.03] hover:border-fn-border/80'
      }`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[12px] font-semibold text-white truncate">{recipe.name}</div>
          <div className="text-[10px] text-gray-500 mt-0.5 line-clamp-2">{recipe.description}</div>
        </div>
        <button
          onClick={(e) => { e.stopPropagation(); onDelete() }}
          className="shrink-0 p-1 text-gray-600 hover:text-red-400 transition-colors"
          title="Delete recipe"
        >
          <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
          </svg>
        </button>
      </div>
      <div className="flex items-center gap-2 mt-2">
        <span className="text-[9px] px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border text-gray-400">
          {catInfo?.icon} {catInfo?.label ?? recipe.category}
        </span>
        <span className="text-[9px] text-gray-600">
          {recipe.devices.length} device{recipe.devices.length !== 1 ? 's' : ''}
        </span>
        {recipe.wiring.length > 0 && (
          <span className="text-[9px] text-gray-600">
            {recipe.wiring.length} wire{recipe.wiring.length !== 1 ? 's' : ''}
          </span>
        )}
      </div>
      {recipe.tags.length > 0 && (
        <div className="flex flex-wrap gap-1 mt-1.5">
          {recipe.tags.slice(0, 4).map((tag) => (
            <span key={tag} className="text-[8px] px-1 py-0.5 rounded bg-fn-darker text-gray-600">
              {tag}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

// ─── Recipe Detail Panel ─────────────────────────────────────────────────────

function RecipeDetail({ recipe }: { recipe: DeviceRecipe }) {
  const [deploying, setDeploying] = useState(false)
  const [deployResult, setDeployResult] = useState<string | null>(null)

  return (
    <div className="p-4 space-y-4">
      {/* Header */}
      <div>
        <h2 className="text-[14px] font-semibold text-white">{recipe.name}</h2>
        <p className="text-[11px] text-gray-400 mt-1">{recipe.description}</p>
        {recipe.sourceProject && (
          <p className="text-[9px] text-gray-600 mt-1">
            From: {recipe.sourceProject} {recipe.sourceLevel ? `/ ${recipe.sourceLevel}` : ''}
          </p>
        )}
      </div>

      {/* Devices */}
      <div>
        <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
          Devices ({recipe.devices.length})
        </h3>
        <div className="space-y-1">
          {recipe.devices.map((dev) => (
            <div key={dev.role} className="flex items-center gap-2 bg-fn-darker rounded px-3 py-2">
              <div className="w-2 h-2 rounded-full bg-blue-400 shrink-0" />
              <div className="min-w-0 flex-1">
                <div className="text-[11px] text-white font-medium truncate">{dev.role}</div>
                <div className="text-[9px] text-gray-500">{dev.displayName}</div>
              </div>
              <div className="text-[9px] text-gray-600 font-mono shrink-0">
                {dev.properties.length} props
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Wiring */}
      {recipe.wiring.length > 0 && (
        <div>
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Wiring ({recipe.wiring.length})
          </h3>
          <div className="space-y-1">
            {recipe.wiring.map((wire, i) => (
              <div key={i} className="flex items-center gap-1 text-[10px] bg-fn-darker rounded px-3 py-1.5">
                <span className="text-yellow-400 font-medium">{wire.sourceRole}</span>
                <span className="text-gray-600">.{wire.outputEvent}</span>
                <svg className="w-3 h-3 text-gray-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M14 5l7 7m0 0l-7 7m7-7H3" />
                </svg>
                <span className="text-green-400 font-medium">{wire.targetRole}</span>
                <span className="text-gray-600">.{wire.inputAction}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Device Properties */}
      {recipe.devices.some((d) => d.properties.length > 0) && (
        <div>
          <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Configuration
          </h3>
          {recipe.devices
            .filter((d) => d.properties.length > 0)
            .map((dev) => (
              <div key={dev.role} className="mb-2">
                <div className="text-[10px] text-gray-400 font-medium mb-1">{dev.role}</div>
                <div className="space-y-0.5">
                  {dev.properties.map((prop, i) => (
                    <div key={i} className="flex items-center gap-2 text-[9px] bg-fn-darker rounded px-2 py-1">
                      <span className="text-gray-500">{prop.name}</span>
                      <span className="text-gray-300 ml-auto">{prop.value}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
        </div>
      )}

      {/* Deploy button */}
      <div className="pt-2 border-t border-fn-border">
        <button
          onClick={() => {
            setDeploying(true)
            setDeployResult('Recipe deployment requires an active project with a template device. Coming soon!')
            setDeploying(false)
          }}
          disabled={deploying}
          className="w-full py-2 text-[11px] font-medium text-white bg-blue-600 hover:bg-blue-500 rounded transition-colors disabled:opacity-50"
        >
          {deploying ? 'Deploying...' : 'Deploy to Project'}
        </button>
        {deployResult && (
          <div className="mt-2 text-[10px] text-gray-400 text-center">{deployResult}</div>
        )}
      </div>
    </div>
  )
}

// ─── Create Recipe from Level ────────────────────────────────────────────────

function CreateRecipePanel({ onCreated }: { onCreated: (recipe: DeviceRecipe) => void }) {
  const [levelPath, setLevelPath] = useState<string | null>(null)
  const [devices, setDevices] = useState<DeviceEntry[]>([])
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [recipeName, setRecipeName] = useState('')
  const [recipeDesc, setRecipeDesc] = useState('')
  const [saving, setSaving] = useState(false)

  const [localLevels, setLocalLevels] = useState<Array<{ filePath: string; name: string }>>([])
  const status = useForgeStore((s) => s.status)

  useEffect(() => {
    window.electronAPI.forgeListLevels()
      .then((result) => {
        const lvls = Array.isArray(result) ? result : []
        setLocalLevels(lvls)
        if (!levelPath && lvls.length > 0) {
          setLevelPath(lvls[0].filePath)
        }
      })
      .catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Load devices when level selected
  useEffect(() => {
    if (!levelPath) return
    setLoading(true)
    setError(null)
    window.electronAPI.forgeListDevices(levelPath)
      .then((result) => {
        setDevices(result?.devices ?? [])
      })
      .catch((err) => setError(err instanceof Error ? err.message : String(err)))
      .finally(() => setLoading(false))
  }, [levelPath])

  function toggleDevice(name: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })
  }

  function selectAll() {
    setSelected(new Set(devices.map((d) => d.name)))
  }

  async function handleCreate() {
    if (selected.size === 0 || !recipeName.trim()) return

    setSaving(true)
    try {
      const selectedDevices = devices.filter((d) => selected.has(d.name))
      const positions = selectedDevices
        .filter((d) => d.position)
        .map((d) => d.position!)
      const center = computeCenter(positions)

      // Build recipe devices with inspected properties
      const recipeDevices: RecipeDevice[] = []
      const wiring: RecipeWiring[] = []

      for (const dev of selectedDevices) {
        // Create role name from device name
        const role = dev.name
          .replace(/^BP_|^PBWA_|_C$/g, '')
          .replace(/\d+$/, '')
          .replace(/([A-Z])/g, '_$1')
          .toLowerCase()
          .replace(/^_/, '')
          .replace(/_+/g, '_') || dev.deviceType.toLowerCase().replace(/\s+/g, '_')

        const offset = dev.position
          ? { x: dev.position.x - center.x, y: dev.position.y - center.y, z: dev.position.z - center.z }
          : { x: 0, y: 0, z: 0 }

        // Try to inspect for properties (best-effort)
        let properties: Array<{ name: string; value: string; type: string }> = []
        try {
          const detail = await window.electronAPI.forgeInspectDevice(dev.filePath)
          if (detail?.properties) {
            properties = detail.properties
              .filter((p) => p.name !== 'RelativeLocation' && p.name !== 'RelativeRotation' && p.name !== 'RelativeScale3D')
              .map((p) => ({ name: p.name, value: p.value, type: p.type }))
          }
        } catch {
          // Inspection failed — save without properties
        }

        recipeDevices.push({
          role,
          deviceClass: dev.deviceType,
          displayName: dev.name,
          offset,
          rotation: { pitch: 0, yaw: 0, roll: 0 },
          scale: { x: 1, y: 1, z: 1 },
          properties,
          _sourceActorName: dev.name,
        })
      }

      const recipe: DeviceRecipe = {
        $schema: 'device-recipe-v1',
        id: generateRecipeId(),
        name: recipeName.trim(),
        description: recipeDesc.trim() || `${selectedDevices.length} devices from ${status?.projectName ?? 'project'}`,
        category: inferCategory(recipeDevices),
        tags: Array.from(new Set(selectedDevices.map((d) => d.deviceType))),
        createdAt: new Date().toISOString(),
        sourceProject: status?.projectName,
        sourceLevel: levels?.find((l) => l.filePath === levelPath)?.name,
        devices: recipeDevices,
        wiring,
      }

      onCreated(recipe)
      setRecipeName('')
      setRecipeDesc('')
      setSelected(new Set())
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const devicesByType = useMemo(() => {
    const groups = new Map<string, DeviceEntry[]>()
    for (const d of devices) {
      const type = d.deviceType || 'Unknown'
      if (!groups.has(type)) groups.set(type, [])
      groups.get(type)!.push(d)
    }
    return Array.from(groups.entries()).sort((a, b) => b[1].length - a[1].length)
  }, [devices])

  return (
    <div className="flex flex-col h-full">
      {/* Level selector */}
      <div className="px-3 py-2 border-b border-fn-border">
        <select
          value={levelPath ?? ''}
          onChange={(e) => setLevelPath(e.target.value || null)}
          className="w-full text-[10px] bg-fn-darker border border-fn-border rounded px-2 py-1.5 text-gray-300"
        >
          <option value="">Select level...</option>
          {localLevels.map((l) => (
            <option key={l.filePath} value={l.filePath}>{l.name}</option>
          ))}
        </select>
      </div>

      {/* Name + description */}
      <div className="px-3 py-2 border-b border-fn-border space-y-1.5">
        <input
          type="text"
          value={recipeName}
          onChange={(e) => setRecipeName(e.target.value)}
          placeholder="Recipe name..."
          className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
        />
        <input
          type="text"
          value={recipeDesc}
          onChange={(e) => setRecipeDesc(e.target.value)}
          placeholder="Description (optional)..."
          className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[10px] text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
        />
      </div>

      {/* Device list */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {loading && (
          <div className="flex items-center justify-center h-32">
            <div className="w-4 h-4 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin" />
          </div>
        )}
        {error && <div className="p-3"><ErrorMessage message={error} /></div>}

        {!loading && devices.length > 0 && (
          <div className="p-2 space-y-1">
            <button
              onClick={selectAll}
              className="w-full text-[9px] text-blue-400 hover:text-blue-300 py-1"
            >
              Select all ({devices.length})
            </button>
            {devicesByType.map(([type, devs]) => (
              <div key={type}>
                <div className="text-[9px] text-gray-600 font-semibold uppercase tracking-wider px-1 py-1">
                  {type} ({devs.length})
                </div>
                {devs.map((dev) => (
                  <label
                    key={dev.name}
                    className={`flex items-center gap-2 px-2 py-1 rounded text-[10px] cursor-pointer transition-colors ${
                      selected.has(dev.name) ? 'bg-blue-500/10 text-white' : 'text-gray-400 hover:bg-white/[0.02]'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={selected.has(dev.name)}
                      onChange={() => toggleDevice(dev.name)}
                      className="rounded border-fn-border"
                    />
                    <span className="truncate">{dev.name}</span>
                  </label>
                ))}
              </div>
            ))}
          </div>
        )}

        {!loading && devices.length === 0 && levelPath && (
          <div className="p-4 text-center text-[10px] text-gray-600">No devices found in this level</div>
        )}
      </div>

      {/* Create button */}
      <div className="px-3 py-2 border-t border-fn-border">
        <button
          onClick={handleCreate}
          disabled={selected.size === 0 || !recipeName.trim() || saving}
          className="w-full py-2 text-[11px] font-medium text-white bg-green-600 hover:bg-green-500 rounded transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
        >
          {saving ? 'Creating...' : `Create Recipe (${selected.size} devices)`}
        </button>
      </div>
    </div>
  )
}

// ─── Main Page ───────────────────────────────────────────────────────────────

export function RecipesPage() {
  const recipes = useRecipeStore((s) => s.recipes)
  const activeRecipe = useRecipeStore((s) => s.activeRecipe)
  const addRecipe = useRecipeStore((s) => s.addRecipe)
  const removeRecipe = useRecipeStore((s) => s.removeRecipe)
  const setActiveRecipe = useRecipeStore((s) => s.setActiveRecipe)
  const importRecipes = useRecipeStore((s) => s.importRecipes)

  const [search, setSearch] = useState('')
  const [filterCategory, setFilterCategory] = useState<RecipeCategory | 'all'>('all')
  const [showCreate, setShowCreate] = useState(false)

  const filteredRecipes = useMemo(() => {
    return recipes.filter((r) => {
      if (filterCategory !== 'all' && r.category !== filterCategory) return false
      if (search.trim()) {
        const q = search.toLowerCase()
        return (
          r.name.toLowerCase().includes(q) ||
          r.description.toLowerCase().includes(q) ||
          r.tags.some((t) => t.toLowerCase().includes(q)) ||
          r.devices.some((d) => d.displayName.toLowerCase().includes(q) || d.deviceClass.toLowerCase().includes(q))
        )
      }
      return true
    })
  }, [recipes, search, filterCategory])

  async function handleImport() {
    try {
      const result = await window.electronAPI.importWidgetSpec()
      if (result.success && result.spec) {
        const data = result.spec as DeviceRecipe | DeviceRecipe[]
        if (Array.isArray(data)) {
          const valid = data.filter(validateRecipe)
          importRecipes(valid)
        } else if (validateRecipe(data)) {
          addRecipe(data)
        }
      }
    } catch { /* user cancelled */ }
  }

  async function handleExport(recipe: DeviceRecipe) {
    try {
      await window.electronAPI.exportWidgetSpec(JSON.stringify(recipe, null, 2))
    } catch { /* user cancelled */ }
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
      {/* Left: Recipe List */}
      <div className="w-[320px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0">
        {/* Header */}
        <div className="px-3 py-2 border-b border-fn-border">
          <div className="flex items-center justify-between mb-2">
            <span className="text-[12px] font-semibold text-white">Device Recipes</span>
            <div className="flex items-center gap-1">
              <button
                onClick={handleImport}
                className="px-2 py-1 text-[9px] text-gray-400 hover:text-white bg-fn-darker border border-fn-border rounded hover:bg-white/[0.05] transition-colors"
                title="Import recipe from JSON"
              >
                Import
              </button>
              <button
                onClick={() => setShowCreate(!showCreate)}
                className={`px-2 py-1 text-[9px] font-medium rounded transition-colors ${
                  showCreate
                    ? 'text-white bg-blue-600'
                    : 'text-blue-400 bg-blue-400/10 border border-blue-400/20 hover:bg-blue-400/20'
                }`}
              >
                {showCreate ? 'Browse' : '+ Create'}
              </button>
            </div>
          </div>

          {!showCreate && (
            <>
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search recipes..."
                className="w-full bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
              />
              {/* Category filter */}
              <div className="flex flex-wrap gap-1 mt-2">
                <button
                  onClick={() => setFilterCategory('all')}
                  className={`px-1.5 py-0.5 text-[8px] rounded transition-colors ${
                    filterCategory === 'all' ? 'text-white bg-fn-panel' : 'text-gray-600 hover:text-gray-400'
                  }`}
                >
                  All
                </button>
                {RECIPE_CATEGORIES.map((cat) => (
                  <button
                    key={cat.id}
                    onClick={() => setFilterCategory(cat.id)}
                    className={`px-1.5 py-0.5 text-[8px] rounded transition-colors ${
                      filterCategory === cat.id ? 'text-white bg-fn-panel' : 'text-gray-600 hover:text-gray-400'
                    }`}
                  >
                    {cat.label}
                  </button>
                ))}
              </div>
            </>
          )}
        </div>

        {/* Content */}
        {showCreate ? (
          <CreateRecipePanel
            onCreated={(recipe) => {
              addRecipe(recipe)
              setActiveRecipe(recipe)
              setShowCreate(false)
            }}
          />
        ) : (
          <div className="flex-1 overflow-y-auto p-2 space-y-2">
            {filteredRecipes.length === 0 ? (
              <div className="text-center py-8">
                <div className="text-[11px] text-gray-600 mb-1">
                  {recipes.length === 0 ? 'No recipes yet' : 'No matches'}
                </div>
                <div className="text-[9px] text-gray-700">
                  {recipes.length === 0
                    ? 'Click "+ Create" to save a device pattern from your level'
                    : 'Try a different search or category'}
                </div>
              </div>
            ) : (
              filteredRecipes.map((recipe) => (
                <RecipeCard
                  key={recipe.id}
                  recipe={recipe}
                  isActive={activeRecipe?.id === recipe.id}
                  onSelect={() => setActiveRecipe(recipe)}
                  onDelete={() => removeRecipe(recipe.id)}
                />
              ))
            )}
          </div>
        )}
      </div>

      {/* Right: Recipe Detail */}
      <div className="flex-1 overflow-y-auto min-h-0">
        {activeRecipe ? (
          <RecipeDetail recipe={activeRecipe} />
        ) : (
          <div className="flex items-center justify-center h-full">
            <div className="text-center">
              <svg className="w-12 h-12 mx-auto mb-3 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                <path d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z" />
              </svg>
              <div className="text-[12px] text-gray-500 font-medium">Device Recipes</div>
              <div className="text-[10px] text-gray-700 mt-1 max-w-xs">
                Save multi-device patterns as reusable recipes. Select a recipe to view its devices, wiring, and configuration.
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
