/**
 * LSP diagnostics → CodeMirror lint integration.
 * Listens for publishDiagnostics events from Tauri and converts
 * them to CodeMirror Diagnostic objects for the lint gutter.
 */

import { linter, type Diagnostic } from '@codemirror/lint'
import type { EditorView } from '@codemirror/view'
import { listen, type UnlistenFn } from '@tauri-apps/api/event'

// ─── LSP Diagnostic types ────────────────────────────────────────────────────

interface LspDiagnostic {
  range: {
    start: { line: number; character: number }
    end: { line: number; character: number }
  }
  severity?: number // 1=Error, 2=Warning, 3=Info, 4=Hint
  message: string
  source?: string
  code?: string | number
}

interface PublishDiagnosticsParams {
  uri: string
  diagnostics: LspDiagnostic[]
}

// ─── Diagnostic store ────────────────────────────────────────────────────────

/** Global store of diagnostics per file URI */
const diagnosticStore = new Map<string, LspDiagnostic[]>()
const listeners = new Map<string, Set<() => void>>()

/** Start listening for LSP diagnostic events from Tauri */
let unlistenFn: UnlistenFn | null = null

export async function startDiagnosticListener(): Promise<void> {
  if (unlistenFn) return // already listening

  try {
    unlistenFn = await listen<PublishDiagnosticsParams>(
      'lsp:textDocument:publishDiagnostics',
      (event) => {
        const { uri, diagnostics } = event.payload
        diagnosticStore.set(uri, diagnostics)

        // Notify any editors watching this URI
        const watchers = listeners.get(uri)
        if (watchers) {
          for (const cb of watchers) cb()
        }
      }
    )
  } catch {
    // Not running in Tauri — ignore
  }
}

export function stopDiagnosticListener(): void {
  if (unlistenFn) {
    unlistenFn()
    unlistenFn = null
  }
  diagnosticStore.clear()
}

function subscribeDiagnostics(uri: string, callback: () => void): () => void {
  if (!listeners.has(uri)) {
    listeners.set(uri, new Set())
  }
  listeners.get(uri)!.add(callback)
  return () => {
    listeners.get(uri)?.delete(callback)
  }
}

// ─── CodeMirror lint extension ───────────────────────────────────────────────

function lspSeverityToCm(severity?: number): 'error' | 'warning' | 'info' {
  switch (severity) {
    case 1: return 'error'
    case 2: return 'warning'
    case 3: return 'info'
    case 4: return 'info' // hint → info
    default: return 'warning'
  }
}

/**
 * Create a CodeMirror linter that reads diagnostics from the LSP event store.
 */
export function verseLspLinter(fileUri: string) {
  return linter(
    (view: EditorView): Diagnostic[] => {
      const lspDiags = diagnosticStore.get(fileUri)
      if (!lspDiags || lspDiags.length === 0) return []

      const doc = view.state.doc
      const diagnostics: Diagnostic[] = []

      for (const diag of lspDiags) {
        try {
          // Convert LSP positions (0-based) to CodeMirror offsets
          const startLine = Math.min(diag.range.start.line + 1, doc.lines)
          const endLine = Math.min(diag.range.end.line + 1, doc.lines)
          const startLineObj = doc.line(startLine)
          const endLineObj = doc.line(endLine)

          const from = startLineObj.from + Math.min(diag.range.start.character, startLineObj.length)
          const to = endLineObj.from + Math.min(diag.range.end.character, endLineObj.length)

          diagnostics.push({
            from: Math.max(0, from),
            to: Math.max(from, to), // ensure to >= from
            severity: lspSeverityToCm(diag.severity),
            message: diag.message,
            source: diag.source ?? 'verse-lsp',
          })
        } catch {
          // Skip malformed diagnostics
        }
      }

      return diagnostics
    },
    {
      delay: 200,
      // We need to re-lint when diagnostics arrive from the LSP.
      // The linter will be called on doc changes; we also trigger manually.
      needsRefresh: (update) => {
        // Always allow refresh — diagnostics come asynchronously
        return update.docChanged
      },
    }
  )
}

/**
 * Get current diagnostic count for a file URI.
 */
export function getDiagnosticCount(fileUri: string): { errors: number; warnings: number; info: number } {
  const diags = diagnosticStore.get(fileUri) ?? []
  return {
    errors: diags.filter(d => d.severity === 1).length,
    warnings: diags.filter(d => d.severity === 2).length,
    info: diags.filter(d => d.severity === 3 || d.severity === 4).length,
  }
}
