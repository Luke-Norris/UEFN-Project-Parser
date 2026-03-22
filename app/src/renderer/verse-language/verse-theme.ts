/**
 * CodeMirror 6 theme for Verse — matches the dark theme from VerseHighlighter.
 * Colors aligned with the Tailwind classes used in TOKEN_CLASSES.
 */

import { EditorView } from '@codemirror/view'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { tags as t } from '@lezer/highlight'

// ─── Editor theme (chrome, gutters, selection) ───────────────────────────────

export const verseEditorTheme = EditorView.theme({
  '&': {
    backgroundColor: 'transparent',
    color: '#d1d5db', // text-gray-300
    fontSize: '12px',
    fontFamily: "'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace",
  },
  '.cm-content': {
    caretColor: '#60a5fa', // blue-400
    padding: '0',
  },
  '.cm-cursor, .cm-dropCursor': {
    borderLeftColor: '#60a5fa',
  },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: 'rgba(96, 165, 250, 0.2)', // blue-400/20
  },
  '.cm-activeLine': {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
  },
  '.cm-gutters': {
    backgroundColor: 'var(--fn-darker, #0d1117)',
    color: '#4b5563', // text-gray-600
    border: 'none',
    borderRight: '1px solid rgba(255, 255, 255, 0.06)',
  },
  '.cm-activeLineGutter': {
    backgroundColor: 'rgba(96, 165, 250, 0.05)',
    color: '#60a5fa',
  },
  '.cm-lineNumbers .cm-gutterElement': {
    padding: '0 12px 0 8px',
    minWidth: '40px',
  },
  '.cm-foldPlaceholder': {
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
    color: '#6b7280',
    border: 'none',
  },
  // Search panel
  '.cm-panels': {
    backgroundColor: 'var(--fn-dark, #161b22)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
    color: '#d1d5db',
  },
  '.cm-panels.cm-panels-top': {
    borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
  },
  '.cm-searchMatch': {
    backgroundColor: 'rgba(245, 158, 11, 0.25)', // amber-500/25
    outline: 'none',
  },
  '.cm-searchMatch.cm-searchMatch-selected': {
    backgroundColor: 'rgba(251, 191, 36, 0.5)', // amber-400/50
    outline: '1px solid rgba(251, 191, 36, 0.7)',
  },
  '.cm-panel.cm-search': {
    padding: '4px 8px',
  },
  '.cm-panel.cm-search input': {
    backgroundColor: 'var(--fn-darker, #0d1117)',
    border: '1px solid rgba(255, 255, 255, 0.06)',
    borderRadius: '4px',
    color: 'white',
    padding: '2px 8px',
    fontSize: '11px',
  },
  '.cm-panel.cm-search button': {
    backgroundColor: 'transparent',
    color: '#9ca3af',
    border: '1px solid rgba(255, 255, 255, 0.06)',
    borderRadius: '4px',
    padding: '2px 8px',
    fontSize: '11px',
    cursor: 'pointer',
  },
  '.cm-panel.cm-search button:hover': {
    color: 'white',
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
  },
  '.cm-panel.cm-search label': {
    color: '#9ca3af',
    fontSize: '11px',
  },
  // Tooltip
  '.cm-tooltip': {
    backgroundColor: 'var(--fn-dark, #161b22)',
    border: '1px solid rgba(255, 255, 255, 0.1)',
    borderRadius: '6px',
    boxShadow: '0 4px 12px rgba(0, 0, 0, 0.3)',
  },
  '.cm-tooltip-autocomplete': {
    '& > ul > li': {
      padding: '2px 8px',
    },
    '& > ul > li[aria-selected]': {
      backgroundColor: 'rgba(96, 165, 250, 0.15)',
      color: 'white',
    },
  },
}, { dark: true })

// ─── Syntax highlighting (colors matching VerseHighlighter TOKEN_CLASSES) ────

export const verseHighlightStyle = HighlightStyle.define([
  // Comments — text-gray-500 italic
  { tag: t.comment, color: '#6b7280', fontStyle: 'italic' },
  { tag: t.blockComment, color: '#6b7280', fontStyle: 'italic' },
  { tag: t.lineComment, color: '#6b7280', fontStyle: 'italic' },

  // Keywords — text-blue-400
  { tag: t.keyword, color: '#60a5fa' },
  { tag: t.controlKeyword, color: '#60a5fa' },

  // Types — text-cyan-400
  { tag: t.typeName, color: '#22d3ee' },

  // Strings — text-green-400
  { tag: t.string, color: '#4ade80' },

  // Numbers — text-orange-400
  { tag: t.number, color: '#fb923c' },

  // Specifiers/annotations — text-purple-400
  { tag: t.annotation, color: '#c084fc' },

  // Decorators — text-yellow-400
  { tag: t.meta, color: '#facc15' },

  // Functions — text-amber-300
  { tag: t.function(t.definition(t.variableName)), color: '#fcd34d' },
  { tag: t.function(t.variableName), color: '#fcd34d' },

  // Class definitions — text-emerald-400 bold
  { tag: t.definition(t.typeName), color: '#34d399', fontWeight: 'bold' },
  { tag: t.className, color: '#34d399', fontWeight: 'bold' },

  // Operators — text-gray-300
  { tag: t.operator, color: '#d1d5db' },
  { tag: t.logicOperator, color: '#d1d5db' },

  // Booleans — text-blue-400
  { tag: t.bool, color: '#60a5fa' },

  // Punctuation — text-gray-400
  { tag: t.punctuation, color: '#9ca3af' },
  { tag: t.paren, color: '#9ca3af' },
  { tag: t.bracket, color: '#9ca3af' },
  { tag: t.brace, color: '#9ca3af' },

  // Variables — default gray-300
  { tag: t.variableName, color: '#d1d5db' },

  // Property access
  { tag: t.propertyName, color: '#d1d5db' },
])

export const verseSyntaxHighlighting = syntaxHighlighting(verseHighlightStyle)
