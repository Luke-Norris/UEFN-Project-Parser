import { useCallback, useEffect, useRef } from 'react'

interface ResizeHandleProps {
  direction: 'horizontal' | 'vertical'
  onResize: (delta: number) => void
  className?: string
}

export function ResizeHandle({ direction, onResize, className }: ResizeHandleProps) {
  const dragging = useRef(false)
  const lastPos = useRef(0)
  const onResizeRef = useRef(onResize)
  onResizeRef.current = onResize

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    dragging.current = true
    lastPos.current = direction === 'horizontal' ? e.clientX : e.clientY
    document.body.style.cursor = direction === 'horizontal' ? 'col-resize' : 'row-resize'
    document.body.style.userSelect = 'none'
  }, [direction])

  useEffect(() => {
    function handleMouseMove(e: MouseEvent) {
      if (!dragging.current) return
      e.preventDefault()
      const pos = direction === 'horizontal' ? e.clientX : e.clientY
      const delta = pos - lastPos.current
      if (delta !== 0) {
        lastPos.current = pos
        onResizeRef.current(delta)
      }
    }

    function handleMouseUp() {
      if (!dragging.current) return
      dragging.current = false
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
    }

    document.addEventListener('mousemove', handleMouseMove)
    document.addEventListener('mouseup', handleMouseUp)
    return () => {
      document.removeEventListener('mousemove', handleMouseMove)
      document.removeEventListener('mouseup', handleMouseUp)
    }
  }, [direction])

  const isH = direction === 'horizontal'

  return (
    <div
      onMouseDown={handleMouseDown}
      className={`group relative shrink-0 z-20
        ${isH ? 'w-1.5 cursor-col-resize' : 'h-1.5 cursor-row-resize'}
        ${className ?? ''}`}
    >
      {/* Wider invisible hit area */}
      <div className={`absolute ${isH ? 'inset-y-0 -left-1 -right-1' : 'inset-x-0 -top-1 -bottom-1'}`} />
      {/* Visible line */}
      <div className={`absolute ${isH ? 'inset-y-0 left-0 w-px' : 'inset-x-0 top-0 h-px'}
        bg-fn-border group-hover:bg-blue-500/60 group-active:bg-blue-500 transition-colors`}
      />
    </div>
  )
}
