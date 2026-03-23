/**
 * VerseEditor — CodeMirror 6 editor for Verse files.
 * Replaces VerseHighlighter for the main verse file viewer.
 * Supports read-only mode, scroll-to-line, Verse syntax highlighting,
 * and optional LSP features (autocomplete, hover, goto-def).
 */

import { useEffect, useRef, useCallback, useState } from 'react'
import { EditorState, type Extension } from '@codemirror/state'
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { bracketMatching, indentOnInput, foldGutter, foldKeymap } from '@codemirror/language'
import { search, searchKeymap, highlightSelectionMatches } from '@codemirror/search'
import { lintGutter } from '@codemirror/lint'
import { verseLanguage, verseEditorTheme, verseSyntaxHighlighting } from '../verse-language'
import {
  verseLspAutocompletion,
  verseLspHoverTooltip,
  getDefinitionAt,
  filePathToUri,
} from '../verse-language/verse-lsp-extensions'
import { verseLspLinter } from '../verse-language/verse-lsp-diagnostics'

interface VerseEditorProps {
  source: string
  readOnly?: boolean
  fontSize?: number
  scrollToLine?: number
  /** Absolute file path — enables LSP features when provided */
  filePath?: string
  /** Whether the LSP is connected and ready */
  lspReady?: boolean
  onLineClick?: (lineNum: number) => void
  onChange?: (content: string) => void
  /** Called when goto-definition navigates to another file */
  onGotoDefinition?: (uri: string, line: number) => void
}

export function VerseEditor({
  source,
  readOnly = true,
  fontSize = 12,
  scrollToLine,
  filePath,
  lspReady = false,
  onLineClick,
  onChange,
  onGotoDefinition,
}: VerseEditorProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [isReady, setIsReady] = useState(false)

  const fileUri = filePath ? filePathToUri(filePath) : ''

  // Build extensions list
  const getExtensions = useCallback((): Extension[] => {
    const extensions: Extension[] = [
      verseLanguage,
      verseEditorTheme,
      verseSyntaxHighlighting,
      lineNumbers(),
      highlightActiveLine(),
      highlightActiveLineGutter(),
      bracketMatching(),
      indentOnInput(),
      foldGutter(),
      search(),
      highlightSelectionMatches(),
      history(),
      keymap.of([
        ...defaultKeymap,
        ...historyKeymap,
        ...foldKeymap,
        ...searchKeymap,
      ]),
      // Custom font size
      EditorView.theme({
        '&': { fontSize: `${fontSize}px` },
        '.cm-gutters': { fontSize: `${fontSize}px` },
      }),
    ]

    if (readOnly) {
      extensions.push(EditorState.readOnly.of(true))
      extensions.push(EditorView.editable.of(false))
    }

    // LSP-powered features (when LSP is connected and we have a file path)
    if (lspReady && fileUri) {
      extensions.push(verseLspAutocompletion(fileUri))
      extensions.push(verseLspHoverTooltip(fileUri))
      extensions.push(verseLspLinter(fileUri))
      extensions.push(lintGutter())

      // Ctrl+Click goto definition
      if (onGotoDefinition) {
        extensions.push(EditorView.domEventHandlers({
          click(event, view) {
            if (event.ctrlKey || event.metaKey) {
              const pos = view.posAtCoords({ x: event.clientX, y: event.clientY })
              if (pos != null) {
                const line = view.state.doc.lineAt(pos)
                const lspLine = line.number - 1
                const character = pos - line.from
                getDefinitionAt(fileUri, lspLine, character).then(result => {
                  if (result) {
                    onGotoDefinition(result.uri, result.line + 1) // convert to 1-based
                  }
                })
                event.preventDefault()
              }
            }
          },
        }))
      }
    }

    if (onChange) {
      extensions.push(EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          onChange(update.state.doc.toString())
        }
      }))
    }

    // Line click handler via gutter (when not doing Ctrl+Click)
    if (onLineClick) {
      extensions.push(EditorView.domEventHandlers({
        click(event, view) {
          if (!event.ctrlKey && !event.metaKey) {
            const pos = view.posAtCoords({ x: event.clientX, y: event.clientY })
            if (pos != null) {
              const line = view.state.doc.lineAt(pos)
              onLineClick(line.number)
            }
          }
        },
      }))
    }

    return extensions
  }, [readOnly, fontSize, onChange, onLineClick, lspReady, fileUri, onGotoDefinition])

  // Create editor on mount
  useEffect(() => {
    if (!containerRef.current) return

    const state = EditorState.create({
      doc: source,
      extensions: getExtensions(),
    })

    const view = new EditorView({
      state,
      parent: containerRef.current,
    })

    viewRef.current = view
    setIsReady(true)

    return () => {
      view.destroy()
      viewRef.current = null
      setIsReady(false)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Update source when it changes externally
  useEffect(() => {
    const view = viewRef.current
    if (!view || !isReady) return

    const currentDoc = view.state.doc.toString()
    if (currentDoc !== source) {
      view.dispatch({
        changes: {
          from: 0,
          to: currentDoc.length,
          insert: source,
        },
      })
    }
  }, [source, isReady])

  // Recreate editor when key props change (including LSP readiness)
  useEffect(() => {
    const view = viewRef.current
    if (!view || !isReady || !containerRef.current) return

    const currentDoc = view.state.doc.toString()
    view.destroy()

    const state = EditorState.create({
      doc: currentDoc,
      extensions: getExtensions(),
    })

    const newView = new EditorView({
      state,
      parent: containerRef.current,
    })

    viewRef.current = newView
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [readOnly, fontSize, lspReady, fileUri])

  // Scroll to line
  useEffect(() => {
    const view = viewRef.current
    if (!view || !isReady || scrollToLine == null || scrollToLine <= 0) return

    const lineCount = view.state.doc.lines
    const targetLine = Math.min(scrollToLine, lineCount)

    try {
      const line = view.state.doc.line(targetLine)
      view.dispatch({
        effects: EditorView.scrollIntoView(line.from, { y: 'center' }),
        selection: { anchor: line.from },
      })
    } catch {
      // Line number out of range
    }
  }, [scrollToLine, isReady])

  return (
    <div
      ref={containerRef}
      className="verse-editor h-full overflow-hidden [&>.cm-editor]:h-full [&>.cm-editor>.cm-scroller]:overflow-auto"
      style={{ '--fn-darker': '#0d1117', '--fn-dark': '#161b22' } as React.CSSProperties}
    />
  )
}
