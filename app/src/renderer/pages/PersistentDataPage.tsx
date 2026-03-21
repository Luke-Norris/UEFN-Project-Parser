import { useEffect, useState, useMemo } from 'react'

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
    if (!trimmed || trimmed.startsWith('#') || trimmed.startsWith('//')) continue

    const structMatch = trimmed.match(/^(\w+)(?:<([^>]*)>)?\s*:=\s*(class|struct)(?:<([^>]+)>)?(?:\((\w+)\))?\s*:?\s*$/)
    if (structMatch) {
      if (current && current.fields.length > 0) schemas.push(current)
      const [, name, accessMods, kind, classMods, parent] = structMatch
      const allMods = [accessMods, classMods].filter(Boolean).join(',').split(',').map(s => s.trim()).filter(Boolean)
      current = {
        name,
        kind: allMods.includes('persistable') ? 'persistable' : kind as 'struct' | 'class',
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

function typeColor(type: string): string {
  if (type === 'int' || type === 'float') return 'text-cyan-400'
  if (type === 'string') return 'text-yellow-400'
  if (type === 'logic') return 'text-pink-400'
  if (type.startsWith('[]') || type.startsWith('array')) return 'text-purple-400'
  if (type.startsWith('?')) return 'text-orange-400'
  return 'text-gray-400'
}

function typeBadgeColor(type: string): string {
  if (type === 'int' || type === 'float') return 'bg-cyan-400/10 text-cyan-400 border-cyan-400/20'
  if (type === 'string') return 'bg-yellow-400/10 text-yellow-400 border-yellow-400/20'
  if (type === 'logic') return 'bg-pink-400/10 text-pink-400 border-pink-400/20'
  if (type.startsWith('[]') || type.startsWith('array')) return 'bg-purple-400/10 text-purple-400 border-purple-400/20'
  return 'bg-gray-400/10 text-gray-400 border-gray-400/20'
}

interface Props {
  onNavigate?: (page: string) => void
}

export function PersistentDataPage({ onNavigate }: Props = {}) {
  const [schemas, setSchemas] = useState<PersistSchema[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedSchema, setSelectedSchema] = useState<PersistSchema | null>(null)
  const [search, setSearch] = useState('')
  const [sortBy, setSortBy] = useState<'name' | 'fields' | 'file'>('name')
  const [sortReversed, setSortReversed] = useState(false)

  useEffect(() => { scanForSchemas() }, [])

  async function scanForSchemas() {
    setLoading(true)
    try {
      const allSchemas: PersistSchema[] = []
      async function scanDir(path?: string) {
        const result = await window.electronAPI.forgeBrowseContent(path)
        for (const entry of result?.entries ?? []) {
          const isDir = entry.isDirectory || (entry as any).type === 'folder'
          if (isDir) {
            if (entry.name?.startsWith('__')) continue
            await scanDir(entry.path || entry.relativePath)
          } else if (entry.name?.endsWith('.verse') || (entry as any).type === 'verse') {
            try {
              const r = await window.electronAPI.forgeReadVerse(entry.path || entry.relativePath) as any
              const source = r?.content ?? r?.source ?? ''
              if (source.includes('persistable') || source.includes(':= struct') || source.includes(':= class')) {
                allSchemas.push(...parseAllPersistentStructs(source, entry.path || entry.relativePath))
              }
            } catch { }
          }
        }
      }
      await scanDir()
      setSchemas(allSchemas)
    } catch { }
    setLoading(false)
  }

  const filtered = useMemo(() => {
    let list = schemas
    if (search.trim()) {
      const q = search.toLowerCase()
      list = list.filter(s =>
        s.name.toLowerCase().includes(q) ||
        s.fields.some(f => f.name.toLowerCase().includes(q) || f.type.toLowerCase().includes(q)) ||
        s.relativePath.toLowerCase().includes(q)
      )
    }
    list = [...list].sort((a, b) => {
      if (sortBy === 'name') return a.name.localeCompare(b.name)
      if (sortBy === 'fields') return b.fields.length - a.fields.length
      if (sortBy === 'file') return a.relativePath.localeCompare(b.relativePath)
      return 0
    })
    if (sortReversed) list.reverse()
    return list
  }, [schemas, search, sortBy, sortReversed])

  const persistable = useMemo(() => filtered.filter(s => s.kind === 'persistable'), [filtered])
  const structs = useMemo(() => filtered.filter(s => s.kind !== 'persistable'), [filtered])

  function handleSort(field: 'name' | 'fields' | 'file') {
    if (sortBy === field) setSortReversed(!sortReversed)
    else { setSortBy(field); setSortReversed(false) }
  }

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-fn-darker">
        <div className="text-center">
          <div className="w-6 h-6 border-2 border-emerald-400/30 border-t-emerald-400 rounded-full animate-spin mx-auto mb-2" />
          <div className="text-[11px] text-gray-400">Scanning for data schemas...</div>
        </div>
      </div>
    )
  }

  return (
    <div className="flex-1 flex flex-col bg-fn-darker overflow-hidden min-h-0">
      {/* Header */}
      <div className="px-6 py-4 border-b border-fn-border bg-fn-dark shrink-0">
        <div className="flex items-center gap-3">
          <svg className="w-5 h-5 text-emerald-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4M4 12c0 2.21 3.582 4 8 4s8-1.79 8-4" />
          </svg>
          <div>
            <h1 className="text-[14px] font-bold text-white">Persistent Data</h1>
            <p className="text-[10px] text-gray-500">{persistable.length} persistable classes, {structs.length} structs, {schemas.reduce((a, s) => a + s.fields.length, 0)} total fields</p>
          </div>
          <div className="ml-auto flex items-center gap-2">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search schemas, fields..."
              className="w-[220px] bg-fn-darker border border-fn-border rounded px-2.5 py-1.5 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-emerald-500/50"
            />
          </div>
        </div>
      </div>

      {/* Sort bar */}
      <div className="flex items-center gap-1 px-6 py-2 border-b border-fn-border/50 bg-fn-darker shrink-0">
        {(['name', 'fields', 'file'] as const).map((field) => (
          <button
            key={field}
            onClick={() => handleSort(field)}
            className={`px-2 py-1 text-[9px] rounded transition-colors ${
              sortBy === field ? 'text-white bg-white/10' : 'text-gray-600 hover:text-gray-400'
            }`}
          >
            {field === 'name' ? 'Name' : field === 'fields' ? 'Fields' : 'File'}
            {sortBy === field && (
              <span className="ml-1">{sortReversed ? '↑' : '↓'}</span>
            )}
          </button>
        ))}
      </div>

      {/* Schema list + detail */}
      <div className="flex-1 overflow-y-auto min-h-0 px-6 py-4 space-y-6">
        {/* Persistable section */}
        {persistable.length > 0 && (
          <div>
            <div className="flex items-center gap-2 mb-3">
              <div className="w-2 h-2 rounded-full bg-emerald-400" />
              <span className="text-[11px] font-semibold text-emerald-400 uppercase tracking-wider">Persistable Classes</span>
              <div className="flex-1 h-px bg-fn-border/50" />
            </div>
            <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-3">
              {persistable.map((s) => (
                <SchemaCard key={`${s.sourceFile}:${s.name}`} schema={s} selected={selectedSchema === s} onSelect={() => setSelectedSchema(selectedSchema === s ? null : s)} onViewCode={onNavigate} />
              ))}
            </div>
          </div>
        )}

        {/* Structs section */}
        {structs.length > 0 && (
          <div>
            <div className="flex items-center gap-2 mb-3">
              <div className="w-2 h-2 rounded-full bg-blue-400" />
              <span className="text-[11px] font-semibold text-blue-400 uppercase tracking-wider">Data Structs</span>
              <div className="flex-1 h-px bg-fn-border/50" />
            </div>
            <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-3">
              {structs.map((s) => (
                <SchemaCard key={`${s.sourceFile}:${s.name}`} schema={s} selected={selectedSchema === s} onSelect={() => setSelectedSchema(selectedSchema === s ? null : s)} onViewCode={onNavigate} />
              ))}
            </div>
          </div>
        )}

        {filtered.length === 0 && (
          <div className="flex items-center justify-center py-16">
            <div className="text-center">
              <div className="text-[11px] text-gray-600">{search ? 'No matching schemas' : 'No data schemas found'}</div>
            </div>
          </div>
        )}
      </div>

      {/* Schema detail modal */}
      {selectedSchema && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={() => setSelectedSchema(null)}>
          <div className="bg-fn-dark border border-fn-border rounded-xl shadow-2xl max-w-[700px] w-full max-h-[80vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
            {/* Modal header */}
            <div className="flex items-center gap-3 px-5 py-4 border-b border-fn-border">
              <svg className={`w-5 h-5 ${selectedSchema.kind === 'persistable' ? 'text-emerald-400' : 'text-blue-400'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
              </svg>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-[14px] font-bold text-white">{selectedSchema.name}</span>
                  {selectedSchema.kind === 'persistable' && (
                    <span className="text-[8px] px-1.5 py-0.5 rounded bg-emerald-400/10 text-emerald-400 border border-emerald-400/20 font-semibold uppercase">Persistable</span>
                  )}
                  {selectedSchema.parent && (
                    <span className="text-[10px] text-gray-500">: {selectedSchema.parent}</span>
                  )}
                </div>
                <div className="text-[9px] text-gray-600 font-mono mt-0.5">{selectedSchema.relativePath}</div>
              </div>
              <button
                onClick={() => {
                  setSelectedSchema(null)
                  if (onNavigate) onNavigate('project-verse-files')
                }}
                className="px-3 py-1.5 text-[10px] font-medium text-emerald-400 bg-emerald-400/10 border border-emerald-400/20 rounded hover:bg-emerald-400/20 transition-colors"
              >
                View Code
              </button>
              <button onClick={() => setSelectedSchema(null)} className="p-1 text-gray-500 hover:text-white transition-colors">
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path d="M6 18L18 6M6 6l12 12" /></svg>
              </button>
            </div>

            {/* Field table */}
            <div className="flex-1 overflow-y-auto min-h-0">
              <table className="w-full">
                <thead className="sticky top-0 bg-fn-panel z-10">
                  <tr>
                    <th className="text-left text-[9px] font-semibold text-gray-500 uppercase tracking-wider px-5 py-2.5">#</th>
                    <th className="text-left text-[9px] font-semibold text-gray-500 uppercase tracking-wider px-3 py-2.5">Field</th>
                    <th className="text-left text-[9px] font-semibold text-gray-500 uppercase tracking-wider px-3 py-2.5">Type</th>
                    <th className="text-left text-[9px] font-semibold text-gray-500 uppercase tracking-wider px-3 py-2.5">Default</th>
                    <th className="text-left text-[9px] font-semibold text-gray-500 uppercase tracking-wider px-3 py-2.5">Description</th>
                  </tr>
                </thead>
                <tbody>
                  {selectedSchema.fields.map((f, i) => (
                    <tr key={f.name} className={`border-t border-fn-border/20 ${i % 2 === 0 ? 'bg-fn-darker/30' : ''} hover:bg-white/[0.03]`}>
                      <td className="px-5 py-2 text-[9px] text-gray-700 tabular-nums">{i + 1}</td>
                      <td className="px-3 py-2">
                        <span className="text-[11px] text-white font-medium">{f.name}</span>
                      </td>
                      <td className="px-3 py-2">
                        <span className={`text-[10px] px-1.5 py-0.5 rounded border font-mono ${typeBadgeColor(f.type)}`}>{f.type}</span>
                      </td>
                      <td className="px-3 py-2">
                        <code className="text-[10px] text-gray-400 font-mono">{f.defaultValue}</code>
                      </td>
                      <td className="px-3 py-2">
                        <span className="text-[10px] text-gray-600">{f.comment || ''}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Footer */}
            <div className="px-5 py-2.5 border-t border-fn-border text-[9px] text-gray-600 flex items-center gap-3 shrink-0">
              <span>{selectedSchema.fields.length} fields</span>
              <span>{selectedSchema.modifiers.join(', ')}</span>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// Schema card component
function SchemaCard({ schema, selected, onSelect, onViewCode }: { schema: PersistSchema; selected: boolean; onSelect: () => void; onViewCode?: (page: string) => void }) {
  const isPersist = schema.kind === 'persistable'
  const accentColor = isPersist ? 'emerald' : 'blue'

  // Count field types
  const typeCounts = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const f of schema.fields) {
      const base = f.type === 'int' || f.type === 'float' ? 'number' : f.type === 'string' ? 'string' : f.type === 'logic' ? 'bool' : f.type.startsWith('[]') ? 'array' : 'other'
      counts[base] = (counts[base] || 0) + 1
    }
    return counts
  }, [schema])

  return (
    <button
      onClick={onSelect}
      className={`w-full text-left rounded-lg border transition-all ${
        selected
          ? `border-${accentColor}-400/50 bg-${accentColor}-400/5 ring-1 ring-${accentColor}-400/20`
          : 'border-fn-border bg-fn-dark hover:bg-white/[0.02] hover:border-fn-border/80'
      }`}
    >
      {/* Card header */}
      <div className="px-4 py-3 border-b border-fn-border/30">
        <div className="flex items-center gap-2">
          <svg className={`w-4 h-4 text-${accentColor}-400 shrink-0`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path d="M4 7v10c0 2.21 3.582 4 8 4s8-1.79 8-4V7M4 7c0 2.21 3.582 4 8 4s8-1.79 8-4M4 7c0-2.21 3.582-4 8-4s8 1.79 8 4" />
          </svg>
          <span className="text-[12px] font-semibold text-white truncate">{schema.name}</span>
          {isPersist && <span className="text-[7px] px-1 py-0.5 rounded bg-emerald-400/10 text-emerald-400 border border-emerald-400/20 font-bold uppercase shrink-0">DB</span>}
        </div>
        {schema.parent && <div className="text-[9px] text-gray-600 mt-0.5 pl-6">extends {schema.parent}</div>}
      </div>

      {/* Field preview */}
      <div className="px-4 py-2 space-y-0.5">
        {schema.fields.slice(0, 4).map((f) => (
          <div key={f.name} className="flex items-center gap-2 text-[10px]">
            <span className="text-gray-400 truncate flex-1">{f.name}</span>
            <span className={`font-mono text-[9px] ${typeColor(f.type)}`}>{f.type}</span>
          </div>
        ))}
        {schema.fields.length > 4 && (
          <div className="text-[9px] text-gray-600">+{schema.fields.length - 4} more fields</div>
        )}
      </div>

      {/* Card footer */}
      <div className="px-4 py-2 border-t border-fn-border/30 flex items-center gap-2">
        <span className="text-[9px] text-gray-600">{schema.fields.length} fields</span>
        <div className="flex-1" />
        {Object.entries(typeCounts).map(([type, count]) => (
          <span key={type} className="text-[8px] text-gray-600">{count} {type}</span>
        ))}
      </div>
    </button>
  )
}
