import { useState } from 'react'

export function ErrorMessage({ message }: { message: string }) {
  const [copied, setCopied] = useState(false)

  function handleCopy() {
    navigator.clipboard.writeText(message).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }

  return (
    <div className="flex items-start gap-2 px-3 py-2 text-[10px] text-red-400 bg-red-400/10 border border-red-400/20 rounded">
      <span className="flex-1 break-all">{message}</span>
      <button
        onClick={handleCopy}
        className="shrink-0 px-1.5 py-0.5 text-[9px] rounded bg-red-400/20 hover:bg-red-400/30 transition-colors"
        title="Copy error"
      >
        {copied ? 'Copied' : 'Copy'}
      </button>
    </div>
  )
}
