import * as THREE from 'three'
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js'
import { previewExportMesh, previewExportMeshBatch } from './api'
import type { MeshExportResult } from './api'

// ─── Mesh cache for device GLB models ────────────────────────────────────────
// Caches loaded THREE.Group objects by device class name.
// Deduplicates in-flight requests so the same mesh isn't fetched twice.

const meshCache = new Map<string, THREE.Group>()
const pendingLoads = new Map<string, Promise<THREE.Group | null>>()
const failedClasses = new Set<string>()
const loader = new GLTFLoader()

export interface MeshLoadProgress {
  total: number
  loaded: number
  failed: number
}

/** Check if a mesh is already cached for this device class */
export function hasCachedMesh(deviceClass: string): boolean {
  return meshCache.has(deviceClass)
}

/** Get a cached mesh clone (returns null if not cached) */
export function getCachedMeshClone(deviceClass: string): THREE.Group | null {
  const group = meshCache.get(deviceClass)
  return group ? group.clone() : null
}

/** Load a single device mesh. Returns a cloned THREE.Group or null. */
export async function loadDeviceMesh(deviceClass: string): Promise<THREE.Group | null> {
  // Already cached
  if (meshCache.has(deviceClass)) {
    return meshCache.get(deviceClass)!.clone()
  }

  // Known failure — don't retry
  if (failedClasses.has(deviceClass)) return null

  // Deduplicate in-flight requests
  if (pendingLoads.has(deviceClass)) {
    const result = await pendingLoads.get(deviceClass)!
    return result ? result.clone() : null
  }

  const promise = fetchAndParseMesh(deviceClass)
  pendingLoads.set(deviceClass, promise)

  try {
    const result = await promise
    return result ? result.clone() : null
  } finally {
    pendingLoads.delete(deviceClass)
  }
}

/** Preload meshes for a batch of device classes. Reports progress via callback. */
export async function preloadMeshBatch(
  deviceClasses: string[],
  onProgress?: (progress: MeshLoadProgress) => void
): Promise<void> {
  // Deduplicate and filter already-cached/failed
  const unique = [...new Set(deviceClasses)].filter(
    (dc) => !meshCache.has(dc) && !failedClasses.has(dc)
  )

  if (unique.length === 0) {
    onProgress?.({ total: 0, loaded: 0, failed: 0 })
    return
  }

  const progress: MeshLoadProgress = { total: unique.length, loaded: 0, failed: 0 }
  onProgress?.(progress)

  try {
    const response = await previewExportMeshBatch(unique)

    // Parse each result in parallel
    const parsePromises = response.results.map(async (result) => {
      if (result.found && result.glbBase64) {
        try {
          await parseAndCacheGlb(result.deviceClass, result.glbBase64)
          progress.loaded++
        } catch {
          failedClasses.add(result.deviceClass)
          progress.failed++
        }
      } else {
        failedClasses.add(result.deviceClass)
        progress.failed++
      }
      onProgress?.({ ...progress })
    })

    await Promise.all(parsePromises)
  } catch {
    // Batch fetch failed entirely — mark all as failed
    unique.forEach((dc) => failedClasses.add(dc))
    progress.failed = unique.length
    onProgress?.({ ...progress })
  }
}

/** Clear all caches (e.g. when switching projects) */
export function clearMeshCache(): void {
  // Dispose all cached geometries and materials
  meshCache.forEach((group) => {
    group.traverse((child) => {
      if (child instanceof THREE.Mesh) {
        child.geometry?.dispose()
        if (Array.isArray(child.material)) {
          child.material.forEach((m) => m.dispose())
        } else {
          child.material?.dispose()
        }
      }
    })
  })
  meshCache.clear()
  failedClasses.clear()
}

// ─── Internal helpers ────────────────────────────────────────────────────────

async function fetchAndParseMesh(deviceClass: string): Promise<THREE.Group | null> {
  try {
    const result: MeshExportResult = await previewExportMesh(deviceClass)
    if (!result.found || !result.glbBase64) {
      failedClasses.add(deviceClass)
      return null
    }
    return await parseAndCacheGlb(deviceClass, result.glbBase64)
  } catch {
    failedClasses.add(deviceClass)
    return null
  }
}

async function parseAndCacheGlb(deviceClass: string, base64: string): Promise<THREE.Group> {
  const binary = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
  const buffer = binary.buffer

  const gltf = await loader.parseAsync(buffer, '')
  const group = gltf.scene

  // Normalize: center the mesh and scale to reasonable size
  const box = new THREE.Box3().setFromObject(group)
  const center = box.getCenter(new THREE.Vector3())
  group.position.sub(center)

  // Store the template (we clone from this on each use)
  meshCache.set(deviceClass, group)
  return group
}
