import { useEffect, useRef, useState, useCallback } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import { ErrorMessage } from '../components/ErrorMessage'
import type { DeviceEntry, DeviceInspectResult } from '../../shared/types'

// ─── Device type → color mapping ─────────────────────────────────────────────

const TYPE_COLORS: Record<string, number> = {
  // Creative devices
  'ItemSpawnerDevice': 0x3d85e0,
  'VendingMachineDevice': 0xc76b29,
  'PlayerSpawnDevice': 0x60aa3a,
  'EliminationManagerDevice': 0xe04040,
  'ScoreManagerDevice': 0xa34ee1,
  'TeamSettingsDevice': 0x76d6e3,
  'TimerDevice': 0xc4a23c,
  'TriggerDevice': 0xff6b9d,
  'BarrierDevice': 0x4a9eff,
  'DamageVolumeDevice': 0xff4444,
  'HUDMessageDevice': 0x44ff88,
  'ButtonDevice': 0xffaa00,
  'ConditionDevice': 0x9966ff,
  'ClassSelectorDevice': 0x66ffcc,
  'MatchmakingPortalDevice': 0xff66aa,
  'MapIndicatorDevice': 0xffff44,
}

const DEFAULT_COLOR = 0x888899
const HOVER_COLOR = 0xffffff
const SELECTED_COLOR = 0x00ffaa
const GROUND_COLOR = 0x1a1a2e
const GRID_COLOR = 0x2a2a4a

function getDeviceColor(deviceType: string): number {
  // Check exact match first
  if (TYPE_COLORS[deviceType]) return TYPE_COLORS[deviceType]
  // Check partial match
  for (const [key, color] of Object.entries(TYPE_COLORS)) {
    if (deviceType.includes(key.replace('Device', ''))) return color
  }
  // Hash-based color for unknown types
  let hash = 0
  for (let i = 0; i < deviceType.length; i++) {
    hash = deviceType.charCodeAt(i) + ((hash << 5) - hash)
  }
  const h = Math.abs(hash) % 360
  const color = new THREE.Color()
  color.setHSL(h / 360, 0.6, 0.5)
  return color.getHex()
}

// ─── Device shape by type ────────────────────────────────────────────────────

function createDeviceMesh(device: DeviceEntry, geometry: THREE.BufferGeometry, color: number): THREE.Mesh {
  const material = new THREE.MeshLambertMaterial({
    color,
    transparent: true,
    opacity: 0.85,
  })
  const mesh = new THREE.Mesh(geometry, material)
  if (device.position) {
    // Unreal uses cm, scale down to meters for Three.js
    mesh.position.set(
      device.position.x / 100,
      device.position.z / 100, // Unreal Z = Three.js Y (up)
      -device.position.y / 100, // Unreal Y = Three.js -Z (forward)
    )
  }
  mesh.userData = { device }
  return mesh
}

// ─── Main Component ──────────────────────────────────────────────────────────

interface ScenePreviewPageProps {
  selectedLevel?: string | null
}

export function ScenePreviewPage({ selectedLevel }: ScenePreviewPageProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const rendererRef = useRef<THREE.WebGLRenderer | null>(null)
  const sceneRef = useRef<THREE.Scene | null>(null)
  const cameraRef = useRef<THREE.PerspectiveCamera | null>(null)
  const controlsRef = useRef<OrbitControls | null>(null)
  const raycasterRef = useRef(new THREE.Raycaster())
  const mouseRef = useRef(new THREE.Vector2())
  const deviceMeshesRef = useRef<THREE.Mesh[]>([])
  const labelRef = useRef<HTMLDivElement>(null)
  const animFrameRef = useRef<number>(0)

  const [devices, setDevices] = useState<DeviceEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hoveredDevice, setHoveredDevice] = useState<DeviceEntry | null>(null)
  const [selectedDevice, setSelectedDevice] = useState<DeviceEntry | null>(null)
  const [inspectResult, setInspectResult] = useState<DeviceInspectResult | null>(null)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [stats, setStats] = useState({ total: 0, withPos: 0, types: 0 })
  const [levelPath, setLevelPath] = useState<string | null>(selectedLevel ?? null)

  // Fetch levels directly (bypass cache to get fresh data for active project)
  const [localLevels, setLocalLevels] = useState<Array<{ filePath: string; name: string }>>([])
  useEffect(() => {
    window.electronAPI.forgeListLevels()
      .then((result) => {
        const lvls = Array.isArray(result) ? result : []
        setLocalLevels(lvls)
        if (!levelPath && lvls.length > 0) {
          setLevelPath(lvls[0].filePath)
        }
      })
      .catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Fetch devices when level changes
  useEffect(() => {
    if (!levelPath) return
    setLoading(true)
    setError(null)
    setDevices([])
    setSelectedDevice(null)
    setInspectResult(null)

    window.electronAPI.forgeListDevices(levelPath)
      .then((result) => {
        const devs = result?.devices ?? []
        setDevices(devs)
        const withPos = devs.filter((d: any) => d.position)
        const types = new Set(devs.map((d: any) => d.deviceType))
        setStats({ total: devs.length, withPos: withPos.length, types: types.size })
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : String(err))
      })
      .finally(() => setLoading(false))
  }, [levelPath])

  // Inspect selected device
  useEffect(() => {
    if (!selectedDevice) { setInspectResult(null); return }
    setInspectLoading(true)
    window.electronAPI.forgeInspectDevice(selectedDevice.filePath)
      .then((r) => setInspectResult(r))
      .catch(() => setInspectResult(null))
      .finally(() => setInspectLoading(false))
  }, [selectedDevice])

  // ─── Three.js setup ──────────────────────────────────────────────────────

  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    // Scene
    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x0a0a1a)
    scene.fog = new THREE.Fog(0x0a0a1a, 500, 2000)
    sceneRef.current = scene

    // Camera
    const camera = new THREE.PerspectiveCamera(60, container.clientWidth / container.clientHeight, 0.1, 5000)
    camera.position.set(50, 80, 50)
    cameraRef.current = camera

    // Renderer
    const renderer = new THREE.WebGLRenderer({ antialias: true })
    renderer.setSize(container.clientWidth, container.clientHeight)
    renderer.setPixelRatio(window.devicePixelRatio)
    container.appendChild(renderer.domElement)
    rendererRef.current = renderer

    // Controls
    const controls = new OrbitControls(camera, renderer.domElement)
    controls.enableDamping = true
    controls.dampingFactor = 0.1
    controls.maxDistance = 2000
    controls.minDistance = 5
    controlsRef.current = controls

    // Lights
    const ambient = new THREE.AmbientLight(0x404060, 1.5)
    scene.add(ambient)
    const directional = new THREE.DirectionalLight(0xffffff, 1.2)
    directional.position.set(100, 200, 100)
    scene.add(directional)
    const directional2 = new THREE.DirectionalLight(0x6060ff, 0.4)
    directional2.position.set(-100, 100, -100)
    scene.add(directional2)

    // Ground grid
    const gridHelper = new THREE.GridHelper(1000, 100, GRID_COLOR, GRID_COLOR)
    gridHelper.material.opacity = 0.3
    gridHelper.material.transparent = true
    scene.add(gridHelper)

    // Ground plane (subtle)
    const groundGeo = new THREE.PlaneGeometry(2000, 2000)
    const groundMat = new THREE.MeshLambertMaterial({ color: GROUND_COLOR, transparent: true, opacity: 0.5 })
    const ground = new THREE.Mesh(groundGeo, groundMat)
    ground.rotation.x = -Math.PI / 2
    ground.position.y = -0.1
    scene.add(ground)

    // Animation loop
    function animate() {
      animFrameRef.current = requestAnimationFrame(animate)
      controls.update()
      renderer.render(scene, camera)
    }
    animate()

    // Resize handler
    function onResize() {
      if (!container) return
      camera.aspect = container.clientWidth / container.clientHeight
      camera.updateProjectionMatrix()
      renderer.setSize(container.clientWidth, container.clientHeight)
    }
    const observer = new ResizeObserver(onResize)
    observer.observe(container)

    return () => {
      cancelAnimationFrame(animFrameRef.current)
      observer.disconnect()
      controls.dispose()
      renderer.dispose()
      if (container.contains(renderer.domElement)) {
        container.removeChild(renderer.domElement)
      }
    }
  }, [])

  // ─── Populate scene with devices ───────────────────────────────────────

  useEffect(() => {
    const scene = sceneRef.current
    if (!scene) return

    // Remove old meshes
    for (const mesh of deviceMeshesRef.current) {
      scene.remove(mesh)
      mesh.geometry.dispose()
      ;(mesh.material as THREE.Material).dispose()
    }
    deviceMeshesRef.current = []

    const devicesWithPos = devices.filter((d) => d.position)
    if (devicesWithPos.length === 0) return

    // Shared geometries
    const boxGeo = new THREE.BoxGeometry(1.5, 2, 1.5)
    const sphereGeo = new THREE.SphereGeometry(1, 12, 8)
    const cylinderGeo = new THREE.CylinderGeometry(0.8, 0.8, 2, 12)

    const meshes: THREE.Mesh[] = []

    for (const device of devicesWithPos) {
      const color = getDeviceColor(device.deviceType)
      const type = device.deviceType.toLowerCase()

      let geo = boxGeo
      if (type.includes('spawn') || type.includes('portal')) geo = sphereGeo
      else if (type.includes('trigger') || type.includes('volume')) geo = cylinderGeo

      const mesh = createDeviceMesh(device, geo, color)
      scene.add(mesh)
      meshes.push(mesh)
    }

    deviceMeshesRef.current = meshes

    // Auto-frame: center camera on all devices
    if (meshes.length > 0) {
      const box = new THREE.Box3()
      for (const m of meshes) box.expandByObject(m)
      const center = box.getCenter(new THREE.Vector3())
      const size = box.getSize(new THREE.Vector3())
      const maxDim = Math.max(size.x, size.y, size.z)
      const dist = Math.max(maxDim * 1.5, 50)

      const camera = cameraRef.current
      const controls = controlsRef.current
      if (camera && controls) {
        controls.target.copy(center)
        camera.position.set(center.x + dist * 0.5, center.y + dist * 0.7, center.z + dist * 0.5)
        controls.update()
      }
    }

    // Don't dispose shared geos — they're reused
  }, [devices])

  // ─── Mouse interaction ─────────────────────────────────────────────────

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    const container = containerRef.current
    if (!container) return

    const rect = container.getBoundingClientRect()
    mouseRef.current.x = ((e.clientX - rect.left) / rect.width) * 2 - 1
    mouseRef.current.y = -((e.clientY - rect.top) / rect.height) * 2 + 1

    const camera = cameraRef.current
    if (!camera) return

    raycasterRef.current.setFromCamera(mouseRef.current, camera)
    const intersects = raycasterRef.current.intersectObjects(deviceMeshesRef.current)

    // Reset all hovered
    for (const mesh of deviceMeshesRef.current) {
      const dev = mesh.userData.device as DeviceEntry
      const isSelected = selectedDevice && dev.filePath === selectedDevice.filePath
      const mat = mesh.material as THREE.MeshLambertMaterial
      if (!isSelected) {
        mat.color.setHex(getDeviceColor(dev.deviceType))
        mat.emissive.setHex(0x000000)
      }
    }

    if (intersects.length > 0) {
      const mesh = intersects[0].object as THREE.Mesh
      const dev = mesh.userData.device as DeviceEntry
      const isSelected = selectedDevice && dev.filePath === selectedDevice.filePath
      if (!isSelected) {
        const mat = mesh.material as THREE.MeshLambertMaterial
        mat.emissive.setHex(0x222244)
      }
      setHoveredDevice(dev)

      // Position label
      if (labelRef.current) {
        labelRef.current.style.left = `${e.clientX - rect.left + 12}px`
        labelRef.current.style.top = `${e.clientY - rect.top - 8}px`
      }
    } else {
      setHoveredDevice(null)
    }
  }, [selectedDevice])

  const handleClick = useCallback((e: React.MouseEvent) => {
    const camera = cameraRef.current
    if (!camera) return

    raycasterRef.current.setFromCamera(mouseRef.current, camera)
    const intersects = raycasterRef.current.intersectObjects(deviceMeshesRef.current)

    // Deselect previous
    for (const mesh of deviceMeshesRef.current) {
      const mat = mesh.material as THREE.MeshLambertMaterial
      const dev = mesh.userData.device as DeviceEntry
      mat.color.setHex(getDeviceColor(dev.deviceType))
      mat.emissive.setHex(0x000000)
      mesh.scale.set(1, 1, 1)
    }

    if (intersects.length > 0) {
      const mesh = intersects[0].object as THREE.Mesh
      const dev = mesh.userData.device as DeviceEntry
      const mat = mesh.material as THREE.MeshLambertMaterial
      mat.color.setHex(SELECTED_COLOR)
      mat.emissive.setHex(0x003322)
      mesh.scale.set(1.3, 1.3, 1.3)
      setSelectedDevice(dev)
    } else {
      setSelectedDevice(null)
    }
  }, [])

  // ─── Render ────────────────────────────────────────────────────────────

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* 3D Viewport */}
      <div className="flex-1 flex flex-col relative">
        {/* Toolbar */}
        <div className="h-10 flex items-center gap-3 px-4 border-b border-fn-border bg-fn-dark shrink-0">
          <span className="text-[11px] font-semibold text-white">Scene Preview</span>

          {/* Level selector */}
          {localLevels.length > 0 && (
            <select
              value={levelPath ?? ''}
              onChange={(e) => setLevelPath(e.target.value || null)}
              className="text-[10px] bg-fn-darker border border-fn-border rounded px-2 py-1 text-gray-300"
            >
              {localLevels.map((l) => (
                <option key={l.filePath} value={l.filePath}>{l.name}</option>
              ))}
            </select>
          )}

          <div className="flex-1" />

          {/* Stats */}
          <div className="flex items-center gap-3 text-[10px] text-gray-500">
            <span>{stats.withPos} positioned</span>
            <span>{stats.types} types</span>
            <span>{stats.total} total</span>
          </div>
        </div>

        {/* Viewport */}
        <div
          ref={containerRef}
          className="flex-1 relative cursor-crosshair"
          onMouseMove={handleMouseMove}
          onClick={handleClick}
        >
          {/* Loading overlay */}
          {loading && (
            <div className="absolute inset-0 flex items-center justify-center bg-black/50 z-10">
              <div className="text-center">
                <div className="w-6 h-6 border-2 border-blue-400/30 border-t-blue-400 rounded-full animate-spin mx-auto mb-2" />
                <div className="text-[11px] text-gray-300">Scanning level...</div>
              </div>
            </div>
          )}

          {/* Error overlay */}
          {error && (
            <div className="absolute top-4 left-4 right-4 z-10">
              <ErrorMessage message={error} />
            </div>
          )}

          {/* No data */}
          {!loading && !error && devices.length === 0 && levelPath && (
            <div className="absolute inset-0 flex items-center justify-center z-10">
              <div className="text-center text-gray-500">
                <div className="text-[13px] mb-1">No devices found</div>
                <div className="text-[10px]">Select a level with placed devices</div>
              </div>
            </div>
          )}

          {/* Hover label */}
          {hoveredDevice && (
            <div
              ref={labelRef}
              className="absolute z-20 pointer-events-none px-2 py-1 rounded bg-black/80 border border-fn-border text-[10px] text-white whitespace-nowrap"
            >
              <div className="font-semibold">{hoveredDevice.name}</div>
              <div className="text-gray-400">{hoveredDevice.deviceType}</div>
              {hoveredDevice.position && (
                <div className="text-gray-600 font-mono text-[9px]">
                  {hoveredDevice.position.x.toFixed(0)}, {hoveredDevice.position.y.toFixed(0)}, {hoveredDevice.position.z.toFixed(0)}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Right Panel — Device Inspector */}
      <div className="w-[280px] border-l border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden">
        <div className="px-3 py-2 border-b border-fn-border">
          <div className="text-[11px] font-semibold text-white">Device Inspector</div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {selectedDevice ? (
            <div className="p-3 space-y-3">
              {/* Device header */}
              <div>
                <div className="text-[12px] font-semibold text-white">{selectedDevice.name}</div>
                <div className="flex items-center gap-2 mt-1">
                  <span
                    className="inline-block w-2.5 h-2.5 rounded-full"
                    style={{ backgroundColor: '#' + getDeviceColor(selectedDevice.deviceType).toString(16).padStart(6, '0') }}
                  />
                  <span className="text-[10px] text-gray-400">{selectedDevice.deviceType}</span>
                </div>
              </div>

              {/* Position */}
              {selectedDevice.position && (
                <div>
                  <div className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-1">Position</div>
                  <div className="grid grid-cols-3 gap-1 text-[10px] font-mono">
                    <div className="bg-fn-darker rounded px-2 py-1">
                      <span className="text-red-400">X</span> <span className="text-gray-300">{selectedDevice.position.x.toFixed(1)}</span>
                    </div>
                    <div className="bg-fn-darker rounded px-2 py-1">
                      <span className="text-green-400">Y</span> <span className="text-gray-300">{selectedDevice.position.y.toFixed(1)}</span>
                    </div>
                    <div className="bg-fn-darker rounded px-2 py-1">
                      <span className="text-blue-400">Z</span> <span className="text-gray-300">{selectedDevice.position.z.toFixed(1)}</span>
                    </div>
                  </div>
                </div>
              )}

              {/* Properties from inspect */}
              {inspectLoading && (
                <div className="flex items-center gap-2 text-[10px] text-gray-500">
                  <div className="w-3 h-3 border border-gray-600 border-t-gray-300 rounded-full animate-spin" />
                  Loading properties...
                </div>
              )}

              {inspectResult && (inspectResult.properties?.length ?? 0) > 0 && (
                <div>
                  <div className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-1">
                    Properties ({inspectResult.properties.length})
                  </div>
                  <div className="space-y-0.5">
                    {inspectResult.properties.slice(0, 30).map((prop, i) => (
                      <div key={i} className="flex items-start gap-1 text-[10px] bg-fn-darker rounded px-2 py-1">
                        <span className="text-gray-500 shrink-0">{prop.name}</span>
                        <span className="text-gray-300 break-all ml-auto text-right">{prop.value}</span>
                      </div>
                    ))}
                    {inspectResult.properties.length > 30 && (
                      <div className="text-[9px] text-gray-600 text-center py-1">
                        +{inspectResult.properties.length - 30} more
                      </div>
                    )}
                  </div>
                </div>
              )}

              {/* File path */}
              <div>
                <div className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-1">File</div>
                <div className="text-[9px] text-gray-600 font-mono break-all bg-fn-darker rounded px-2 py-1">
                  {selectedDevice.filePath}
                </div>
              </div>
            </div>
          ) : (
            <div className="flex-1 flex items-center justify-center p-6 text-center">
              <div>
                <svg className="w-8 h-8 mx-auto mb-2 text-gray-700" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
                  <path d="M15 15l-2 5L9 9l11 4-5 2zm0 0l5 5M7.188 2.239l.777 2.897M5.136 7.965l-2.898-.777M13.95 4.05l-2.122 2.122m-5.657 5.656l-2.12 2.122" />
                </svg>
                <div className="text-[11px] text-gray-600">Click a device to inspect</div>
                <div className="text-[9px] text-gray-700 mt-1">Scroll to zoom, drag to orbit</div>
              </div>
            </div>
          )}
        </div>

        {/* Legend */}
        <div className="border-t border-fn-border px-3 py-2 max-h-32 overflow-y-auto">
          <div className="text-[9px] font-semibold text-gray-600 uppercase tracking-wider mb-1">Types</div>
          <div className="flex flex-wrap gap-1">
            {Array.from(new Set(devices.map((d) => d.deviceType))).sort().map((type) => (
              <div key={type} className="flex items-center gap-1 text-[9px] text-gray-500">
                <span
                  className="inline-block w-2 h-2 rounded-sm"
                  style={{ backgroundColor: '#' + getDeviceColor(type).toString(16).padStart(6, '0') }}
                />
                <span>{type.replace('Device', '').replace(/([A-Z])/g, ' $1').trim()}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
