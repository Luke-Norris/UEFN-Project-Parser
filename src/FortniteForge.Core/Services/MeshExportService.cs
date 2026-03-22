using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Meshes;
using Microsoft.Extensions.Logging;

namespace FortniteForge.Core.Services;

/// <summary>
/// Exports device meshes as GLB from Fortnite PAK files via CUE4Parse.
/// Read-only — never modifies Fortnite installation. Cache at Z:/UEFN_Resources/mesh-cache/.
/// </summary>
public class MeshExportService
{
    private readonly AssetPreviewService _previewService;
    private readonly ILogger<MeshExportService> _logger;
    private readonly string _cacheDir;

    // In-memory cache of resolved device class → PAK mesh path
    private readonly Dictionary<string, string?> _resolvedPaths = new();

    public MeshExportService(AssetPreviewService previewService, ILogger<MeshExportService> logger,
        string cacheDirectory = @"Z:\UEFN_Resources\mesh-cache")
    {
        _previewService = previewService;
        _logger = logger;
        _cacheDir = cacheDirectory;
    }

    /// <summary>
    /// Get or export a GLB mesh for a device class. Checks disk cache first.
    /// </summary>
    public MeshExportResult? GetOrExportMesh(string deviceClassName)
    {
        if (!_previewService.IsInitialized)
        {
            _logger.LogDebug("CUE4Parse not initialized, cannot export mesh for {Class}", deviceClassName);
            return null;
        }

        try
        {
            // Resolve device class → PAK asset path
            var meshPath = ResolveDeviceMesh(deviceClassName);
            if (meshPath == null)
            {
                _logger.LogDebug("No mesh found for device class: {Class}", deviceClassName);
                return null;
            }

            // Check disk cache
            var cacheKey = GetCacheKey(meshPath);
            var cachePath = Path.Combine(_cacheDir, $"{cacheKey}.glb");

            if (File.Exists(cachePath))
            {
                var cachedBytes = File.ReadAllBytes(cachePath);
                _logger.LogDebug("Cache hit for {Class} at {Path}", deviceClassName, cachePath);
                return new MeshExportResult
                {
                    DeviceClass = deviceClassName,
                    GlbData = cachedBytes,
                    VertexCount = 0, // Not tracked for cached
                    Cached = true,
                    AssetPath = meshPath,
                };
            }

            // Export from PAK
            var glbBytes = ExportMeshGlb(meshPath);
            if (glbBytes == null || glbBytes.Length == 0)
            {
                _logger.LogDebug("Export returned no data for {Path}", meshPath);
                return null;
            }

            // Write to disk cache
            EnsureCacheDirectory();
            File.WriteAllBytes(cachePath, glbBytes);
            _logger.LogInformation("Exported and cached mesh for {Class}: {Bytes} bytes at {Path}",
                deviceClassName, glbBytes.Length, cachePath);

            return new MeshExportResult
            {
                DeviceClass = deviceClassName,
                GlbData = glbBytes,
                VertexCount = 0,
                Cached = false,
                AssetPath = meshPath,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export mesh for {Class}", deviceClassName);
            return null;
        }
    }

    /// <summary>
    /// Export a UStaticMesh from PAK as GLB bytes.
    /// </summary>
    public byte[]? ExportMeshGlb(string pakAssetPath)
    {
        if (!_previewService.IsInitialized) return null;

        try
        {
            // Use AssetPreviewService's provider to load the mesh
            // We need to access the provider — add a method or use reflection
            var meshInfo = _previewService.GetMeshInfo(pakAssetPath);
            if (meshInfo == null)
            {
                _logger.LogDebug("Could not load mesh info for: {Path}", pakAssetPath);
                return null;
            }

            // Load the actual UStaticMesh for export
            var staticMesh = _previewService.LoadStaticMesh(pakAssetPath);
            if (staticMesh == null)
            {
                _logger.LogDebug("Could not load UStaticMesh for: {Path}", pakAssetPath);
                return null;
            }

            var options = new ExporterOptions
            {
                MeshFormat = EMeshFormat.Gltf2,
                LodFormat = ELodFormat.FirstLod,
                ExportMaterials = false, // Skip materials for now — geometry only
            };

            var exporter = new MeshExporter(staticMesh, options);
            if (exporter.MeshLods.Count == 0)
            {
                _logger.LogDebug("MeshExporter produced no LODs for: {Path}", pakAssetPath);
                return null;
            }

            return exporter.MeshLods[0].FileData;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export GLB for: {Path}", pakAssetPath);
            return null;
        }
    }

    /// <summary>
    /// Resolve a device class name to a PAK mesh asset path.
    /// Tries PAK search first, then falls back to static mapping.
    /// </summary>
    public string? ResolveDeviceMesh(string deviceClassName)
    {
        if (_resolvedPaths.TryGetValue(deviceClassName, out var cached))
            return cached;

        // Try searching PAK files for the device blueprint
        var resolved = SearchPakForDeviceMesh(deviceClassName);

        // Fall back to static mapping
        resolved ??= GetStaticMeshMapping(deviceClassName);

        _resolvedPaths[deviceClassName] = resolved;
        return resolved;
    }

    private string? SearchPakForDeviceMesh(string deviceClassName)
    {
        if (!_previewService.IsInitialized) return null;

        try
        {
            // Strip common prefixes/suffixes to get search term
            var searchTerm = deviceClassName
                .Replace("_C", "")
                .Replace("BP_", "")
                .Replace("PBWA_", "")
                .Replace("B_", "");

            // Search for static mesh assets matching this device
            var results = _previewService.SearchAssets(searchTerm.ToLowerInvariant(), 20);

            // Look for StaticMesh assets (not Blueprints)
            foreach (var path in results)
            {
                if (path.Contains("/SM_", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("/S_", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("StaticMesh", StringComparison.OrdinalIgnoreCase))
                {
                    // Verify it's actually a mesh
                    var info = _previewService.GetMeshInfo(path);
                    if (info != null && info.VertexCount > 0)
                    {
                        _logger.LogDebug("PAK search resolved {Class} → {Path}", deviceClassName, path);
                        return path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PAK search failed for {Class}", deviceClassName);
        }

        return null;
    }

    /// <summary>
    /// Static mapping of common Creative device classes to their PAK mesh paths.
    /// These are the known paths for Epic's shipped devices — reliable fallback
    /// when PAK search doesn't find the right mesh.
    /// </summary>
    private static string? GetStaticMeshMapping(string deviceClassName)
    {
        // Strip common prefixes to normalize
        var normalized = deviceClassName
            .Replace("_C", "")
            .Replace("BP_", "")
            .Replace("PBWA_", "")
            .Replace("B_", "")
            .Replace("Device", "");

        // This mapping will be populated by running PAK searches and recording results.
        // For now, return null — PAK search is the primary resolution method.
        // As we discover paths, we add them here for instant resolution.
        return KnownDeviceMeshPaths.GetValueOrDefault(normalized);
    }

    // Known device → mesh path mappings (populated as we discover them)
    // Key: normalized device name (no prefix/suffix), Value: PAK asset path
    private static readonly Dictionary<string, string> KnownDeviceMeshPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // These will be populated by running the tool against real PAK files
        // and recording which paths resolve successfully.
        // Format: { "VendingMachine", "FortniteGame/Content/Athena/Items/Devices/VendingMachine/SM_VendingMachine" }
    };

    /// <summary>
    /// Clear the resolved paths cache (e.g. after reinitializing CUE4Parse).
    /// </summary>
    public void ClearCache()
    {
        _resolvedPaths.Clear();
    }

    private void EnsureCacheDirectory()
    {
        if (!Directory.Exists(_cacheDir))
        {
            Directory.CreateDirectory(_cacheDir);
            _logger.LogInformation("Created mesh cache directory: {Dir}", _cacheDir);
        }
    }

    private static string GetCacheKey(string assetPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(assetPath));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

public class MeshExportResult
{
    public string DeviceClass { get; set; } = "";
    public byte[] GlbData { get; set; } = Array.Empty<byte>();
    public int VertexCount { get; set; }
    public bool Cached { get; set; }
    public string AssetPath { get; set; } = "";
}
