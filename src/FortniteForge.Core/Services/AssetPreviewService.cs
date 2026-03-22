using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Textures;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace FortniteForge.Core.Services;

/// <summary>
/// Extracts asset previews (textures, mesh data) from Fortnite game files using CUE4Parse.
/// Requires Fortnite to be installed locally.
/// </summary>
public class AssetPreviewService : IDisposable
{
    private readonly ILogger<AssetPreviewService> _logger;
    private DefaultFileProvider? _provider;
    private bool _initialized;
    private string? _gamePath;

    public AssetPreviewService(ILogger<AssetPreviewService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Whether the service has been initialized with a valid Fortnite installation.
    /// </summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// The path to the Fortnite game installation being used.
    /// </summary>
    public string? GamePath => _gamePath;

    /// <summary>
    /// Initialize the file provider with a Fortnite installation path.
    /// </summary>
    public async Task<bool> InitializeAsync(string fortnitePath)
    {
        try
        {
            // Look for the Content/Paks directory
            var paksDir = FindPaksDirectory(fortnitePath);
            if (paksDir == null)
            {
                _logger.LogWarning("Could not find Fortnite Paks directory at: {Path}", fortnitePath);
                return false;
            }

            _provider = new DefaultFileProvider(paksDir, SearchOption.TopDirectoryOnly,
                isCaseInsensitive: true,
                versions: new VersionContainer(EGame.GAME_UE5_4));

            _provider.Initialize();

            // Submit encryption key (Fortnite's main AES key is publicly known)
            await _provider.SubmitKeyAsync(new FGuid(), new CUE4Parse.Encryption.Aes.FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

            _gamePath = fortnitePath;
            _initialized = true;

            var fileCount = _provider.Files.Count;
            _logger.LogInformation("CUE4Parse initialized: {FileCount} files from {Path}", fileCount, paksDir);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CUE4Parse with path: {Path}", fortnitePath);
            return false;
        }
    }

    /// <summary>
    /// Extract a texture as PNG bytes.
    /// </summary>
    public byte[]? ExtractTexture(string assetPath)
    {
        if (!_initialized || _provider == null) return null;

        try
        {
            var package = _provider.LoadPackage(assetPath);
            var obj = package?.GetExports()?.FirstOrDefault();
            if (obj is UTexture2D texture)
            {
                var bitmap = texture.Decode();
                if (bitmap != null)
                {
                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
                    return data.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract texture: {Path}", assetPath);
        }

        return null;
    }

    /// <summary>
    /// Load a UStaticMesh object for export. Returns null if not found or not a static mesh.
    /// </summary>
    public UStaticMesh? LoadStaticMesh(string assetPath)
    {
        if (!_initialized || _provider == null) return null;

        try
        {
            var package = _provider.LoadPackage(assetPath);
            var obj = package?.GetExports()?.FirstOrDefault();
            return obj as UStaticMesh;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load static mesh: {Path}", assetPath);
            return null;
        }
    }

    /// <summary>
    /// Get mesh vertex count and basic info without full export.
    /// </summary>
    public MeshPreviewInfo? GetMeshInfo(string assetPath)
    {
        if (!_initialized || _provider == null) return null;

        try
        {
            var package = _provider.LoadPackage(assetPath);
            var obj = package?.GetExports()?.FirstOrDefault();
            if (obj is UStaticMesh mesh)
            {
                var lods = mesh.RenderData?.LODs;
                var lod0 = lods != null && lods.Length > 0 ? lods[0] : null;
                var vertCount = 0;
                var triCount = 0;
                if (lod0 != null)
                {
                    try { vertCount = lod0.PositionVertexBuffer?.Verts?.Length ?? 0; } catch { }
                    try { triCount = (lod0.IndexBuffer?.Indices32?.Length ?? 0) / 3; } catch { }
                }
                return new MeshPreviewInfo
                {
                    VertexCount = vertCount,
                    TriangleCount = triCount,
                    LODCount = lods?.Length ?? 0,
                    MaterialCount = mesh.StaticMaterials?.Length ?? 0,
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get mesh info: {Path}", assetPath);
        }

        return null;
    }

    /// <summary>
    /// Search for assets by name pattern.
    /// </summary>
    public List<string> SearchAssets(string query, int limit = 50)
    {
        if (!_initialized || _provider == null) return new();

        var results = new List<string>();
        var q = query.ToLowerInvariant();

        foreach (var (path, _) in _provider.Files)
        {
            if (path.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(path);
                if (results.Count >= limit) break;
            }
        }

        return results;
    }

    /// <summary>
    /// Get the total number of files in the loaded archives.
    /// </summary>
    public int GetFileCount()
    {
        return _provider?.Files.Count ?? 0;
    }

    /// <summary>
    /// List asset classes available for a given device type path.
    /// </summary>
    public DeviceAssetInfo? GetDeviceAssetInfo(string deviceClassName)
    {
        if (!_initialized || _provider == null) return null;

        try
        {
            // Search for the device blueprint
            var searchName = deviceClassName.Replace("_C", "").ToLowerInvariant();
            var matches = _provider.Files
                .Where(f => f.Key.Contains(searchName, StringComparison.OrdinalIgnoreCase)
                         && f.Key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Key)
                .Take(5)
                .ToList();

            if (matches.Count == 0) return null;

            return new DeviceAssetInfo
            {
                ClassName = deviceClassName,
                AssetPaths = matches,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find device assets for: {Class}", deviceClassName);
        }

        return null;
    }

    private static string? FindPaksDirectory(string basePath)
    {
        // Try common Fortnite installation layouts
        var candidates = new[]
        {
            Path.Combine(basePath, "FortniteGame", "Content", "Paks"),
            Path.Combine(basePath, "Content", "Paks"),
            basePath, // Maybe they pointed directly at the Paks dir
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                Directory.EnumerateFiles(candidate, "*.pak").Any())
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _provider?.Dispose();
        _provider = null;
        _initialized = false;
    }
}

/// <summary>
/// Basic mesh preview information.
/// </summary>
public class MeshPreviewInfo
{
    public int VertexCount { get; set; }
    public int TriangleCount { get; set; }
    public int LODCount { get; set; }
    public int MaterialCount { get; set; }
}

/// <summary>
/// Device asset lookup result.
/// </summary>
public class DeviceAssetInfo
{
    public string ClassName { get; set; } = "";
    public List<string> AssetPaths { get; set; } = new();
}
