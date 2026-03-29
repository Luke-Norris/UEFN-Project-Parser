import { useEffect, useState } from 'react'
import type { WellVersedProject, WellVersedProjectList, WellVersedDiscoveredProject } from '../../shared/types'
import { useForgeStore } from '../stores/forgeStore'
import { ErrorMessage } from '../components/ErrorMessage'
import { forgeInstallBridge } from '../lib/api'

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
              {projects.map((project) => (
                <ProjectCard
                  key={project.id}
                  project={project}
                  isActive={project.id === activeId}
                  onActivate={() => handleActivateProject(project.id)}
                  onRemove={() => handleRemoveProject(project.id)}
                />
              ))}
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

function ProjectCard({
  project,
  isActive,
  onActivate,
  onRemove
}: {
  project: WellVersedProject
  isActive: boolean
  onActivate: () => void
  onRemove: () => void
}) {
  const isLibrary = project.type === 'Library'
  const typeBadge = isLibrary
    ? 'text-blue-400 bg-blue-400/10 border-blue-400/20'
    : 'text-fn-rare bg-fn-rare/10 border-fn-rare/20'

  const [installing, setInstalling] = useState(false)
  const [bridgeStatus, setBridgeStatus] = useState<'unknown' | 'installed' | 'not_installed'>('unknown')
  const [installMsg, setInstallMsg] = useState<string | null>(null)

  // Check if bridge is already installed
  useEffect(() => {
    if (project.isUefnProject) {
      // Simple check — does Content/Python/wellversed exist?
      // We can't do fs checks from renderer, so we just show the button
      setBridgeStatus('unknown')
    }
  }, [project.isUefnProject])

  async function handleInstallBridge() {
    try {
      setInstalling(true)
      setInstallMsg(null)
      const result = await forgeInstallBridge(project.projectPath)
      setBridgeStatus('installed')
      setInstallMsg(result.message)
    } catch (err) {
      setInstallMsg(err instanceof Error ? err.message : 'Failed to install bridge')
    } finally {
      setInstalling(false)
    }
  }

  return (
    <div
      className={`bg-fn-panel border rounded-lg p-3 transition-colors ${
        isActive ? 'border-fn-rare/40 bg-fn-rare/[0.03]' : 'border-fn-border'
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 mb-0.5">
            {isActive && (
              <span className="w-1.5 h-1.5 rounded-full bg-fn-rare shrink-0" />
            )}
            <span className="text-[11px] font-medium text-white truncate">{project.name}</span>
            <span className={`inline-block px-1.5 py-0.5 rounded text-[9px] font-medium border shrink-0 ${typeBadge}`}>
              {isLibrary ? 'Library' : 'My Project'}
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
          <p className="text-[10px] text-gray-500 truncate">{project.projectPath}</p>

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

          {/* Install bridge feedback */}
          {installMsg && (
            <p className={`text-[9px] mt-1.5 ${bridgeStatus === 'installed' ? 'text-emerald-400/70' : 'text-amber-400/70'}`}>
              {installMsg}
            </p>
          )}
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-1.5 shrink-0">
          {project.isUefnProject && !isLibrary && bridgeStatus !== 'installed' && (
            <button
              onClick={handleInstallBridge}
              disabled={installing}
              className="px-2 py-1 text-[10px] font-medium text-cyan-400 bg-cyan-400/10 border border-cyan-400/20 rounded hover:bg-cyan-400/20 transition-colors disabled:opacity-40"
              title="Install Python bridge for live UEFN control"
            >
              {installing ? 'Installing...' : 'Install Bridge'}
            </button>
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
