import { useEffect, useState, useMemo, useCallback } from 'react'
import { VerseHighlighter } from '../components/VerseHighlighter'
import { useSettingsStore } from '../stores/settingsStore'
import type { VerseFileContent } from '../../shared/types'

interface TreeNode {
  name: string
  path: string
  isDir: boolean
  children: TreeNode[]
  size?: number
}

interface ParsedClass { name: string; parent?: string; startLine: number }
interface ParsedFunction { name: string; signature: string; startLine: number }
interface ParsedDevice { name: string; type: string; line: number }
interface ParsedImport { module: string; line: number }

function parseVerseSource(content: string) {
  if (!content) return { classes: [], functions: [], devices: [], imports: [] }
  const lines = content.split('\n')
  const classes: ParsedClass[] = []
  const functions: ParsedFunction[] = []
  const devices: ParsedDevice[] = []
  const imports: ParsedImport[] = []

  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i].trim()
    const usingMatch = trimmed.match(/^using\s*\{\s*(.+?)\s*\}/) || trimmed.match(/^using\s+(.+)/)
    if (usingMatch) { imports.push({ module: usingMatch[1], line: i + 1 }); continue }
    const classMatch = trimmed.match(/^(\w+)\s*(?:<[^>]*>\s*)?:=\s*class(?:\((\w+)\))?\s*:?\s*$/)
    if (classMatch) { classes.push({ name: classMatch[1], parent: classMatch[2], startLine: i + 1 }); continue }
    const funcMatch = trimmed.match(/^(\w+)\s*(?:<[^>]*>\s*)?\(([^)]*)\)(?:\s*<[^>]+>)*\s*(?::\s*(\w+))?\s*=\s*$/)
    if (funcMatch) { functions.push({ name: funcMatch[1], signature: trimmed.replace(/\s*=\s*$/, ''), startLine: i + 1 }); continue }
    const deviceMatch = trimmed.match(/^@editable\s+(\w+)\s*:\s*(\w+)/)
    if (deviceMatch) { devices.push({ name: deviceMatch[1], type: deviceMatch[2], line: i + 1 }) }
  }
  return { classes, functions, devices, imports }
}

// Parse persistent data structures from verse source
interface PersistField {
  name: string
  type: string
  defaultValue: string
  line: number
}

interface PersistStruct {
  name: string
  kind: 'persistable' | 'struct' | 'class'
  parent?: string
  fields: PersistField[]
  sourceFile: string
  startLine: number
}

function parsePersistentStructs(content: string, sourceFile: string): PersistStruct[] {
  if (!content) return []
  const lines = content.split('\n')
  const structs: PersistStruct[] = []
  let current: PersistStruct | null = null
  let baseIndent = 0

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    const trimmed = line.trim()

    // Match class/struct definitions
    const structMatch = trimmed.match(/^(\w+)(?:<[^>]*>)?\s*:=\s*(class|struct)(?:<([^>]+)>)?(?:\((\w+)\))?\s*:?\s*$/)
    if (structMatch) {
      const [, name, kind, modifiers, parent] = structMatch
      const isPersistable = (modifiers || '').includes('persistable')
      if (current) structs.push(current)
      current = {
        name,
        kind: isPersistable ? 'persistable' : kind as 'struct' | 'class',
        parent,
        fields: [],
        sourceFile,
        startLine: i + 1,
      }
      baseIndent = line.search(/\S/)
      continue
    }

    // If we're inside a struct, parse fields
    if (current) {
      const indent = line.search(/\S/)
      // If indent goes back to base or less, the struct ended
      if (indent >= 0 && indent <= baseIndent && trimmed && !trimmed.startsWith('#') && !trimmed.startsWith('//')) {
        structs.push(current)
        current = null
        // Re-check this line for a new struct
        i--
        continue
      }

      // Match field: FieldName<public> : type = default
      const fieldMatch = trimmed.match(/^(?:@editable\s+)?(\w+)(?:<[^>]*>)?\s*:\s*(\S+)\s*=\s*(.+)$/)
      if (fieldMatch) {
        current.fields.push({
          name: fieldMatch[1],
          type: fieldMatch[2],
          defaultValue: fieldMatch[3].replace(/\s*#.*$/, '').trim(),
          line: i + 1,
        })
      }
    }
  }
  if (current) structs.push(current)

  return structs
}

function CollapsibleSection({ title, count, color, children }: { title: string; count: number; color: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(true)
  return (
    <div className="border-b border-fn-border/30 last:border-b-0">
      <button onClick={() => setOpen(!open)} className="w-full flex items-center gap-2 px-2 py-1.5 text-left hover:bg-white/[0.03] transition-colors">
        <svg className={`w-3 h-3 text-gray-500 shrink-0 transition-transform ${open ? 'rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M9 5l7 7-7 7" />
        </svg>
        <span className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">{title}</span>
        {count > 0 && <span className={`text-[9px] font-medium px-1.5 py-0.5 rounded-full ${color}`}>{count}</span>}
      </button>
      {open && <div className="px-2 pb-2">{children}</div>}
    </div>
  )
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
  const [persistStructs, setPersistStructs] = useState<PersistStruct[]>([])
  const [showPersistence, setShowPersistence] = useState(false)

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

      // Scan all verse files for persistent data structures
      const allVerse: TreeNode[] = []
      function collectVerse(n: TreeNode) { if (!n.isDir) allVerse.push(n); n.children.forEach(collectVerse) }
      collectVerse(root)

      const structs: PersistStruct[] = []
      for (const vf of allVerse) {
        try {
          const result = await window.electronAPI.forgeReadVerse(vf.path) as any
          const source = result?.content ?? result?.source ?? ''
          if (source.includes('persistable') || source.includes(':= struct') || source.includes(':= class')) {
            structs.push(...parsePersistentStructs(source, vf.path))
          }
        } catch { /* skip */ }
      }
      setPersistStructs(structs)
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
      const result = await window.electronAPI.forgeReadVerse(node.path) as any
      // Normalize — sidecar returns 'source' not 'content'
      setFileContent({
        filePath: result?.filePath ?? node.path,
        name: result?.name ?? node.name,
        content: result?.content ?? result?.source ?? result?.Content ?? '',
        lineCount: result?.lineCount ?? result?.LineCount ?? 0,
      })
    } catch {
      setFileContent(null)
    } finally {
      setFileLoading(false)
    }
  }, [])

  // Parse analysis from current file
  const parsed = useMemo(() => {
    if (!fileContent?.content) return null
    return parseVerseSource(fileContent.content)
  }, [fileContent])

  const [scrollToLine, setScrollToLine] = useState<number | undefined>(undefined)

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
    <div className="flex-1 flex bg-fn-darker overflow-hidden min-h-0">
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

        {/* Persistence Data toggle */}
        {persistStructs.length > 0 && (
          <div className="border-t border-fn-border shrink-0">
            <button
              onClick={() => setShowPersistence(!showPersistence)}
              className="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-white/[0.03] transition-colors"
            >
              <svg className={`w-3 h-3 text-gray-500 shrink-0 transition-transform ${showPersistence ? 'rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path d="M9 5l7 7-7 7" />
              </svg>
              <svg className="w-3.5 h-3.5 text-emerald-400/70 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
              </svg>
              <span className="text-[10px] font-semibold text-gray-300">Persistent Data</span>
              <span className="text-[9px] text-emerald-400/70 bg-emerald-400/10 px-1.5 py-0.5 rounded-full ml-auto">{persistStructs.length}</span>
            </button>
            {showPersistence && (
              <div className="max-h-[300px] overflow-y-auto px-2 pb-2 space-y-2">
                {persistStructs.filter(s => s.kind === 'persistable').length > 0 && (
                  <div className="text-[8px] text-emerald-400/50 uppercase tracking-wider px-1 pt-1">Persistable</div>
                )}
                {persistStructs.filter(s => s.kind === 'persistable').map((s) => (
                  <button
                    key={`${s.sourceFile}:${s.name}`}
                    onClick={() => {
                      // Find and open the source file, scroll to line
                      const allFiles: TreeNode[] = []
                      function walk(n: TreeNode) { if (!n.isDir) allFiles.push(n); n.children.forEach(walk) }
                      if (tree) walk(tree)
                      const file = allFiles.find(f => f.path === s.sourceFile)
                      if (file) { handleSelectFile(file); setTimeout(() => setScrollToLine(s.startLine), 300) }
                    }}
                    className="w-full text-left bg-fn-darker rounded px-2 py-1.5 hover:bg-white/[0.03] transition-colors"
                  >
                    <div className="flex items-center gap-1.5">
                      <span className="text-[10px] font-semibold text-emerald-400">{s.name}</span>
                      <span className="text-[8px] text-gray-600">{s.fields.length} fields</span>
                    </div>
                    <div className="text-[8px] text-gray-600 truncate">{s.sourceFile.split(/[/\\]/).pop()}</div>
                  </button>
                ))}
                {persistStructs.filter(s => s.kind === 'struct').length > 0 && (
                  <div className="text-[8px] text-blue-400/50 uppercase tracking-wider px-1 pt-1">Structs</div>
                )}
                {persistStructs.filter(s => s.kind === 'struct').map((s) => (
                  <button
                    key={`${s.sourceFile}:${s.name}`}
                    onClick={() => {
                      const allFiles: TreeNode[] = []
                      function walk(n: TreeNode) { if (!n.isDir) allFiles.push(n); n.children.forEach(walk) }
                      if (tree) walk(tree)
                      const file = allFiles.find(f => f.path === s.sourceFile)
                      if (file) { handleSelectFile(file); setTimeout(() => setScrollToLine(s.startLine), 300) }
                    }}
                    className="w-full text-left bg-fn-darker rounded px-2 py-1.5 hover:bg-white/[0.03] transition-colors"
                  >
                    <div className="flex items-center gap-1.5">
                      <span className="text-[10px] font-semibold text-blue-400">{s.name}</span>
                      <span className="text-[8px] text-gray-600">{s.fields.length} fields</span>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Center: File Content */}
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
                source={fileContent.content || '// Empty file'}
                fontSize={verseEditorFontSize}
                scrollToLine={scrollToLine}
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

      {/* Right: Analysis Panel */}
      {fileContent && parsed && (
        <div className="w-[260px] border-l border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden min-h-0">
          <div className="px-2 py-1.5 border-b border-fn-border shrink-0">
            <span className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider">Analysis</span>
          </div>
          <div className="flex-1 overflow-y-auto min-h-0">
            <CollapsibleSection title="Classes" count={parsed.classes.length} color="text-blue-400 bg-blue-400/10">
              {parsed.classes.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No classes found</div>
              ) : parsed.classes.map((cls, i) => (
                <button key={i} onClick={() => setScrollToLine(cls.startLine)} className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors">
                  <span className="text-[10px] font-semibold text-blue-400 truncate">{cls.name}</span>
                  {cls.parent && <span className="text-[9px] text-gray-600">: {cls.parent}</span>}
                  <span className="ml-auto text-[8px] text-gray-700 tabular-nums shrink-0">L{cls.startLine}</span>
                </button>
              ))}
            </CollapsibleSection>

            <CollapsibleSection title="Functions" count={parsed.functions.length} color="text-cyan-400 bg-cyan-400/10">
              {parsed.functions.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No functions found</div>
              ) : parsed.functions.map((fn, i) => (
                <button key={i} onClick={() => setScrollToLine(fn.startLine)} className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors">
                  <span className="text-[10px] font-semibold text-cyan-400 truncate flex-1 min-w-0">{fn.name}</span>
                  <span className="text-[8px] text-gray-700 tabular-nums shrink-0">L{fn.startLine}</span>
                </button>
              ))}
            </CollapsibleSection>

            <CollapsibleSection title="Devices" count={parsed.devices.length} color="text-purple-400 bg-purple-400/10">
              {parsed.devices.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No @editable devices</div>
              ) : parsed.devices.map((dev, i) => (
                <button key={i} onClick={() => setScrollToLine(dev.line)} className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors">
                  <span className="text-[9px] text-purple-400/70">@</span>
                  <span className="text-[10px] font-semibold text-white truncate">{dev.name}</span>
                  <span className="text-[9px] text-gray-600 truncate">{dev.type}</span>
                  <span className="ml-auto text-[8px] text-gray-700 tabular-nums shrink-0">L{dev.line}</span>
                </button>
              ))}
            </CollapsibleSection>

            <CollapsibleSection title="Imports" count={parsed.imports.length} color="text-orange-400 bg-orange-400/10">
              {parsed.imports.length === 0 ? (
                <div className="py-2 text-center text-[10px] text-gray-600">No imports</div>
              ) : parsed.imports.map((imp, i) => (
                <button key={i} onClick={() => setScrollToLine(imp.line)} className="w-full flex items-center gap-1.5 px-2 py-1 text-left hover:bg-white/[0.03] transition-colors">
                  <span className="text-[10px] font-semibold text-orange-400 truncate font-mono flex-1 min-w-0">{imp.module}</span>
                </button>
              ))}
            </CollapsibleSection>

            {/* Persistent Data Tables for this file */}
            {(() => {
              const fileStructs = persistStructs.filter(s => s.sourceFile === selectedFile)
              if (fileStructs.length === 0) return null
              return (
                <CollapsibleSection title="Data Schemas" count={fileStructs.length} color="text-emerald-400 bg-emerald-400/10">
                  {fileStructs.map((s) => (
                    <div key={s.name} className="mb-3">
                      <button onClick={() => setScrollToLine(s.startLine)} className="flex items-center gap-1.5 mb-1 hover:bg-white/[0.03] rounded px-1 py-0.5 w-full text-left">
                        <span className={`text-[10px] font-semibold ${s.kind === 'persistable' ? 'text-emerald-400' : 'text-blue-400'}`}>{s.name}</span>
                        {s.kind === 'persistable' && <span className="text-[7px] px-1 py-0.5 rounded bg-emerald-400/10 text-emerald-400/70">PERSIST</span>}
                        {s.parent && <span className="text-[8px] text-gray-600">: {s.parent}</span>}
                      </button>
                      {s.fields.length > 0 && (
                        <table className="w-full text-[9px]">
                          <thead>
                            <tr className="text-gray-600">
                              <th className="text-left font-medium px-1 py-0.5">Field</th>
                              <th className="text-left font-medium px-1 py-0.5">Type</th>
                              <th className="text-right font-medium px-1 py-0.5">Default</th>
                            </tr>
                          </thead>
                          <tbody>
                            {s.fields.map((f) => (
                              <tr key={f.name} className="hover:bg-white/[0.02] cursor-pointer" onClick={() => setScrollToLine(f.line)}>
                                <td className="px-1 py-0.5 text-white font-mono">{f.name}</td>
                                <td className="px-1 py-0.5 text-cyan-400/70 font-mono">{f.type}</td>
                                <td className="px-1 py-0.5 text-gray-500 text-right font-mono">{f.defaultValue}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      )}
                    </div>
                  ))}
                </CollapsibleSection>
              )
            })()}
          </div>
        </div>
      )}
    </div>
  )
}
