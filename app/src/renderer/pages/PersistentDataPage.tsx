import { useEffect, useState, useMemo } from 'react'
import { ResizeHandle } from '../components/ResizeHandle'
import { VerseHighlighter } from '../components/VerseHighlighter'
import { useSettingsStore } from '../stores/settingsStore'

interface PersistField {
  name: string
  type: string
  defaultValue: string
  comment: string
  line: number
}

interface PersistSchema {
  name: string
  kind: 'persistable' | 'struct' | 'class'
  parent?: string
  modifiers: string[]
  fields: PersistField[]
  sourceFile: string
  relativePath: string
  startLine: number
}

function parseAllPersistentStructs(content: string, filePath: string): PersistSchema[] {
  if (!content) return []
  const lines = content.split('\n')
  const schemas: PersistSchema[] = []
  let current: PersistSchema | null = null
  let baseIndent = -1

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    const trimmed = line.trim()
    if (!trimmed || trimmed.startsWith('#') || trimmed.startsWith('//')) {
      // Capture comment for the next field
      continue
    }

    const structMatch = trimmed.match(/^(\w+)(?:<([^>]*)>)?\s*:=\s*(class|struct)(?:<([^>]+)>)?(?:\((\w+)\))?\s*:?\s*$/)
    if (structMatch) {
      if (current && current.fields.length > 0) schemas.push(current)
      const [, name, accessMods, kind, classMods, parent] = structMatch
      const allMods = [accessMods, classMods].filter(Boolean).join(',').split(',').map(s => s.trim()).filter(Boolean)
      const isPersistable = allMods.includes('persistable')
      current = {
        name,
        kind: isPersistable ? 'persistable' : kind as 'struct' | 'class',
        parent,
        modifiers: allMods,
        fields: [],
        sourceFile: filePath,
        relativePath: filePath.split(/[/\\]/).slice(-2).join('/'),
        startLine: i + 1,
      }
      baseIndent = line.search(/\S/)
      continue
    }

    if (current) {
      const indent = line.search(/\S/)
      if (indent >= 0 && indent <= baseIndent && !trimmed.startsWith('#')) {
        if (current.fields.length > 0) schemas.push(current)
        current = null
        i--
        continue
      }

      // Field: FieldName<public> : type = default  # comment
      const fieldMatch = trimmed.match(/^(?:@editable\s+)?(\w+)(?:<[^>]*>)?\s*:\s*(\S+)\s*=\s*([^#]+?)(?:\s*#\s*(.*))?$/)
      if (fieldMatch) {
        current.fields.push({
          name: fieldMatch[1],
          type: fieldMatch[2].replace(/,$/, ''),
          defaultValue: fieldMatch[3].trim(),
          comment: fieldMatch[4]?.trim() || '',
          line: i + 1,
        })
      }
    }
  }
  if (current && current.fields.length > 0) schemas.push(current)
  return schemas
}

// Type icons
function typeIcon(type: string): string {
  if (type === 'int' || type === 'float') return '#'
  if (type === 'string') return 'T'
  if (type === 'logic') return '?'
  if (type.startsWith('[]') || type.startsWith('array')) return '[]'
  if (type.startsWith('?')) return '?'
  return '{}'
}

function typeColor(type: string): string {
  if (type === 'int' || type === 'float') return 'text-cyan-400'
  if (type === 'string') return 'text-yellow-400'
  if (type === 'logic') return 'text-pink-400'
  if (type.startsWith('[]') || type.startsWith('array')) return 'text-purple-400'
  if (type.startsWith('?')) return 'text-orange-400'
  return 'text-gray-400'
}

export function PersistentDataPage() {
  const [schemas, setSchemas] = useState<PersistSchema[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedSchema, setSelectedSchema] = useState<PersistSchema | null>(null)
  const [sourceContent, setSourceContent] = useState<string | null>(null)
  const [sourceLoading, setSourceLoading] = useState(false)
  const [detailWidth, setDetailWidth] = useState(500)
  const fontSize = useSettingsStore((s) => s.verseEditorFontSize)

  useEffect(() => {
    scanForSchemas()
  }, [])

  async function scanForSchemas() {
    setLoading(true)
    try {
      const allSchemas: PersistSchema[] = []

      // Recursively find all verse files
      async function scanDir(path?: string) {
        const result = await window.electronAPI.forgeBrowseContent(path)
        for (const entry of result?.entries ?? []) {
          const isDir = entry.isDirectory || (entry as any).type === 'folder'
          if (isDir) {
            if (entry.name?.startsWith('__')) continue
            await scanDir(entry.path || entry.relativePath)
          } else if (entry.name?.endsWith('.verse') || (entry as any).type === 'verse') {
            try {
              const result2 = await window.electronAPI.forgeReadVerse(entry.path || entry.relativePath) as any
              const source = result2?.content ?? result2?.source ?? ''
              if (source.includes('persistable') || source.includes(':= struct') || source.includes(':= class')) {
                allSchemas.push(...parseAllPersistentStructs(source, entry.path || entry.relativePath))
              }
            } catch { /* skip */ }
          }
        }
      }

      await scanDir()
      setSchemas(allSchemas)
    } catch { /* */ }
    setLoading(false)
  }

  const persistable = useMemo(() => schemas.filter(s => s.kind === 'persistable'), [schemas])
  const structs = useMemo(() => schemas.filter(s => s.kind === 'struct'), [schemas])
  const classes = useMemo(() => schemas.filter(s => s.kind === 'class' && s.kind !== 'persistable'), [schemas])

  async function handleSelectSchema(schema: PersistSchema) {
    setSelectedSchema(schema)
    setSourceLoading(true)
    try {
      const result = await window.electronAPI.forgeReadVerse(schema.sourceFile) as any
      setSourceContent(result?.content ?? result?.source ?? '')
    } catch {
      setSourceContent(null)
    }
    setSourceLoading(false)
  }

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-6 h-6 border-2 border-emerald-400/30 border-t-emerald-400 rounded-full animate-spin mx-auto mb-2" />
          <div className="text-[11px] text-gray-400">Scanning verse files for data schemas...</div>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* Left: Schema List */}
      <div className="w-[300px] flex flex-col border-r border-fn-border bg-fn-dark shrink-0 min-h-0">
        <div className="px-3 py-2 border-b border-fn-border shrink-0">
          <div className="flex items-center gap-2 mb-1">
            <svg className="w-4 h-4 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4M4 12c0 2.21 3.582 4 8 4s8-1.79 8-4" />
            </svg>
            <span className="text-[12px] font-semibold text-white">Persistent Data</span>
          </div>
          <div className="text-[10px] text-gray-500">
            {persistable.length} persistable, {structs.length} structs, {schemas.reduce((a, s) => a + s.fields.length, 0)} total fields
          </div>
        </div>

        <div className="flex-1 overflow-y-auto min-h-0">
          {/* Persistable classes — the "databases" */}
          {persistable.length > 0 && (
            <div>
              <div className="px-3 py-1.5 text-[9px] font-semibold text-emerald-400/60 uppercase tracking-wider sticky top-0 bg-fn-dark z-10 border-b border-fn-border/30">
                Persistable Classes
              </div>
              {persistable.map((s) => (
                <button
                  key={`${s.sourceFile}:${s.name}`}
                  onClick={() => handleSelectSchema(s)}
                  className={`w-full text-left px-3 py-2 border-b border-fn-border/20 transition-colors ${
                    selectedSchema === s ? 'bg-emerald-500/10 border-l-2 border-l-emerald-400' : 'hover:bg-white/[0.03]'
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <svg className="w-3.5 h-3.5 text-emerald-400 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                      <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
                    </svg>
                    <span className="text-[11px] font-semibold text-white">{s.name}</span>
                    <span className="text-[9px] text-gray-600 ml-auto">{s.fields.length} fields</span>
                  </div>
                  <div className="text-[9px] text-gray-600 mt-0.5 pl-5.5">{s.relativePath}</div>
                </button>
              ))}
            </div>
          )}

          {/* Structs */}
          {structs.length > 0 && (
            <div>
              <div className="px-3 py-1.5 text-[9px] font-semibold text-blue-400/60 uppercase tracking-wider sticky top-0 bg-fn-dark z-10 border-b border-fn-border/30">
                Data Structs
              </div>
              {structs.map((s) => (
                <button
                  key={`${s.sourceFile}:${s.name}`}
                  onClick={() => handleSelectSchema(s)}
                  className={`w-full text-left px-3 py-2 border-b border-fn-border/20 transition-colors ${
                    selectedSchema === s ? 'bg-blue-500/10 border-l-2 border-l-blue-400' : 'hover:bg-white/[0.03]'
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="text-[10px] text-blue-400/50 font-mono shrink-0">{'{}'}</span>
                    <span className="text-[11px] font-medium text-gray-300">{s.name}</span>
                    <span className="text-[9px] text-gray-600 ml-auto">{s.fields.length}</span>
                  </div>
                </button>
              ))}
            </div>
          )}

          {schemas.length === 0 && (
            <div className="p-6 text-center">
              <div className="text-[11px] text-gray-600">No persistent data structures found</div>
              <div className="text-[9px] text-gray-700 mt-1">Add <code className="text-emerald-400/60">class&lt;persistable&gt;</code> or <code className="text-blue-400/60">struct</code> definitions to your verse files</div>
            </div>
          )}
        </div>
      </div>

      {/* Right: Schema Detail */}
      {selectedSchema ? (
        <>
          <div className="flex-1 flex flex-col min-h-0 overflow-hidden">
            {/* Schema header */}
            <div className="px-4 py-3 border-b border-fn-border bg-fn-dark shrink-0">
              <div className="flex items-center gap-2">
                <span className={`text-[14px] font-bold ${selectedSchema.kind === 'persistable' ? 'text-emerald-400' : 'text-blue-400'}`}>
                  {selectedSchema.name}
                </span>
                {selectedSchema.kind === 'persistable' && (
                  <span className="text-[8px] px-1.5 py-0.5 rounded bg-emerald-400/10 text-emerald-400 border border-emerald-400/20 font-semibold uppercase">Persistable</span>
                )}
                {selectedSchema.parent && (
                  <span className="text-[10px] text-gray-500">extends <span className="text-gray-400">{selectedSchema.parent}</span></span>
                )}
              </div>
              <div className="text-[9px] text-gray-600 mt-1 font-mono">{selectedSchema.sourceFile}</div>
            </div>

            {/* Field table */}
            <div className="flex-1 overflow-auto min-h-0">
              <table className="w-full">
                <thead className="sticky top-0 bg-fn-dark z-10">
                  <tr className="border-b border-fn-border">
                    <th className="text-left text-[10px] font-semibold text-gray-500 uppercase tracking-wider px-4 py-2">Field</th>
                    <th className="text-left text-[10px] font-semibold text-gray-500 uppercase tracking-wider px-4 py-2">Type</th>
                    <th className="text-left text-[10px] font-semibold text-gray-500 uppercase tracking-wider px-4 py-2">Default</th>
                    <th className="text-left text-[10px] font-semibold text-gray-500 uppercase tracking-wider px-4 py-2">Description</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedSchema.fields.map((f, i) => (
                    <tr key={f.name} className={`border-b border-fn-border/20 hover:bg-white/[0.02] ${i % 2 === 0 ? '' : 'bg-white/[0.01]'}`}>
                      <td className="px-4 py-2">
                        <span className="text-[11px] text-white font-mono font-medium">{f.name}</span>
                      </td>
                      <td className="px-4 py-2">
                        <div className="flex items-center gap-1.5">
                          <span className={`text-[9px] font-mono font-bold ${typeColor(f.type)}`}>{typeIcon(f.type)}</span>
                          <span className={`text-[11px] font-mono ${typeColor(f.type)}`}>{f.type}</span>
                        </div>
                      </td>
                      <td className="px-4 py-2">
                        <code className="text-[10px] text-gray-400 font-mono bg-fn-darker px-1.5 py-0.5 rounded">{f.defaultValue}</code>
                      </td>
                      <td className="px-4 py-2">
                        <span className="text-[10px] text-gray-600 italic">{f.comment || '—'}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              {selectedSchema.fields.length === 0 && (
                <div className="p-6 text-center text-[11px] text-gray-600">No fields defined</div>
              )}
            </div>
          </div>

          {/* Source preview */}
          {sourceContent && (
            <>
              <ResizeHandle direction="horizontal" onResize={(d) => setDetailWidth(w => Math.max(300, Math.min(800, w - d)))} />
              <div className="border-l border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden" style={{ width: detailWidth }}>
                <div className="px-3 py-2 border-b border-fn-border shrink-0">
                  <span className="text-[10px] font-semibold text-gray-400">Source</span>
                </div>
                <div className="flex-1 overflow-auto min-h-0">
                  {sourceLoading ? (
                    <div className="flex items-center justify-center h-32">
                      <div className="w-4 h-4 border-2 border-gray-600 border-t-gray-300 rounded-full animate-spin" />
                    </div>
                  ) : (
                    <VerseHighlighter
                      source={sourceContent}
                      fontSize={fontSize}
                      scrollToLine={selectedSchema.startLine}
                    />
                  )}
                </div>
              </div>
            </>
          )}
        </>
      ) : (
        <div className="flex-1 flex items-center justify-center">
          <div className="text-center">
            <svg className="w-12 h-12 mx-auto mb-3 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
              <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4M4 12c0 2.21 3.582 4 8 4s8-1.79 8-4" />
            </svg>
            <div className="text-[12px] text-gray-500 font-medium">Persistent Data Schemas</div>
            <div className="text-[10px] text-gray-700 mt-1 max-w-xs">
              View your project's persistable classes and data structures as formatted tables. Select a schema to inspect its fields.
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
