import { useCallback, useEffect, useState } from 'react'
import { useBridgeStore } from '../stores/bridgeStore'
import { forgeStampList, forgeStampSave, forgeStampPlace, forgeBridgeCommand } from '../lib/api'

interface Stamp {
  name: string
  actorCount: number
  createdAt: string
  description?: string
  tags?: string[]
}

export function StampsPage() {
  const bridgeConnected = useBridgeStore((s) => s.connected)
  const [stamps, setStamps] = useState<Stamp[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Save stamp form
  const [showSaveForm, setShowSaveForm] = useState(false)
  const [saveName, setSaveName] = useState('')
  const [saving, setSaving] = useState(false)

  // Place stamp form
  const [placingStamp, setPlacingStamp] = useState<string | null>(null)
  const [placeX, setPlaceX] = useState('0')
  const [placeY, setPlaceY] = useState('0')
  const [placeZ, setPlaceZ] = useState('0')
  const [placing, setPlacing] = useState(false)

  // Import/Export
  const [importExportStatus, setImportExportStatus] = useState<string | null>(null)

  const loadStamps = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const result = await forgeStampList()
      setStamps(result.stamps ?? [])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load stamps')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadStamps()
  }, [loadStamps])

  const handleSaveStamp = useCallback(async () => {
    if (!saveName.trim() || !bridgeConnected) return
    setSaving(true)
    try {
      await forgeStampSave(saveName.trim())
      setSaveName('')
      setShowSaveForm(false)
      await loadStamps()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save stamp')
    } finally {
      setSaving(false)
    }
  }, [saveName, bridgeConnected, loadStamps])

  const handlePlaceStamp = useCallback(async () => {
    if (!placingStamp || !bridgeConnected) return
    setPlacing(true)
    try {
      await forgeStampPlace(placingStamp, parseFloat(placeX), parseFloat(placeY), parseFloat(placeZ))
      setPlacingStamp(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to place stamp')
    } finally {
      setPlacing(false)
    }
  }, [placingStamp, bridgeConnected, placeX, placeY, placeZ])

  const handleExportStamp = useCallback(async (stampName: string) => {
    try {
      const { save } = await import('@tauri-apps/plugin-dialog')
      const filePath = await save({
        defaultPath: `${stampName.replace(/\s+/g, '_')}.stamp.json`,
        filters: [{ name: 'Stamp JSON', extensions: ['json'] }],
      })
      if (!filePath) return

      const result = await forgeBridgeCommand('export_stamp', { name: stampName })
      const json = JSON.stringify(result, null, 2)
      const { invoke } = await import('@tauri-apps/api/core')
      const encoder = new TextEncoder()
      const bytes = encoder.encode(json)
      const binary = Array.from(bytes).map((b) => String.fromCharCode(b)).join('')
      const b64 = btoa(binary)
      await invoke('export_png', {
        dataUrl: `data:application/json;base64,${b64}`,
        filePath,
      })
      setImportExportStatus(`Exported ${stampName} to ${filePath}`)
      setTimeout(() => setImportExportStatus(null), 3000)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Export failed')
    }
  }, [])

  const handleImportStamp = useCallback(async () => {
    try {
      const { open } = await import('@tauri-apps/plugin-dialog')
      const filePath = await open({
        filters: [{ name: 'Stamp JSON', extensions: ['json'] }],
      })
      if (!filePath) return

      const { invoke } = await import('@tauri-apps/api/core')
      const result = await invoke<{ content?: string; error?: string }>('read_text_file', { filePath })
      if (result.error || !result.content) {
        setError(result.error ?? 'Failed to read file')
        return
      }

      const stampData = JSON.parse(result.content)
      await forgeBridgeCommand('import_stamp', { stamp: stampData })
      setImportExportStatus('Stamp imported successfully')
      setTimeout(() => setImportExportStatus(null), 3000)
      await loadStamps()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Import failed')
    }
  }, [loadStamps])

  return (
    <div className="flex-1 flex flex-col bg-fn-darker min-h-0 overflow-y-auto">
      {/* Header */}
      <div className="border-b border-fn-border bg-fn-dark/50 px-6 py-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-[14px] font-semibold text-white">Stamps</h1>
            <p className="text-[10px] text-gray-500 mt-0.5">
              Save and reuse actor groups across your project
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={handleImportStamp}
              className="text-[10px] px-3 py-1.5 rounded-lg border border-fn-border text-gray-400 hover:text-white hover:border-gray-500 transition-colors"
            >
              Import
            </button>
            <button
              onClick={() => setShowSaveForm(true)}
              disabled={!bridgeConnected}
              className="text-[10px] px-3 py-1.5 rounded-lg border border-fn-rare/30 bg-fn-rare/10 text-fn-rare hover:bg-fn-rare/20 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              title={bridgeConnected ? 'Save selected actors as stamp' : 'Connect to UEFN bridge first'}
            >
              Save Stamp
            </button>
          </div>
        </div>

        {!bridgeConnected && (
          <div className="mt-3 flex items-center gap-2 text-[10px] text-amber-400/70 bg-amber-400/5 border border-amber-400/10 rounded-lg px-3 py-2">
            <svg className="w-3.5 h-3.5 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
            </svg>
            <span>Connect to UEFN bridge to save and place stamps. Import/export works offline.</span>
          </div>
        )}
      </div>

      {/* Status messages */}
      {importExportStatus && (
        <div className="mx-6 mt-3 text-[10px] text-emerald-400 bg-emerald-400/5 border border-emerald-400/10 rounded-lg px-3 py-2">
          {importExportStatus}
        </div>
      )}
      {error && (
        <div className="mx-6 mt-3 text-[10px] text-red-400 bg-red-400/5 border border-red-400/10 rounded-lg px-3 py-2 flex items-center justify-between">
          <span>{error}</span>
          <button onClick={() => setError(null)} className="text-red-400/60 hover:text-red-400 ml-2">Dismiss</button>
        </div>
      )}

      {/* Save Stamp Form */}
      {showSaveForm && (
        <div className="mx-6 mt-4 bg-fn-panel border border-fn-border rounded-lg p-4">
          <h3 className="text-[11px] font-semibold text-white mb-3">Save Current Selection as Stamp</h3>
          <div className="flex gap-2">
            <input
              type="text"
              value={saveName}
              onChange={(e) => setSaveName(e.target.value)}
              placeholder="Stamp name..."
              className="flex-1 bg-fn-darker border border-fn-border rounded-lg px-3 py-2 text-[11px] text-white placeholder-gray-600 focus:border-fn-rare/40 focus:outline-none"
              autoFocus
            />
            <button
              onClick={handleSaveStamp}
              disabled={!saveName.trim() || saving}
              className="text-[10px] px-4 py-2 rounded-lg bg-fn-rare/20 text-fn-rare hover:bg-fn-rare/30 disabled:opacity-40 transition-colors"
            >
              {saving ? 'Saving...' : 'Save'}
            </button>
            <button
              onClick={() => setShowSaveForm(false)}
              className="text-[10px] px-3 py-2 rounded-lg border border-fn-border text-gray-400 hover:text-white transition-colors"
            >
              Cancel
            </button>
          </div>
          <p className="text-[9px] text-gray-600 mt-2">
            Select actors in UEFN first, then save them here as a reusable stamp.
          </p>
        </div>
      )}

      {/* Stamps List */}
      <div className="flex-1 p-6">
        {loading ? (
          <div className="flex items-center justify-center h-48">
            <div className="text-[11px] text-gray-500">Loading stamps...</div>
          </div>
        ) : stamps.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-48 text-center">
            <div className="w-12 h-12 rounded-xl bg-fn-panel border border-fn-border flex items-center justify-center mb-3">
              <svg className="w-6 h-6 text-gray-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path d="M3.75 13.5l10.5-11.25L12 10.5h8.25L9.75 21.75 12 13.5H3.75z" />
              </svg>
            </div>
            <p className="text-[11px] text-gray-400">No stamps saved yet</p>
            <p className="text-[9px] text-gray-600 mt-1">
              {bridgeConnected
                ? 'Select actors in UEFN and click "Save Stamp" to get started.'
                : 'Connect to the UEFN bridge to save and place stamps.'}
            </p>
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-3">
            {stamps.map((stamp) => (
              <div
                key={stamp.name}
                className="bg-fn-panel border border-fn-border rounded-lg p-4 hover:bg-white/[0.02] transition-colors"
              >
                <div className="flex items-start justify-between mb-2">
                  <div>
                    <h3 className="text-[12px] font-semibold text-white">{stamp.name}</h3>
                    <p className="text-[9px] text-gray-500 mt-0.5">
                      {stamp.actorCount} actors
                      {stamp.createdAt && ` - ${new Date(stamp.createdAt).toLocaleDateString()}`}
                    </p>
                  </div>
                  <div className="flex gap-1">
                    <button
                      onClick={() => handleExportStamp(stamp.name)}
                      className="text-[9px] px-2 py-1 rounded border border-fn-border text-gray-500 hover:text-white hover:border-gray-500 transition-colors"
                      title="Export stamp as JSON"
                    >
                      Export
                    </button>
                  </div>
                </div>

                {stamp.description && (
                  <p className="text-[10px] text-gray-500 mb-3">{stamp.description}</p>
                )}

                {stamp.tags && stamp.tags.length > 0 && (
                  <div className="flex flex-wrap gap-1 mb-3">
                    {stamp.tags.map((tag) => (
                      <span key={tag} className="text-[8px] px-1.5 py-0.5 rounded bg-fn-darker border border-fn-border text-gray-500">
                        {tag}
                      </span>
                    ))}
                  </div>
                )}

                {/* Place controls */}
                {placingStamp === stamp.name ? (
                  <div className="space-y-2 mt-3 pt-3 border-t border-fn-border">
                    <div className="grid grid-cols-3 gap-2">
                      <div>
                        <label className="text-[8px] text-gray-600 uppercase">X</label>
                        <input
                          type="number" value={placeX} onChange={(e) => setPlaceX(e.target.value)}
                          className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white focus:border-fn-rare/40 focus:outline-none"
                        />
                      </div>
                      <div>
                        <label className="text-[8px] text-gray-600 uppercase">Y</label>
                        <input
                          type="number" value={placeY} onChange={(e) => setPlaceY(e.target.value)}
                          className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white focus:border-fn-rare/40 focus:outline-none"
                        />
                      </div>
                      <div>
                        <label className="text-[8px] text-gray-600 uppercase">Z</label>
                        <input
                          type="number" value={placeZ} onChange={(e) => setPlaceZ(e.target.value)}
                          className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white focus:border-fn-rare/40 focus:outline-none"
                        />
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <button
                        onClick={handlePlaceStamp}
                        disabled={placing}
                        className="flex-1 text-[10px] py-1.5 rounded-lg bg-fn-rare/20 text-fn-rare hover:bg-fn-rare/30 disabled:opacity-40 transition-colors"
                      >
                        {placing ? 'Placing...' : 'Place Here'}
                      </button>
                      <button
                        onClick={() => setPlacingStamp(null)}
                        className="text-[10px] px-3 py-1.5 rounded-lg border border-fn-border text-gray-400 hover:text-white transition-colors"
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                ) : (
                  <button
                    onClick={() => setPlacingStamp(stamp.name)}
                    disabled={!bridgeConnected}
                    className="w-full mt-2 text-[10px] py-1.5 rounded-lg border border-fn-rare/20 text-fn-rare/80 hover:bg-fn-rare/10 disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                  >
                    Place Stamp
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
