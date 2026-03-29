import { useEffect, useState } from 'react'
import type { WellVersedProject, WellVersedProjectList, WellVersedDiscoveredProject } from '../../shared/types'
import { useForgeStore } from '../stores/forgeStore'
import { ErrorMessage } from '../components/ErrorMessage'
import { forgeInstallBridge, forgeCreateDevCopy, forgeOpenInUefn } from '../lib/api'

// Helper to detect dev copy projects
function isDevCopy(project: WellVersedProject): boolean {
  return project.name.endsWith('_WellVersed_Dev')
}

function getBaseProjectName(project: WellVersedProject): string {
  return project.name.replace(/_WellVersed_Dev$/, '')
}

// Clipboard copy with brief feedback
function CopyPathButton({ path }: { path: string }) {
  const [copied, setCopied] = useState(false)

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(path)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // fallback — ignore
    }
  }

  return (
    <button
      onClick={handleCopy}
      className="inline-flex items-center px-1 py-0.5 text-gray-500 hover:text-gray-300 transition-colors"
      title={copied ? 'Copied!' : 'Copy path to clipboard'}
    >
      {copied ? (
        <span className="text-[9px] text-emerald-400">Copied!</span>
      ) : (
        <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M8 5H6a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2v-1M8 5a2 2 0 002 2h2a2 2 0 002-2M8 5a2 2 0 012-2h2a2 2 0 012 2m0 0h2a2 2 0 012 2v3m2 4H10m0 0l3-3m-3 3l3 3" />
        </svg>
      )}
    </button>
  )
}

// Group projects into linked pairs (main + dev copy)
interface ProjectPair {
  main: WellVersedProject | null
  devCopy: WellVersedProject | null
}

function groupProjectPairs(projects: WellVersedProject[]): { pairs: ProjectPair[]; standalone: WellVersedProject[] } {
  const devCopies = new Map<string, WellVersedProject>()
  const mainProjects = new Map<string, WellVersedProject>()
  const standalone: WellVersedProject[] = []

  // First pass: categorize
  for (const p of projects) {
    if (isDevCopy(p)) {
      const baseName = getBaseProjectName(p)
      devCopies.set(baseName, p)
    } else {
      mainProjects.set(p.name, p)
    }
  }

  // Second pass: create pairs
  const pairs: ProjectPair[] = []
  const pairedMains = new Set<string>()

  for (const [baseName, devProject] of devCopies) {
    const mainProject = mainProjects.get(baseName)
    if (mainProject) {
      pairs.push({ main: mainProject, devCopy: devProject })
      pairedMains.add(baseName)
    } else {
      standalone.push(devProject)
    }
  }

  // Add unpaired main projects as standalone
  for (const [name, p] of mainProjects) {
    if (!pairedMains.has(name)) {
      standalone.push(p)
    }
  }

  return { pairs, standalone }
}

export function ProjectsPage({ onNavigate, onProjectChanged }: { onNavigate?: (page: string) => void; onProjectChanged?: () => void }) {
  const storeProjectList = useForgeStore((s) => s.projectList)
  const storeLoading = useForgeStore((s) => s.projectListLoading)
  const fetchProjects = useForgeStore((s) => s.fetchProjects)

  const [error, setError] = useState<string | null>(null)

  // Add project form
  const [addPath, setAddPath] = useState('')
  const [addType, setAddType] = useState<'MyProject' | 'Library'>('MyProject')
  const [adding, setAdding] = useState(false)

  // Scan form
  const [scanPath, setScanPath] = useState('')
  const [scanning, setScanning] = useState(false)
  const [discovered, setDiscovered] = useState<WellVersedDiscoveredProject[]>([])

  useEffect(() => {
    fetchProjects()
  }, [fetchProjects])

  // Force-refresh the store's project list (bypass cache)
  async function reloadProjects() {
    useForgeStore.setState({ projectListFetchedAt: null })
    await fetchProjects()
  }

  async function handleAddProject() {
    if (!addPath.trim()) return
    try {
      setAdding(true)
      setError(null)
      await window.electronAPI.forgeAddProject(addPath.trim(), addType)
      setAddPath('')
      await reloadProjects()
      onProjectChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add project')
    } finally {
      setAdding(false)
    }
  }

  async function handleBrowseAdd() {
    try {
      const dir = await window.electronAPI.selectDirectory()
      if (dir) setAddPath(dir)
    } catch {
      // user cancelled
    }
  }

  async function handleRemoveProject(id: string) {
    try {
      setError(null)
      await window.electronAPI.forgeRemoveProject(id)
      await reloadProjects()
      onProjectChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove project')
    }
  }

  async function handleActivateProject(id: string) {
    try {
      setError(null)
      await window.electronAPI.wellVersedActivateProject(id)
      await reloadProjects()
      onProjectChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to activate project')
    }
  }

  async function handleBrowseScan() {
    try {
      const dir = await window.electronAPI.selectDirectory()
      if (dir) setScanPath(dir)
    } catch {
      // user cancelled
    }
  }

  async function handleScan() {
    if (!scanPath.trim()) return
    try {
      setScanning(true)
      setError(null)
      setDiscovered([])
      const results = await window.electronAPI.forgeScanProjects(scanPath.trim())
      setDiscovered(results)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to scan directory')
    } finally {
      setScanning(false)
    }
  }

  async function handleAddDiscovered(project: WellVersedDiscoveredProject, type: 'MyProject' | 'Library') {
    try {
      setError(null)
      await window.electronAPI.forgeAddProject(project.projectPath, type)
      // Mark as added in the discovered list
      setDiscovered((prev) =>
        prev.map((p) =>
          p.projectPath === project.projectPath ? { ...p, alreadyAdded: true } : p
        )
      )
      await reloadProjects()
      onProjectChanged?.()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add project')
    }
  }

  const projectList = storeProjectList

  if (storeLoading && !projectList) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-[11px] text-gray-400">Loading...</div>
      </div>
    )
  }

  const activeId = projectList?.activeProjectId ?? null
  const projects = projectList?.projects ?? []

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-semibold text-white">Projects</h1>
          <p className="text-[11px] text-gray-500 mt-0.5">
            Manage UEFN projects, configure safety tiers, and switch active project
          </p>
        </div>

        {/* Error display */}
        {error && (
          <ErrorMessage message={error} />
        )}

        {/* Project List */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Projects ({projects.length})
          </h3>

          {projects.length === 0 ? (
            <div className="bg-fn-panel border border-fn-border rounded-lg p-6 text-center">
              <p className="text-[11px] text-gray-400 mb-1">No projects added yet</p>
              <p className="text-[10px] text-gray-600">
                Add a project below or scan a directory to discover UEFN projects
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              {(() => {
                const { pairs, standalone } = groupProjectPairs(projects)
                return (
                  <>
                    {/* Linked pairs */}
                    {pairs.map((pair) => (
                      <LinkedProjectPair
                        key={pair.main?.id ?? pair.devCopy?.id ?? ''}
                        pair={pair}
                        activeId={activeId}
                        onActivate={handleActivateProject}
                        onRemove={handleRemoveProject}
                      />
                    ))}
                    {/* Standalone projects */}
                    {standalone.map((project) => (
                      <ProjectCard
                        key={project.id}
                        project={project}
                        isActive={project.id === activeId}
                        onActivate={() => handleActivateProject(project.id)}
                        onRemove={() => handleRemoveProject(project.id)}
                      />
                    ))}
                  </>
                )
              })()}
            </div>
          )}
        </div>

        {/* Add Project */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Add Project
          </h3>
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4 space-y-3">
            {/* Path input */}
            <div className="flex gap-2">
              <input
                type="text"
                value={addPath}
                onChange={(e) => setAddPath(e.target.value)}
                placeholder="Path to UEFN project folder..."
                className="flex-1 bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
              />
              <button
                onClick={handleBrowseAdd}
                className="px-3 py-1.5 text-[10px] font-medium text-gray-300 bg-fn-darker border border-fn-border rounded hover:bg-white/[0.06] transition-colors shrink-0"
              >
                Browse
              </button>
            </div>

            {/* Safety note */}
            <div className="flex items-center gap-2 text-[10px] text-emerald-400/70">
              <svg className="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
              </svg>
              <span>Protected — all data accessed via read-only copies. Source files are never opened directly.</span>
            </div>

            {/* Add button */}
            <button
              onClick={handleAddProject}
              disabled={!addPath.trim() || adding}
              className="px-4 py-1.5 text-[10px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded hover:bg-fn-rare/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {adding ? 'Adding...' : 'Add Project'}
            </button>
          </div>
        </div>

        {/* Scan for Projects */}
        <div>
          <h3 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Scan for Projects
          </h3>
          <div className="bg-fn-panel border border-fn-border rounded-lg p-4 space-y-3">
            <p className="text-[10px] text-gray-500">
              Search a directory for UEFN projects. Useful for map collections.
            </p>
            <div className="flex gap-2">
              <input
                type="text"
                value={scanPath}
                onChange={(e) => setScanPath(e.target.value)}
                placeholder="Path to search for projects..."
                className="flex-1 bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[11px] text-white placeholder-gray-600 focus:outline-none focus:border-fn-rare/50"
              />
              <button
                onClick={handleBrowseScan}
                className="px-3 py-1.5 text-[10px] font-medium text-gray-300 bg-fn-darker border border-fn-border rounded hover:bg-white/[0.06] transition-colors shrink-0"
              >
                Browse
              </button>
              <button
                onClick={handleScan}
                disabled={!scanPath.trim() || scanning}
                className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-rare/20 border border-fn-rare/30 rounded hover:bg-fn-rare/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed shrink-0"
              >
                {scanning ? 'Scanning...' : 'Scan'}
              </button>
            </div>

            {/* Discovered projects */}
            {discovered.length > 0 && (
              <div className="space-y-1.5 pt-2 border-t border-fn-border">
                <div className="text-[10px] text-gray-400 mb-1">
                  Found {discovered.length} project{discovered.length !== 1 ? 's' : ''}
                </div>
                {discovered.map((dp) => (
                  <DiscoveredProjectRow
                    key={dp.projectPath}
                    project={dp}
                    onAdd={(type) => handleAddDiscovered(dp, type)}
                  />
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

function LinkedProjectPair({
  pair,
  activeId,
  onActivate,
  onRemove
}: {
  pair: ProjectPair
  activeId: string | null
  onActivate: (id: string) => void
  onRemove: (id: string) => void
}) {
  return (
    <div className="rounded-lg border border-fn-border overflow-hidden">
      {/* Main project */}
      {pair.main && (
        <ProjectCard
          project={pair.main}
          isActive={pair.main.id === activeId}
          onActivate={() => onActivate(pair.main!.id)}
          onRemove={() => onRemove(pair.main!.id)}
          roleLabel="Main Copy (PUBLISHABLE)"
          roleColor="text-emerald-400"
        />
      )}
      {/* Divider */}
      {pair.main && pair.devCopy && (
        <div className="border-t border-dashed border-fn-border" />
      )}
      {/* Dev copy */}
      {pair.devCopy && (
        <ProjectCard
          project={pair.devCopy}
          isActive={pair.devCopy.id === activeId}
          onActivate={() => onActivate(pair.devCopy!.id)}
          onRemove={() => onRemove(pair.devCopy!.id)}
          roleLabel="Dev Copy (BRIDGE ENABLED)"
          roleColor="text-blue-400"
        />
      )}
    </div>
  )
}

function ProjectCard({
  project,
  isActive,
  onActivate,
  onRemove,
  roleLabel,
  roleColor,
}: {
  project: WellVersedProject
  isActive: boolean
  onActivate: () => void
  onRemove: () => void
  roleLabel?: string
  roleColor?: string
}) {
  const isLibrary = project.type === 'Library'
  const isDev = isDevCopy(project)
  const typeBadge = isLibrary
    ? 'text-blue-400 bg-blue-400/10 border-blue-400/20'
    : isDev
      ? 'text-blue-400 bg-blue-400/10 border-blue-400/20'
      : 'text-emerald-400 bg-emerald-400/10 border-emerald-400/20'

  const [installing, setInstalling] = useState(false)
  const [creatingCopy, setCreatingCopy] = useState(false)
  const [bridgeStatus, setBridgeStatus] = useState<'unknown' | 'installed' | 'not_installed'>('unknown')
  const [actionMsg, setActionMsg] = useState<string | null>(null)
  const [actionType, setActionType] = useState<'success' | 'error' | 'warning'>('success')
  const [openingUefn, setOpeningUefn] = useState(false)

  async function handleCreateDevCopy() {
    try {
      setCreatingCopy(true)
      setActionMsg(null)
      const result = await forgeCreateDevCopy(project.projectPath)
      setActionType('success')
      setActionMsg(`Dev copy created with bridge installed (${result.filesCopied} files). Open "${result.devCopyPath}" in UEFN and enable Python Editor Scripting.`)
    } catch (err) {
      setActionType('error')
      setActionMsg(err instanceof Error ? err.message : 'Failed to create dev copy')
    } finally {
      setCreatingCopy(false)
    }
  }

  async function handleInstallBridge() {
    try {
      setInstalling(true)
      setActionMsg(null)
      const result = await forgeInstallBridge(project.projectPath)
      setBridgeStatus('installed')
      setActionType('warning')
      setActionMsg(result.message + ' WARNING: This enables experimental features which prevent publishing.')
    } catch (err) {
      setActionType('error')
      setActionMsg(err instanceof Error ? err.message : 'Failed to install bridge')
    } finally {
      setInstalling(false)
    }
  }

  async function handleOpenInUefn() {
    try {
      setOpeningUefn(true)
      setActionMsg(null)
      await forgeOpenInUefn(project.projectPath)
      setActionType('success')
      setActionMsg('Opening in UEFN...')
      setTimeout(() => setActionMsg(null), 3000)
    } catch (err) {
      setActionType('error')
      setActionMsg(err instanceof Error ? err.message : 'Failed to open in UEFN')
    } finally {
      setOpeningUefn(false)
    }
  }

  const dotColor = isDev ? 'bg-blue-400' : 'bg-emerald-400'
  const activeBorder = isDev ? 'border-blue-400/40 bg-blue-400/[0.03]' : 'border-emerald-400/40 bg-emerald-400/[0.03]'

  return (
    <div
      className={`bg-fn-panel p-3 transition-colors ${
        isActive ? activeBorder : 'border-fn-border'
      } ${roleLabel ? '' : 'border rounded-lg'}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 mb-0.5">
            {isActive && (
              <span className={`w-1.5 h-1.5 rounded-full ${dotColor} shrink-0`} />
            )}
            <span className="text-[11px] font-medium text-white truncate">{project.name}</span>
            {isActive && (
              <span className="inline-block px-1.5 py-0.5 rounded text-[9px] font-semibold border shrink-0 text-fn-rare bg-fn-rare/10 border-fn-rare/20">
                Active
              </span>
            )}
            <span className={`inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border shrink-0 ${typeBadge}`}>
              {isLibrary ? 'Library' : isDev ? 'DEV' : 'SAFE'}
            </span>
            {project.isUefnProject && (
              <span className="text-[9px] text-gray-600">UEFN</span>
            )}
            {bridgeStatus === 'installed' && (
              <span className="text-[9px] text-emerald-400 bg-emerald-400/10 border border-emerald-400/20 px-1.5 py-0.5 rounded">
                Bridge Installed
              </span>
            )}
          </div>

          {/* Role label for paired projects */}
          {roleLabel && (
            <p className={`text-[9px] font-medium mb-0.5 ${roleColor ?? 'text-gray-400'}`}>
              {roleLabel}
            </p>
          )}

          {/* Path with copy button */}
          <div className="flex items-center gap-1">
            <p className="text-[10px] text-gray-500 truncate">{project.projectPath}</p>
            <CopyPathButton path={project.projectPath} />
          </div>

          {/* Stats row */}
          <div className="flex items-center gap-3 mt-1.5">
            <span className="text-[10px] text-gray-500">
              {project.assetCount} assets
            </span>
            <span className="text-[10px] text-gray-600">|</span>
            <span className="text-[10px] text-gray-500">
              {project.levelCount} levels
            </span>
            <span className="text-[10px] text-gray-600">|</span>
            <span className="text-[10px] text-gray-500">
              {project.verseFileCount} verse
            </span>
          </div>

          {/* Action feedback */}
          {actionMsg && (
            <p className={`text-[9px] mt-1.5 leading-relaxed ${
              actionType === 'success' ? 'text-emerald-400/70' :
              actionType === 'warning' ? 'text-amber-400/70' :
              'text-red-400/70'
            }`}>
              {actionMsg}
            </p>
          )}
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-1.5 shrink-0">
          {/* Open in UEFN */}
          {project.isUefnProject && (
            <button
              onClick={handleOpenInUefn}
              disabled={openingUefn}
              className="px-2 py-1 text-[10px] font-medium text-gray-400 bg-fn-darker border border-fn-border rounded hover:text-white hover:border-gray-500 transition-colors disabled:opacity-40"
              title="Open this project in UEFN"
            >
              <span className="flex items-center gap-1">
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                  <path d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                {openingUefn ? 'Opening...' : 'Open UEFN'}
              </span>
            </button>
          )}
          {project.isUefnProject && !isLibrary && !isDev && (
            <>
              <button
                onClick={handleCreateDevCopy}
                disabled={creatingCopy}
                className="px-2 py-1 text-[10px] font-medium text-cyan-400 bg-cyan-400/10 border border-cyan-400/20 rounded hover:bg-cyan-400/20 transition-colors disabled:opacity-40"
                title="Create a development copy with bridge pre-installed. Your original project stays clean and publishable."
              >
                {creatingCopy ? 'Copying...' : 'Create Dev Copy'}
              </button>
              {bridgeStatus !== 'installed' && (
                <button
                  onClick={handleInstallBridge}
                  disabled={installing}
                  className="px-2 py-1 text-[10px] font-medium text-gray-500 bg-fn-darker border border-fn-border rounded hover:text-amber-400 hover:border-amber-400/30 transition-colors disabled:opacity-40"
                  title="Install bridge directly (WARNING: requires experimental features, prevents publishing)"
                >
                  {installing ? '...' : 'Bridge Only'}
                </button>
              )}
            </>
          )}
          {!isActive && (
            <button
              onClick={onActivate}
              className="px-2 py-1 text-[10px] font-medium text-fn-rare bg-fn-rare/10 border border-fn-rare/20 rounded hover:bg-fn-rare/20 transition-colors"
            >
              Activate
            </button>
          )}
          <button
            onClick={onRemove}
            className="px-2 py-1 text-[10px] font-medium text-gray-500 bg-fn-darker border border-fn-border rounded hover:text-red-400 hover:border-red-400/30 transition-colors"
          >
            Remove
          </button>
        </div>
      </div>
    </div>
  )
}

function LinkedProjectPair({
  pair,
  activeId,
  onActivate,
  onRemove,
}: {
  pair: ProjectPair
  activeId: string | null
  onActivate: (id: string) => void
  onRemove: (id: string) => void
}) {
  return (
    <div className="border border-fn-border rounded-lg overflow-hidden">
      {/* Header */}
      <div className="px-3 py-1.5 bg-fn-dark/50 border-b border-fn-border flex items-center gap-2">
        <svg className="w-3 h-3 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101" />
          <path d="M10.172 13.828a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.102 1.101" />
        </svg>
        <span className="text-[9px] font-semibold text-gray-500 uppercase tracking-wider">Linked Project Pair</span>
      </div>

      {/* Main copy */}
      {pair.main && (
        <ProjectCard
          project={pair.main}
          isActive={pair.main.id === activeId}
          onActivate={() => onActivate(pair.main!.id)}
          onRemove={() => onRemove(pair.main!.id)}
          roleLabel="MAIN COPY — Publishable"
          roleColor="text-emerald-400"
        />
      )}

      {/* Divider */}
      {pair.main && pair.devCopy && (
        <div className="border-t border-fn-border border-dashed" />
      )}

      {/* Dev copy */}
      {pair.devCopy && (
        <ProjectCard
          project={pair.devCopy}
          isActive={pair.devCopy.id === activeId}
          onActivate={() => onActivate(pair.devCopy!.id)}
          onRemove={() => onRemove(pair.devCopy!.id)}
          roleLabel="DEV COPY — Bridge Enabled (Experimental)"
          roleColor="text-blue-400"
        />
      )}
    </div>
  )
}

function DiscoveredProjectRow({
  project,
  onAdd
}: {
  project: WellVersedDiscoveredProject
  onAdd: (type: 'MyProject' | 'Library') => void
}) {
  return (
    <div className="flex items-center gap-3 bg-fn-darker/50 border border-fn-border/50 rounded px-3 py-2">
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="text-[11px] font-medium text-white truncate">{project.projectName}</span>
          {project.isUefnProject && (
            <span className="text-[9px] text-gray-600">UEFN</span>
          )}
        </div>
        <p className="text-[10px] text-gray-500 truncate">{project.projectPath}</p>
        <div className="flex items-center gap-2 mt-0.5">
          <span className="text-[9px] text-gray-600">{project.assetCount} assets</span>
          <span className="text-[9px] text-gray-600">{project.levelCount} levels</span>
          <span className="text-[9px] text-gray-600">{project.verseFileCount} verse</span>
        </div>
      </div>

      {project.alreadyAdded ? (
        <span className="text-[10px] text-gray-600 shrink-0">Added</span>
      ) : (
        <div className="flex items-center gap-1 shrink-0">
          <button
            onClick={() => onAdd('MyProject')}
            className="px-2 py-1 text-[9px] font-medium text-fn-rare bg-fn-rare/10 border border-fn-rare/20 rounded hover:bg-fn-rare/20 transition-colors"
          >
            + My Project
          </button>
          <button
            onClick={() => onAdd('Library')}
            className="px-2 py-1 text-[9px] font-medium text-blue-400 bg-blue-400/10 border border-blue-400/20 rounded hover:bg-blue-400/20 transition-colors"
          >
            + Library
          </button>
        </div>
      )}
    </div>
  )
}
