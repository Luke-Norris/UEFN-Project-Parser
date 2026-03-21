import { useCallback, useEffect, useRef } from 'react'

interface ResizeHandleProps {
  /** Direction the handle resizes */
  direction: 'horizontal' | 'vertical'
  /** Called with delta pixels while dragging */
  onResize: (delta: number) => void
  /** Optional class overrides */
  className?: string
}

export function ResizeHandle({ direction, onResize, className }: ResizeHandleProps) {
  const dragging = useRef(false)
  const lastPos = useRef(0)

  const handleMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    dragging.current = true
    lastPos.current = direction === 'horizontal' ? e.clientX : e.clientY
    document.body.style.cursor = direction === 'horizontal' ? 'col-resize' : 'row-resize'
    document.body.style.userSelect = 'none'
  }, [direction])

  useEffect(() => {
    function handleMouseMove(e: MouseEvent) {
      if (!dragging.current) return
      const pos = direction === 'horizontal' ? e.clientX : e.clientY
      const delta = pos - lastPos.current
      lastPos.current = pos
      onResize(delta)
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
  }, [direction, onResize])

  const isH = direction === 'horizontal'

  return (
    <div
      onMouseDown={handleMouseDown}
      className={`${isH ? 'w-1 cursor-col-resize hover:w-1.5' : 'h-1 cursor-row-resize hover:h-1.5'}
        bg-transparent hover:bg-blue-500/40 active:bg-blue-500/60 transition-colors shrink-0 z-10
        ${className ?? ''}`}
    />
  )
}
