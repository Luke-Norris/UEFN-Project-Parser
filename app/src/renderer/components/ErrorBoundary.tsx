import React from 'react'

interface State {
  hasError: boolean
  error: Error | null
  errorInfo: string
}

export class ErrorBoundary extends React.Component<{ children: React.ReactNode }, State> {
  state: State = { hasError: false, error: null, errorInfo: '' }

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error('[ErrorBoundary] Caught:', error, info.componentStack)
    this.setState({ errorInfo: info.componentStack || '' })
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="h-screen flex items-center justify-center bg-[#0a0a1a] text-white p-8">
          <div className="max-w-lg text-center">
            <div className="text-4xl mb-4">:(</div>
            <h1 className="text-lg font-semibold mb-2">WellVersed encountered an error</h1>
            <p className="text-sm text-gray-400 mb-4">{this.state.error?.message}</p>
            <pre className="text-[10px] text-gray-600 bg-[#111] rounded p-3 mb-4 max-h-40 overflow-auto text-left whitespace-pre-wrap">
              {this.state.error?.stack}
              {this.state.errorInfo && `\n\nComponent Stack:${this.state.errorInfo}`}
            </pre>
            <button
              onClick={() => {
                const text = `WellVersed Error\n\n${this.state.error?.message}\n\n${this.state.error?.stack || ''}${this.state.errorInfo ? `\n\nComponent Stack:${this.state.errorInfo}` : ''}`
                navigator.clipboard.writeText(text).then(() => {
                  const btn = document.getElementById('copy-error-btn')
                  if (btn) { btn.textContent = 'Copied!'; setTimeout(() => { btn.textContent = 'Copy Error' }, 2000) }
                })
              }}
              id="copy-error-btn"
              className="px-4 py-2 text-sm bg-red-800 hover:bg-red-700 rounded transition-colors mr-2"
            >
              Copy Error
            </button>
            <button
              onClick={() => {
                this.setState({ hasError: false, error: null, errorInfo: '' })
              }}
              className="px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 rounded transition-colors mr-2"
            >
              Try Again
            </button>
            <button
              onClick={() => window.location.reload()}
              className="px-4 py-2 text-sm bg-gray-700 hover:bg-gray-600 rounded transition-colors"
            >
              Reload App
            </button>
          </div>
        </div>
      )
    }
    return this.props.children
  }
}
