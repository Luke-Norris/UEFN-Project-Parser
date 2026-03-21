import React from 'react'
import ReactDOM from 'react-dom/client'
// Initialize Tauri API bridge — installs window.electronAPI shim for backward compat
import './lib/api'
import App from './App'
import { ErrorBoundary } from './components/ErrorBoundary'
import './styles/index.css'

// Catch unhandled promise rejections so they don't crash the app
window.addEventListener('unhandledrejection', (e) => {
  console.error('[Unhandled Rejection]', e.reason)
  e.preventDefault()
})

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </React.StrictMode>
)
