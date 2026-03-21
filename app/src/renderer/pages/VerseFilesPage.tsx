import { useEffect, useState, useMemo, useCallback } from 'react'
import { VerseHighlighter } from '../components/VerseHighlighter'
import { useSettingsStore } from '../stores/settingsStore'
import type { VerseFileContent } from '../../shared/types'

interface TreeNode {
  name: string
  path: string // relative path from content root
  isDir: boolean
  children: TreeNode[]
  size?: number
}

export function VerseFilesPage() {
  const [tree, setTree] = useState<TreeNode | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [expandedDirs, setExpandedDirs] = useState<Set<string>>(new Set())
  const [selectedFile, setSelectedFile] = useState<string | null>(null)
  const [fileContent, setFileContent] = useState<VerseFileContent | null>(null)
  const [fileLoading, setFileLoading] = useState(false)
  const verseEditorFontSize = useSettingsStore((s) => s.verseEditorFontSize)

  // Build tree from recursive browse
  useEffect(() => {
    buildTree()
  }, [])

  async function buildTree() {
    try {
      setLoading(true)
      setError(null)
      const root: TreeNode = { name: 'Content', path: '', isDir: true, children: [] }

      async function scanDir(node: TreeNode, path?: string) {
        const result = await window.electronAPI.forgeBrowseContent(path)
        for (const entry of result?.entries ?? []) {
          const isDir = entry.isDirectory || (entry as any).type === 'folder'
          if (isDir) {
            if (entry.name?.startsWith('__')) continue // skip __ExternalActors__ etc
            if (entry.name === 'Developers') continue
            if (entry.name === 'Collections') continue
            const child: TreeNode = { name: entry.name, path: entry.path || entry.relativePath || '', isDir: true, children: [] }
            node.children.push(child)
            await scanDir(child, entry.path || entry.relativePath)
          } else if (entry.name?.endsWith('.verse') || (entry as any).type === 'verse') {
            node.children.push({
              name: entry.name,
              path: entry.path || entry.relativePath || '',
              isDir: false,
              children: [],
              size: entry.size,
            })
          }
        }
        // Sort: dirs first, then alphabetical
        node.children.sort((a, b) => {
          if (a.isDir && !b.isDir) return -1
          if (!a.isDir && b.isDir) return 1
          return a.name.localeCompare(b.name)
        })
      }

      await scanDir(root)

      // Auto-expand root and first-level dirs
      const autoExpand = new Set<string>([''])
      for (const child of root.children) {
        if (child.isDir) autoExpand.add(child.path)
      }

      setTree(root)
      setExpandedDirs(autoExpand)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to scan verse files')
    } finally {
      setLoading(false)
    }
  }

  // Count verse files in tree
  const totalFiles = useMemo(() => {
    if (!tree) return 0
    let count = 0
    function walk(node: TreeNode) {
      if (!node.isDir) count++
      for (const c of node.children) walk(c)
    }
    walk(tree)
    return count
  }, [tree])

  // Flatten tree for search
  const allFiles = useMemo(() => {
    if (!tree) return []
    const files: TreeNode[] = []
    function walk(node: TreeNode) {
      if (!node.isDir) files.push(node)
      for (const c of node.children) walk(c)
    }
    walk(tree)
    return files
  }, [tree])

  const filteredFiles = useMemo(() => {
    if (!search.trim()) return null // null = show tree view
    const q = search.toLowerCase()
    return allFiles.filter((f) => f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q))
  }, [allFiles, search])

  const toggleDir = useCallback((path: string) => {
    setExpandedDirs((prev) => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })
  }, [])

  const handleSelectFile = useCallback(async (node: TreeNode) => {
    setSelectedFile(node.path)
    setFileLoading(true)
    try {
      const result = await window.electronAPI.forgeReadVerse(node.path)
      setFileContent(result)
    } catch {
      setFileContent(null)
    } finally {
      setFileLoading(false)
    }
  }, [])

  // Render a tree node recursively
  function renderNode(node: TreeNode, depth: number = 0): React.ReactNode {
    if (node.isDir) {
      const isExpanded = expandedDirs.has(node.path)
      const verseCount = node.children.reduce((acc, c) => {
        if (!c.isDir) return acc + 1
        let n = 0; function walk(x: TreeNode) { if (!x.isDir) n++; x.children.forEach(walk) }; walk(c); return acc + n
      }, 0)
      if (verseCount === 0) return null // hide empty dirs

      return (
        <div key={node.path}>
          <button
            onClick={() => toggleDir(node.path)}
            className="w-full flex items-center gap-1.5 py-1 hover:bg-white/[0.03] transition-colors text-left"
            style={{ paddingLeft: depth * 16 + 8 }}
          >
            <svg className={`w-3 h-3 text-gray-600 shrink-0 transition-transform ${isExpanded ? 'rotate-90' : ''}`}
              fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M9 5l7 7-7 7" />
            </svg>
            <svg className="w-3.5 h-3.5 text-yellow-500/70 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
            </svg>
            <span className="text-[11px] text-gray-300 flex-1 truncate">{node.name}</span>
            <span className="text-[9px] text-gray-600 shrink-0 pr-2">{verseCount}</span>
          </button>
          {isExpanded && node.children.map((c) => renderNode(c, depth + 1))}
        </div>
      )
    }

    // File node
    const isSelected = selectedFile === node.path
    return (
      <button
        key={node.path}
        onClick={() => handleSelectFile(node)}
        className={`w-full flex items-center gap-1.5 py-1 transition-colors text-left ${
          isSelected ? 'bg-blue-500/15 text-white' : 'text-gray-400 hover:bg-white/[0.03] hover:text-gray-200'
        }`}
        style={{ paddingLeft: depth * 16 + 8 }}
        title={node.path}
      >
        <svg className="w-3.5 h-3.5 text-green-400/60 shrink-0 ml-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
        </svg>
        <span className="text-[11px] truncate flex-1">{node.name.replace('.verse', '')}</span>
      </button>
    )
  }

  // Loading
  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-5 h-5 border-2 border-green-400/30 border-t-green-400 rounded-full animate-spin mx-auto mb-2" />
          <div className="text-[11px] text-gray-400">Scanning verse files...</div>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="text-[11px] text-red-400 mb-3">{error}</div>
          <button onClick={buildTree} className="px-3 py-1.5 text-[10px] font-medium text-white bg-fn-panel border border-fn-border rounded hover:bg-white/[0.06]">Retry</button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* Left: File Tree */}
      <div className="w-[280px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0 min-h-0">
        <div className="px-3 py-2 border-b border-fn-border shrink-0">
          <div className="flex items-center justify-between mb-1.5">
            <span className="text-[11px] font-semibold text-white">Verse Files</span>
            <span className="text-[9px] text-gray-500">{totalFiles} files</span>
          </div>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter files..."
            className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
          />
        </div>
        <div className="flex-1 overflow-y-auto min-h-0">
          {filteredFiles ? (
            // Search results — flat list
            filteredFiles.length === 0 ? (
              <div className="p-4 text-center text-[10px] text-gray-600">No matches</div>
            ) : (
              filteredFiles.map((f) => (
                <button
                  key={f.path}
                  onClick={() => handleSelectFile(f)}
                  className={`w-full flex items-center gap-2 px-3 py-1.5 text-left transition-colors ${
                    selectedFile === f.path ? 'bg-blue-500/15 text-white' : 'text-gray-400 hover:bg-white/[0.03]'
                  }`}
                >
                  <svg className="w-3.5 h-3.5 text-green-400/60 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
                  </svg>
                  <div className="min-w-0">
                    <div className="text-[11px] truncate">{f.name.replace('.verse', '')}</div>
                    <div className="text-[9px] text-gray-600 truncate">{f.path}</div>
                  </div>
                </button>
              ))
            )
          ) : (
            // Tree view
            tree && tree.children.map((c) => renderNode(c, 0))
          )}
        </div>
      </div>

      {/* Right: File Content */}
      <div className="flex-1 flex flex-col min-h-0 overflow-hidden">
        {fileLoading ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="w-5 h-5 border-2 border-green-400/30 border-t-green-400 rounded-full animate-spin" />
          </div>
        ) : fileContent ? (
          <>
            <div className="flex items-center gap-3 px-4 py-2 border-b border-fn-border bg-fn-dark shrink-0">
              <span className="text-[11px] font-semibold text-white">{fileContent.name}</span>
              <span className="text-[10px] text-gray-500">{fileContent.lineCount} lines</span>
              <span className="text-[9px] text-gray-600 font-mono truncate ml-auto">{selectedFile}</span>
            </div>
            <div className="flex-1 overflow-auto min-h-0">
              <VerseHighlighter
                code={fileContent.content}
                fontSize={verseEditorFontSize}
              />
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center">
              <svg className="w-10 h-10 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                <path d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
              </svg>
              <div className="text-[11px] text-gray-600">Select a verse file to view</div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
