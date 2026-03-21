import { useCallback } from 'react'
import { FabricImage } from 'fabric'
import { useCanvasStore } from '../stores/canvasStore'

export function useAssetDrag() {
  const { canvas } = useCanvasStore()

  const handleAssetDrop = useCallback(
    async (assetPath: string, layerId?: string) => {
      if (!canvas) return

      const dataUrl = await window.electronAPI.getAssetData(assetPath)
      if (!dataUrl) return

      if (layerId) {
        // Replace existing layer image
        const objects = canvas.getObjects()
        const target = objects.find((obj) => (obj as any).layerId === layerId)
        if (target) {
          const img = await FabricImage.fromURL(dataUrl)
          img.set({
            left: target.left,
            top: target.top,
            scaleX: (target.width! * (target.scaleX || 1)) / img.width!,
            scaleY: (target.height! * (target.scaleY || 1)) / img.height!,
            selectable: target.selectable
          })
          ;(img as any).layerId = layerId
          ;(img as any).layerName = (target as any).layerName

          // Remove placeholder label if exists
          const label = objects.find((obj) => (obj as any).layerId === layerId + '-label')
          if (label) canvas.remove(label)

          const idx = objects.indexOf(target)
          canvas.remove(target)
          canvas.insertAt(idx, img)
          canvas.renderAll()
          return
        }
      }

      // Add as new object to canvas center
      const img = await FabricImage.fromURL(dataUrl)
      const maxDim = Math.max(img.width!, img.height!)
      const scale = Math.min(200 / maxDim, 1)
      img.set({
        left: canvas.width! / 2,
        top: canvas.height! / 2,
        scaleX: scale,
        scaleY: scale,
        originX: 'center',
        originY: 'center'
      })
      canvas.add(img)
      canvas.setActiveObject(img)
      canvas.renderAll()
    },
    [canvas]
  )

  return { handleAssetDrop }
}
