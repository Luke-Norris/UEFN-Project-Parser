import { useEffect, useRef, useState, useCallback, useMemo } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import { ErrorMessage } from '../components/ErrorMessage'
import { ResizeHandle } from '../components/ResizeHandle'
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
/** Read a CSS variable as a THREE.Color hex number */
function cssColorToHex(varName: string, fallback: string): number {
  const val = getComputedStyle(document.documentElement).getPropertyValue(varName).trim() || fallback
  return parseInt(val.replace('#', ''), 16)
}

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

// Frontend cache for device scan results
const deviceCache = new Map<string, { devices: DeviceEntry[]; fetchedAt: number }>()
const DEVICE_CACHE_TTL = 5 * 60 * 1000 // 5 minutes

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
  const keysRef = useRef<Set<string>>(new Set())
  const flyingRef = useRef(false)
  const flySpeedRef = useRef(2)

  const [devices, setDevices] = useState<DeviceEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hoveredDevice, setHoveredDevice] = useState<DeviceEntry | null>(null)
  const [selectedDevice, setSelectedDevice] = useState<DeviceEntry | null>(null)
  const [inspectResult, setInspectResult] = useState<DeviceInspectResult | null>(null)
  const [inspectLoading, setInspectLoading] = useState(false)
  const [stats, setStats] = useState({ total: 0, withPos: 0, types: 0 })
  const [levelPath, setLevelPath] = useState<string | null>(selectedLevel ?? null)
  const [inspectorWidth, setInspectorWidth] = useState(400)
  const [inspectorCollapsed, setInspectorCollapsed] = useState(false)
  const [showGrid, setShowGrid] = useState(true)
  const [showFog, setShowFog] = useState(true)
  const [showHierarchy, setShowHierarchy] = useState(true)
  const [hierarchyWidth, setHierarchyWidth] = useState(260)
  const [hierarchySearch, setHierarchySearch] = useState('')
  const [flySpeed, setFlySpeed] = useState(2)
  const [fov, setFov] = useState(60)

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

  // Fetch devices when level changes — with frontend cache
  useEffect(() => {
    if (!levelPath) return

    // Check frontend cache first
    const cached = deviceCache.get(levelPath)
    if (cached && Date.now() - cached.fetchedAt < DEVICE_CACHE_TTL) {
      setDevices(cached.devices)
      const withPos = cached.devices.filter((d) => d.position)
      const types = new Set(cached.devices.map((d) => d.deviceType))
      setStats({ total: cached.devices.length, withPos: withPos.length, types: types.size })
      return
    }

    setLoading(true)
    setError(null)
    setSelectedDevice(null)
    setInspectResult(null)

    window.electronAPI.forgeListDevices(levelPath)
      .then((result) => {
        const devs = result?.devices ?? []
        setDevices(devs)
        deviceCache.set(levelPath, { devices: devs, fetchedAt: Date.now() })
        const withPos = devs.filter((d: any) => d.position)
        const types = new Set(devs.map((d: any) => d.deviceType))
        setStats({ total: devs.length, withPos: withPos.length, types: types.size })
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : String(err))
      })
      .finally(() => setLoading(false))
  }, [levelPath])

  // Sync camera settings
  useEffect(() => { flySpeedRef.current = flySpeed }, [flySpeed])
  useEffect(() => {
    const cam = cameraRef.current
    if (cam) { cam.fov = fov; cam.updateProjectionMatrix() }
  }, [fov])
  useEffect(() => {
    const scene = sceneRef.current
    if (!scene) return
    scene.children.forEach((child) => {
      if (child instanceof THREE.GridHelper) child.visible = showGrid
    })
  }, [showGrid])
  useEffect(() => {
    const scene = sceneRef.current
    if (!scene) return
    if (showFog) {
      const bgColor = cssColorToHex('--fn-viewport', '#08081a')
      scene.fog = new THREE.Fog(bgColor, 500, 2000)
    } else {
      scene.fog = null
    }
  }, [showFog])

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

    // Scene — use theme colors
    const bgColor = cssColorToHex('--fn-viewport', '#08081a')
    const gridColor = cssColorToHex('--fn-grid', '#1a1a3a')
    const scene = new THREE.Scene()
    scene.background = new THREE.Color(bgColor)
    scene.fog = new THREE.Fog(bgColor, 500, 2000)
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

    // Controls — orbit by default, fly with right-click + WASD
    const controls = new OrbitControls(camera, renderer.domElement)
    controls.enableDamping = true
    controls.dampingFactor = 0.1
    controls.maxDistance = 2000
    controls.minDistance = 5
    controls.mouseButtons = { LEFT: THREE.MOUSE.LEFT, MIDDLE: THREE.MOUSE.MIDDLE, RIGHT: THREE.MOUSE.RIGHT }
    controlsRef.current = controls

    // Right-click fly mode with pointer lock
    const onContextMenu = (e: Event) => e.preventDefault()
    renderer.domElement.addEventListener('contextmenu', onContextMenu)

    const onMouseDown = (e: MouseEvent) => {
      if (e.button === 2) {
        flyingRef.current = true
        controls.enabled = false
        renderer.domElement.requestPointerLock()
      }
    }
    const onMouseUp = (e: MouseEvent) => {
      if (e.button === 2) {
        flyingRef.current = false
        controls.enabled = true
        if (document.pointerLockElement) document.exitPointerLock()
      }
    }
    const onMouseMoveFly = (e: MouseEvent) => {
      if (!flyingRef.current) return
      const euler = new THREE.Euler(0, 0, 0, 'YXZ')
      euler.setFromQuaternion(camera.quaternion)
      euler.y -= e.movementX * 0.002
      euler.x -= e.movementY * 0.002
      euler.x = Math.max(-Math.PI / 2, Math.min(Math.PI / 2, euler.x))
      camera.quaternion.setFromEuler(euler)
    }
    renderer.domElement.addEventListener('mousedown', onMouseDown)
    renderer.domElement.addEventListener('mouseup', onMouseUp)
    document.addEventListener('mousemove', onMouseMoveFly)

    // Release pointer lock if lost focus
    const onPointerLockChange = () => {
      if (!document.pointerLockElement && flyingRef.current) {
        flyingRef.current = false
        controls.enabled = true
      }
    }
    document.addEventListener('pointerlockchange', onPointerLockChange)

    const onKeyDown = (e: KeyboardEvent) => {
      keysRef.current.add(e.key.toLowerCase())
      // F key — focus selected device
      if (e.key.toLowerCase() === 'f') {
        const selected = deviceMeshesRef.current.find((m) => {
          const mat = m.material as THREE.MeshLambertMaterial
          return mat.color.getHex() === SELECTED_COLOR
        })
        if (selected) {
          const pos = selected.position.clone()
          const dist = 20
          const dir = new THREE.Vector3()
          camera.getWorldDirection(dir)
          controls.target.copy(pos)
          camera.position.copy(pos).addScaledVector(dir, -dist)
          controls.update()
        }
      }
    }
    const onKeyUp = (e: KeyboardEvent) => keysRef.current.delete(e.key.toLowerCase())
    document.addEventListener('keydown', onKeyDown)
    document.addEventListener('keyup', onKeyUp)

    // Right-click + scroll = adjust fly speed
    const onWheel = (e: WheelEvent) => {
      if (flyingRef.current) {
        e.preventDefault()
        const delta = e.deltaY > 0 ? -0.5 : 0.5
        flySpeedRef.current = Math.max(0.5, Math.min(20, flySpeedRef.current + delta))
      }
    }
    renderer.domElement.addEventListener('wheel', onWheel, { passive: false })

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
    const gridHelper = new THREE.GridHelper(1000, 100, gridColor, gridColor)
    gridHelper.material.opacity = 0.3
    gridHelper.material.transparent = true
    scene.add(gridHelper)

    // Ground plane (subtle)
    const groundGeo = new THREE.PlaneGeometry(2000, 2000)
    const groundMat = new THREE.MeshLambertMaterial({ color: bgColor, transparent: true, opacity: 0.5 })
    const ground = new THREE.Mesh(groundGeo, groundMat)
    ground.rotation.x = -Math.PI / 2
    ground.position.y = -0.1
    scene.add(ground)

    // Animation loop with WASD fly
    function animate() {
      animFrameRef.current = requestAnimationFrame(animate)

      // WASD fly movement when right-click held
      if (flyingRef.current) {
        const speed = flySpeedRef.current
        const keys = keysRef.current
        const forward = new THREE.Vector3()
        camera.getWorldDirection(forward)
        const right = new THREE.Vector3().crossVectors(forward, camera.up).normalize()

        if (keys.has('w')) camera.position.addScaledVector(forward, speed)
        if (keys.has('s')) camera.position.addScaledVector(forward, -speed)
        if (keys.has('a')) camera.position.addScaledVector(right, -speed)
        if (keys.has('d')) camera.position.addScaledVector(right, speed)
        if (keys.has(' ') || keys.has('e')) camera.position.y += speed
        if (keys.has('shift') || keys.has('q')) camera.position.y -= speed

        controls.target.copy(camera.position).addScaledVector(forward, 10)
      }

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
      renderer.domElement.removeEventListener('contextmenu', onContextMenu)
      renderer.domElement.removeEventListener('mousedown', onMouseDown)
      renderer.domElement.removeEventListener('mouseup', onMouseUp)
      document.removeEventListener('mousemove', onMouseMoveFly)
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('keyup', onKeyUp)
      document.removeEventListener('pointerlockchange', onPointerLockChange)
      if (document.pointerLockElement) document.exitPointerLock()
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

  // Focus on a device
  const focusDevice = useCallback((device: DeviceEntry) => {
    const mesh = deviceMeshesRef.current.find((m) => m.userData.device === device)
    if (!mesh) return
    const cam = cameraRef.current
    const ctrl = controlsRef.current
    if (!cam || !ctrl) return
    const pos = mesh.position.clone()
    ctrl.target.copy(pos)
    cam.position.set(pos.x + 15, pos.y + 10, pos.z + 15)
    ctrl.update()
  }, [])

  // Select device from hierarchy
  const selectDeviceFromHierarchy = useCallback((device: DeviceEntry) => {
    // Deselect all
    for (const mesh of deviceMeshesRef.current) {
      const mat = mesh.material as THREE.MeshLambertMaterial
      const dev = mesh.userData.device as DeviceEntry
      mat.color.setHex(getDeviceColor(dev.deviceType))
      mat.emissive.setHex(0x000000)
      mesh.scale.set(1, 1, 1)
    }
    // Select this one
    const mesh = deviceMeshesRef.current.find((m) => m.userData.device === device)
    if (mesh) {
      const mat = mesh.material as THREE.MeshLambertMaterial
      mat.color.setHex(SELECTED_COLOR)
      mat.emissive.setHex(0x003322)
      mesh.scale.set(1.3, 1.3, 1.3)
    }
    setSelectedDevice(device)
  }, [])

  // Filtered hierarchy list
  const hierarchyDevices = useMemo(() => {
    if (!hierarchySearch.trim()) return devices
    const q = hierarchySearch.toLowerCase()
    return devices.filter((d) =>
      d.name.toLowerCase().includes(q) || d.deviceType.toLowerCase().includes(q)
    )
  }, [devices, hierarchySearch])

  // Collapse state for hierarchy groups
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(new Set())

  // Clean device display name
  const cleanDeviceName = useCallback((dev: DeviceEntry, index: number) => {
    let name = dev.name || ''

    // If it has UAID hash, strip it to get a cleaner base name
    if (name.includes('_UAID_')) {
      name = name.split('_UAID_')[0]
    }

    // If it's still a raw class name (ends with _C), clean it up
    if (name.endsWith('_C') || name.includes('_C_')) {
      name = name.replace(/_C$/, '').replace(/_C_.*/, '')
    }

    // Clean up common prefixes/suffixes
    name = name
      .replace(/^BP_|^PBWA_|^B_|^Device_/, '')
      .replace(/_V\d+$/, '')
      .replace(/_Placed$/, '')
      .replace(/_/g, ' ')
      .trim()

    if (!name || name.length < 2) {
      name = (dev.deviceType || 'Object').replace(/_/g, ' ').replace(/V\d+$/, '').trim()
    }

    return `${name} ${index + 1}`
  }, [])

  // Group hierarchy by type
  const hierarchyGroups = useMemo(() => {
    const groups = new Map<string, Array<DeviceEntry & { _displayName: string }>>()
    for (const d of hierarchyDevices) {
      const type = d.deviceType || 'Unknown'
      if (!groups.has(type)) groups.set(type, [])
      const list = groups.get(type)!
      const displayName = cleanDeviceName(d, list.length)
      list.push({ ...d, _displayName: displayName })
    }
    return Array.from(groups.entries()).sort((a, b) => b[1].length - a[1].length)
  }, [hierarchyDevices, cleanDeviceName])

  // ─── Render ────────────────────────────────────────────────────────────

  return (
    <div className="flex-1 flex bg-fn-darker overflow-hidden">
      {/* Hierarchy Panel */}
      {showHierarchy && devices.length > 0 && (
        <>
          <div className="flex flex-col border-r border-fn-border bg-fn-dark shrink-0 overflow-hidden" style={{ width: hierarchyWidth }}>
            <div className="px-2 py-2 border-b border-fn-border shrink-0">
              <input
                type="text"
                value={hierarchySearch}
                onChange={(e) => setHierarchySearch(e.target.value)}
                placeholder="Filter objects..."
                className="w-full bg-fn-darker border border-fn-border rounded px-2 py-1 text-[10px] text-white placeholder-gray-600 focus:outline-none focus:border-blue-500/50"
              />
            </div>
            <div className="flex-1 overflow-y-auto min-h-0">
              {hierarchyGroups.map(([type, devs]) => {
                const isCollapsed = collapsedGroups.has(type)
                const prettyType = type.replace(/_/g, ' ').replace(/V\d+$/, '').replace(/Placed$/, '').trim()
                return (
                  <div key={type}>
                    <button
                      onClick={() => setCollapsedGroups((prev) => {
                        const next = new Set(prev)
                        if (next.has(type)) next.delete(type)
                        else next.add(type)
                        return next
                      })}
                      className="w-full flex items-center gap-1.5 px-2 py-1.5 text-[10px] font-medium text-gray-400 hover:text-white hover:bg-white/[0.03] transition-colors sticky top-0 bg-fn-dark z-10"
                    >
                      <svg className={`w-3 h-3 shrink-0 transition-transform ${isCollapsed ? '' : 'rotate-90'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path d="M9 5l7 7-7 7" />
                      </svg>
                      <span
                        className="w-2.5 h-2.5 rounded-sm shrink-0"
                        style={{ backgroundColor: '#' + getDeviceColor(type).toString(16).padStart(6, '0') }}
                      />
                      <span className="truncate flex-1 text-left">{prettyType}</span>
                      <span className="text-gray-600 text-[9px] shrink-0">{devs.length}</span>
                    </button>
                    {!isCollapsed && devs.map((dev) => (
                      <button
                        key={dev.filePath || dev.name}
                        onClick={() => selectDeviceFromHierarchy(dev)}
                        onDoubleClick={() => { selectDeviceFromHierarchy(dev); focusDevice(dev) }}
                        className={`w-full text-left pl-7 pr-2 py-1 text-[10px] truncate transition-colors ${
                          selectedDevice?.filePath === dev.filePath
                            ? 'text-white bg-blue-500/15 border-l-2 border-blue-400'
                            : 'text-gray-500 hover:text-gray-300 hover:bg-white/[0.03]'
                        }`}
                        title={`${dev._displayName} — double-click to focus (F)`}
                      >
                        {dev._displayName}
                      </button>
                    ))}
                  </div>
                )
              })}
            </div>
            <div className="px-2 py-1.5 border-t border-fn-border text-[9px] text-gray-600 shrink-0">
              {hierarchyDevices.length} / {devices.length} objects
            </div>
          </div>
          <ResizeHandle direction="horizontal" onResize={(delta) => setHierarchyWidth((w) => Math.max(180, Math.min(500, w + delta)))} />
        </>
      )}

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

        {/* Camera settings bar */}
        <div className="h-8 flex items-center gap-4 px-4 border-b border-fn-border/50 bg-fn-darker/80 shrink-0 text-[10px]">
          {/* FOV */}
          <div className="flex items-center gap-1.5 text-gray-500">
            <span>FOV</span>
            <input
              type="range" min={30} max={120} step={5} value={fov}
              onChange={(e) => setFov(Number(e.target.value))}
              className="w-16 accent-blue-500"
            />
            <span className="w-6 text-center text-gray-400 font-mono">{fov}</span>
          </div>

          {/* Fly Speed */}
          <div className="flex items-center gap-1.5 text-gray-500">
            <span>Speed</span>
            <input
              type="range" min={0.5} max={20} step={0.5} value={flySpeed}
              onChange={(e) => setFlySpeed(Number(e.target.value))}
              className="w-16 accent-blue-500"
            />
            <span className="w-6 text-center text-gray-400 font-mono">{flySpeed}</span>
          </div>

          <div className="w-px h-4 bg-fn-border" />

          {/* Hierarchy toggle */}
          <button
            onClick={() => setShowHierarchy(!showHierarchy)}
            className={`px-1.5 py-0.5 rounded transition-colors ${showHierarchy ? 'text-white bg-white/10' : 'text-gray-600 hover:text-gray-400'}`}
          >
            Hierarchy
          </button>

          {/* Grid toggle */}
          <button
            onClick={() => setShowGrid(!showGrid)}
            className={`px-1.5 py-0.5 rounded transition-colors ${showGrid ? 'text-white bg-white/10' : 'text-gray-600 hover:text-gray-400'}`}
          >
            Grid
          </button>

          {/* Fog toggle */}
          <button
            onClick={() => setShowFog(!showFog)}
            className={`px-1.5 py-0.5 rounded transition-colors ${showFog ? 'text-white bg-white/10' : 'text-gray-600 hover:text-gray-400'}`}
          >
            Fog
          </button>

          <div className="w-px h-4 bg-fn-border" />

          {/* Reset camera */}
          <button
            onClick={() => {
              const cam = cameraRef.current
              const ctrl = controlsRef.current
              if (cam && ctrl && deviceMeshesRef.current.length > 0) {
                const box = new THREE.Box3()
                for (const m of deviceMeshesRef.current) box.expandByObject(m)
                const center = box.getCenter(new THREE.Vector3())
                const size = box.getSize(new THREE.Vector3())
                const dist = Math.max(Math.max(size.x, size.y, size.z) * 1.5, 50)
                ctrl.target.copy(center)
                cam.position.set(center.x + dist * 0.5, center.y + dist * 0.7, center.z + dist * 0.5)
                cam.quaternion.identity()
                ctrl.update()
              }
            }}
            className="px-1.5 py-0.5 rounded text-gray-500 hover:text-white hover:bg-white/10 transition-colors"
          >
            Reset View
          </button>
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

      {/* Right Panel — Device Inspector (only shows when device selected) */}
      {!selectedDevice ? null : inspectorCollapsed ? (
        <div className="w-10 border-l border-fn-border bg-fn-dark flex flex-col items-center shrink-0">
          <button
            onClick={() => setInspectorCollapsed(false)}
            className="w-10 h-10 flex items-center justify-center text-gray-500 hover:text-white hover:bg-white/[0.05] transition-colors border-b border-fn-border"
            title="Expand inspector"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M11 19l-7-7 7-7" />
            </svg>
          </button>
          <div className="flex-1 flex items-center justify-center">
            <span className="text-[9px] text-gray-600 [writing-mode:vertical-lr] rotate-180">Inspector</span>
          </div>
        </div>
      ) : (
        <>
          <ResizeHandle
            direction="horizontal"
            onResize={(delta) => setInspectorWidth((w) => Math.max(250, Math.min(800, w - delta)))}
          />
          <div className="border-l border-fn-border bg-fn-dark flex flex-col shrink-0 overflow-hidden" style={{ width: inspectorWidth }}>
            <div className="h-10 flex items-center px-3 border-b border-fn-border shrink-0">
              <span className="text-[11px] font-semibold text-white flex-1">Device Inspector</span>
              <button
                onClick={() => setInspectorCollapsed(true)}
                className="shrink-0 p-1 rounded text-gray-600 hover:text-gray-300 hover:bg-white/[0.05] transition-colors"
                title="Collapse inspector"
              >
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path d="M13 5l7 7-7 7" />
                </svg>
              </button>
            </div>

            <div className="flex-1 overflow-y-auto min-h-0">
              {selectedDevice ? (
                <div className="p-3 space-y-3">
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
                        {inspectResult.properties.map((prop, i) => (
                          <div key={i} className="flex items-start gap-2 text-[10px] bg-fn-darker rounded px-2 py-1">
                            <span className="text-gray-500 shrink-0">{prop.name}</span>
                            <span className="text-gray-300 break-all ml-auto text-right">{prop.value}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

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
                    <div className="text-[9px] text-gray-700 mt-2 leading-relaxed">
                      Left-drag to orbit<br />
                      Right-click + WASD to fly<br />
                      Scroll to zoom<br />
                      F to focus selected
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
