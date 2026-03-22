/**
 * CodeMirror extensions that connect to Epic's verse-lsp via Tauri.
 * Provides autocomplete, hover tooltips, and go-to-definition.
 */

import { autocompletion, type CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { hoverTooltip, type Tooltip } from '@codemirror/view'
import { lspCompletion, lspHover, lspDefinition, lspSignatureHelp } from '../lib/api'

// ─── File URI helper ─────────────────────────────────────────────────────────

export function filePathToUri(filePath: string): string {
  // Convert Windows path to file:// URI
  const normalized = filePath.replace(/\\/g, '/')
  if (normalized.startsWith('/')) return `file://${normalized}`
  return `file:///${normalized}`
}

// ─── Autocomplete extension ─────────────────────────────────────────────────

function verseLspCompletionSource(fileUri: string) {
  return async (context: CompletionContext): Promise<CompletionResult | null> => {
    const pos = context.state.doc.lineAt(context.pos)
    const line = pos.number - 1 // LSP uses 0-based lines
    const character = context.pos - pos.from

    // Only trigger on explicit completion or after trigger characters
    if (!context.explicit && !context.matchBefore(/[\w.]$/)) {
      return null
    }

    try {
      const result = await lspCompletion(fileUri, line, character) as any
      if (!result) return null

      const items = Array.isArray(result) ? result : result.items ?? []
      if (items.length === 0) return null

      // Find word start for replacement range
      const word = context.matchBefore(/[\w]*/)
      const from = word ? word.from : context.pos

      return {
        from,
        options: items.map((item: any) => ({
          label: item.label ?? item.insertText ?? '',
          detail: item.detail ?? item.labelDetails?.detail ?? '',
          info: item.documentation?.value ?? item.documentation ?? undefined,
          type: lspKindToType(item.kind),
          boost: item.sortText ? -parseInt(item.sortText, 10) : 0,
        })),
      }
    } catch {
      return null
    }
  }
}

function lspKindToType(kind?: number): string {
  // LSP CompletionItemKind → CodeMirror completion type
  const kinds: Record<number, string> = {
    1: 'text',       // Text
    2: 'function',   // Method
    3: 'function',   // Function
    4: 'function',   // Constructor
    5: 'variable',   // Field
    6: 'variable',   // Variable
    7: 'class',      // Class
    8: 'interface',  // Interface
    9: 'namespace',  // Module
    10: 'property',  // Property
    13: 'enum',      // Enum
    14: 'keyword',   // Keyword
    15: 'text',      // Snippet
    21: 'constant',  // Constant
    22: 'class',     // Struct
    25: 'type',      // TypeParameter
  }
  return kind ? kinds[kind] ?? 'text' : 'text'
}

export function verseLspAutocompletion(fileUri: string) {
  return autocompletion({
    override: [verseLspCompletionSource(fileUri)],
    activateOnTyping: true,
    maxRenderedOptions: 50,
  })
}

// ─── Hover tooltip extension ─────────────────────────────────────────────────

export function verseLspHoverTooltip(fileUri: string) {
  return hoverTooltip(async (view, pos): Promise<Tooltip | null> => {
    const line = view.state.doc.lineAt(pos)
    const lspLine = line.number - 1
    const character = pos - line.from

    try {
      const result = await lspHover(fileUri, lspLine, character) as any
      if (!result || !result.contents) return null

      // Extract hover content
      let content = ''
      if (typeof result.contents === 'string') {
        content = result.contents
      } else if (result.contents.value) {
        content = result.contents.value
      } else if (Array.isArray(result.contents)) {
        content = result.contents.map((c: any) =>
          typeof c === 'string' ? c : c.value ?? ''
        ).join('\n')
      }

      if (!content.trim()) return null

      return {
        pos,
        above: true,
        create() {
          const dom = document.createElement('div')
          dom.className = 'verse-hover-tooltip'
          dom.style.cssText = `
            max-width: 500px;
            padding: 8px 12px;
            font-family: 'JetBrains Mono', 'Cascadia Code', monospace;
            font-size: 11px;
            line-height: 1.5;
            color: #d1d5db;
            white-space: pre-wrap;
            word-wrap: break-word;
          `

          // Simple markdown-ish rendering
          if (content.startsWith('```')) {
            // Code block
            const code = content.replace(/^```\w*\n?/, '').replace(/\n?```$/, '')
            const pre = document.createElement('pre')
            pre.style.cssText = 'margin: 0; color: #22d3ee;'
            pre.textContent = code
            dom.appendChild(pre)
          } else {
            dom.textContent = content
          }

          return { dom }
        },
      }
    } catch {
      return null
    }
  }, { hoverTime: 300 })
}

// ─── Go-to-definition (Ctrl+Click) ──────────────────────────────────────────

export interface GotoDefinitionResult {
  uri: string
  line: number      // 0-based
  character: number  // 0-based
}

export async function getDefinitionAt(
  fileUri: string,
  line: number,
  character: number,
): Promise<GotoDefinitionResult | null> {
  try {
    const result = await lspDefinition(fileUri, line, character) as any
    if (!result) return null

    // Result can be Location or Location[]
    const loc = Array.isArray(result) ? result[0] : result
    if (!loc?.uri || loc.range == null) return null

    return {
      uri: loc.uri,
      line: loc.range.start.line,
      character: loc.range.start.character,
    }
  } catch {
    return null
  }
}
