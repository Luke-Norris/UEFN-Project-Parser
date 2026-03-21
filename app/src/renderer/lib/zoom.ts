/** Apply zoom via CSS transform — doesn't break layout like body.zoom does */
export function applyZoom(zoom: number) {
  const root = document.getElementById('root')
  if (!root) return
  if (zoom === 1) {
    root.style.transform = ''
    root.style.width = '100%'
    root.style.height = '100%'
  } else {
    root.style.transform = `scale(${zoom})`
    root.style.width = `${100 / zoom}%`
    root.style.height = `${100 / zoom}%`
  }
  document.body.style.zoom = ''
}
