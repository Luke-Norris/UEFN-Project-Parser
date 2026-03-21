import { useEffect } from 'react'
import { useForgeStore } from '../stores/forgeStore'
import type { PageId } from '../components/Sidebar/Sidebar'

interface HomePageProps {
  onNavigate: (page: PageId) => void
}

export function HomePage({ onNavigate }: HomePageProps) {
  const status = useForgeStore((s) => s.status)
  const projectList = useForgeStore((s) => s.projectList)
  const fetchStatus = useForgeStore((s) => s.fetchStatus)
  const fetchProjects = useForgeStore((s) => s.fetchProjects)
  const statusLoading = useForgeStore((s) => s.statusLoading)
  const projectListLoading = useForgeStore((s) => s.projectListLoading)

  useEffect(() => {
    fetchStatus()
    fetchProjects()
  }, [fetchStatus, fetchProjects])

  const projectCount = projectList?.projects.length ?? 0
  const activeProject = projectList?.projects.find(
    (p) => p.id === projectList.activeProjectId
  )
  const isLoading = statusLoading && projectListLoading && !status && !projectList

  return (
    <div className="flex-1 bg-fn-darker overflow-y-auto min-h-0">
      <div className="max-w-3xl mx-auto p-6 space-y-5">
        {/* Welcome Card */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-5">
          <div className="flex items-start gap-4">
            <img src={new URL('../assets/logo.png', import.meta.url).href} alt="WellVersed" className="w-11 h-11 rounded-xl shrink-0" />
            <div>
              <h1 className="text-lg font-semibold text-white">Welcome to WellVersed</h1>
              <p className="text-[11px] text-gray-400 mt-1 leading-relaxed">
                UEFN project management studio. Browse assets, audit projects,
                design widgets, and manage your Fortnite maps.
              </p>
              <div className="mt-2 text-[10px] text-gray-600">v1.0.0</div>
            </div>
          </div>
        </div>

        {/* Projects Summary */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <h2 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-3">
            Projects
          </h2>
          {isLoading ? (
            <div className="text-[11px] text-gray-500">Loading...</div>
          ) : (
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex items-center gap-2">
                    <span className="text-2xl font-semibold text-white">{projectCount}</span>
                    <span className="text-[11px] text-gray-400">
                      project{projectCount !== 1 ? 's' : ''} registered
                    </span>
                  </div>
                </div>
                {activeProject && (
                  <div className="flex items-center gap-2">
                    <span className="w-1.5 h-1.5 rounded-full bg-fn-rare" />
                    <span className="text-[11px] text-gray-300">{activeProject.name}</span>
                    <span
                      className={`text-[9px] font-medium px-1.5 py-0.5 rounded border ${
                        activeProject.type === 'Library'
                          ? 'text-blue-400 bg-blue-400/10 border-blue-400/20'
                          : 'text-fn-rare bg-fn-rare/10 border-fn-rare/20'
                      }`}
                    >
                      {activeProject.type === 'Library' ? 'Library' : 'Active'}
                    </span>
                  </div>
                )}
              </div>

              {/* UEFN status */}
              {status?.isConfigured && (
                <div className="flex items-center gap-3 pt-2 border-t border-fn-border">
                  <div className="flex items-center gap-1.5 text-[10px]">
                    <span
                      className={`w-1.5 h-1.5 rounded-full ${
                        status.isUefnRunning ? 'bg-green-400' : 'bg-gray-600'
                      }`}
                    />
                    <span
                      className={status.isUefnRunning ? 'text-green-400' : 'text-gray-500'}
                    >
                      UEFN {status.isUefnRunning ? 'Running' : 'Not running'}
                    </span>
                  </div>
                  <div className="text-[10px] text-gray-500">
                    Mode: <span className="text-gray-300">{status.mode}</span>
                  </div>
                </div>
              )}

              {projectCount === 0 && (
                <p className="text-[10px] text-gray-600">
                  No projects yet. Add a UEFN project to get started.
                </p>
              )}
            </div>
          )}
        </div>

        {/* Quick Actions */}
        <div>
          <h2 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-2">
            Quick Actions
          </h2>
          <div className="grid grid-cols-2 gap-2">
            <QuickAction
              label="Add Project"
              description="Register a UEFN project or scan a directory"
              onClick={() => onNavigate('projects')}
              icon={
                <svg
                  className="w-4 h-4"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={1.5}
                >
                  <path d="M12 4v16m8-8H4" />
                </svg>
              }
            />
            <QuickAction
              label="Widget Editor"
              description="Design UMG widgets visually"
              onClick={() => onNavigate('widget-editor')}
              icon={
                <svg
                  className="w-4 h-4"
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={1.5}
                >
                  <path d="M4 5a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1V5zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1V5zM4 15a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1H5a1 1 0 01-1-1v-4zm10 0a1 1 0 011-1h4a1 1 0 011 1v4a1 1 0 01-1 1h-4a1 1 0 01-1-1v-4z" />
                </svg>
              }
            />
            {status?.isConfigured && (
              <>
                <QuickAction
                  label="Project Dashboard"
                  description="View project stats and status"
                  onClick={() => onNavigate('dashboard')}
                  icon={
                    <svg
                      className="w-4 h-4"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.5}
                    >
                      <path d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1h-2z" />
                    </svg>
                  }
                />
                <QuickAction
                  label="Audit Project"
                  description="Check for issues and warnings"
                  onClick={() => onNavigate('audit')}
                  icon={
                    <svg
                      className="w-4 h-4"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                      strokeWidth={1.5}
                    >
                      <path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                    </svg>
                  }
                />
              </>
            )}
          </div>
        </div>

        {/* Tips */}
        <div className="bg-fn-panel border border-fn-border rounded-lg p-4">
          <h2 className="text-[11px] font-semibold text-gray-500 uppercase tracking-wider mb-3">
            Getting Started
          </h2>
          <div className="space-y-2.5">
            <Tip
              number={1}
              text="Add your UEFN project via Projects. Point it at the folder containing .uefnproject."
            />
            <Tip
              number={2}
              text="Browse levels, devices, and assets from the sidebar. Select a level to inspect placed devices."
            />
            <Tip
              number={3}
              text="Use the Widget Editor to design UMG widgets visually and export them as .uasset files."
            />
            <Tip
              number={4}
              text="Run an Audit to check your project for common issues, missing references, and best practices."
            />
          </div>
        </div>
      </div>
    </div>
  )
}

function QuickAction({
  label,
  description,
  onClick,
  icon,
}: {
  label: string
  description: string
  onClick: () => void
  icon: JSX.Element
}) {
  return (
    <button
      onClick={onClick}
      className="text-left bg-fn-panel border border-fn-border rounded-lg p-3 hover:bg-white/[0.03] hover:border-fn-border/80 transition-colors group"
    >
      <div className="flex items-center gap-2 mb-0.5">
        <span className="text-gray-500 group-hover:text-fn-rare transition-colors">{icon}</span>
        <span className="text-[11px] font-medium text-white group-hover:text-fn-rare transition-colors">
          {label}
        </span>
      </div>
      <div className="text-[10px] text-gray-500 mt-0.5">{description}</div>
    </button>
  )
}

function Tip({ number, text }: { number: number; text: string }) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="w-5 h-5 rounded-full bg-fn-rare/10 border border-fn-rare/20 flex items-center justify-center text-[10px] font-semibold text-fn-rare shrink-0">
        {number}
      </span>
      <p className="text-[11px] text-gray-400 leading-relaxed">{text}</p>
    </div>
  )
}
