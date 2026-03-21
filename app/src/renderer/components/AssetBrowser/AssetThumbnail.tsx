import { useState, useEffect, useRef } from 'react'
import type { AssetEntry } from '../../../shared/types'

interface Props {
  asset: AssetEntry
  onClick: () => void
  onContextMenu?: (e: React.MouseEvent) => void
}

export function AssetThumbnail({ asset, onClick, onContextMenu }: Props) {
  const [dataUrl, setDataUrl] = useState<string | null>(null)
  const [loaded, setLoaded] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  // Lazy load using intersection observer
  useEffect(() => {
    const el = ref.current
    if (!el) return

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && !loaded) {
          setLoaded(true)
          window.electronAPI.getAssetData(asset.path).then((data) => {
            if (data) setDataUrl(data)
          })
        }
      },
      { threshold: 0.1 }
    )

    observer.observe(el)
    return () => observer.disconnect()
  }, [asset.path, loaded])

  return (
    <div
      ref={ref}
      className="asset-thumb"
      onClick={onClick}
      onContextMenu={onContextMenu}
      title={asset.name}
    >
      {dataUrl ? (
        <img src={dataUrl} alt={asset.name} loading="lazy" />
      ) : (
        <div className="w-full h-full flex items-center justify-center">
          <span className="text-[10px] text-gray-600 text-center px-1 truncate">
            {asset.name}
          </span>
        </div>
      )}
    </div>
  )
}
