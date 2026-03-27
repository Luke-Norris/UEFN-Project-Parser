using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the AssetValidator — verifies snapshot capture, re-read validation,
/// empty file detection, and structural integrity checking.
///
/// Sandbox-dependent tests validate real .uasset roundtrip integrity.
/// </summary>
public class AssetValidatorTests : IDisposable
{
    private static readonly string? SandboxPath = Environment.GetEnvironmentVariable("WELLVERSED_SANDBOX");
    private static bool SandboxExists => !string.IsNullOrEmpty(SandboxPath) && Directory.Exists(SandboxPath);

    private readonly AssetValidator _validator = new(NullLogger<AssetValidator>.Instance);
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* cleanup best-effort */ }
            // Also clean .uexp
            var uexp = Path.ChangeExtension(file, ".uexp");
            try { if (File.Exists(uexp)) File.Delete(uexp); }
            catch { }
        }
    }

    // =====================================================================
    //  Validate — empty/missing file detection
    // =====================================================================

    [Fact]
    public void Validate_CatchesMissingFile()
    {
        var snapshot = new AssetSnapshot
        {
            Context = "test",
            ExportCount = 1,
            ImportCount = 1
        };

        var result = _validator.Validate("/nonexistent/path/fake.uasset", snapshot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public void Validate_CatchesEmptyFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"wv_test_{Guid.NewGuid():N}.uasset");
        File.WriteAllBytes(tempFile, Array.Empty<byte>());
        _tempFiles.Add(tempFile);

        var snapshot = new AssetSnapshot
        {
            Context = "test",
            ExportCount = 1,
            ImportCount = 1
        };

        var result = _validator.Validate(tempFile, snapshot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_CatchesSuspiciouslySmallFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"wv_test_{Guid.NewGuid():N}.uasset");
        File.WriteAllBytes(tempFile, new byte[50]); // 50 bytes — too small for valid uasset
        _tempFiles.Add(tempFile);

        var snapshot = new AssetSnapshot
        {
            Context = "test",
            ExportCount = 1,
            ImportCount = 1
        };

        var result = _validator.Validate(tempFile, snapshot);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("suspiciously small"));
    }

    // =====================================================================
    //  CaptureSnapshot
    // =====================================================================

    [SkippableFact]
    public void CaptureSnapshot_RecordsExportAndImportCounts()
    {
        Skip.IfNot(SandboxExists, "WELLVERSED_SANDBOX not set");

        var assetFile = FindFirstUassetInSandbox();
        Skip.If(assetFile == null, "No .uasset files found in sandbox");

        var asset = new UAsset(assetFile, EngineVersion.VER_UE5_4);
        var snapshot = _validator.CaptureSnapshot(asset, "test_snapshot");

        Assert.Equal("test_snapshot", snapshot.Context);
        Assert.True(snapshot.ExportCount >= 0, "Export count should be non-negative");
        Assert.True(snapshot.ImportCount >= 0, "Import count should be non-negative");
        Assert.True(snapshot.NameMapCount >= 0, "NameMap count should be non-negative");
        Assert.NotNull(snapshot.ExportClassTypes);
        Assert.NotNull(snapshot.ExportNames);
        Assert.Equal(snapshot.ExportCount, snapshot.ExportClassTypes.Count);
        Assert.Equal(snapshot.ExportCount, snapshot.ExportNames.Count);
    }

    // =====================================================================
    //  Full roundtrip validation (sandbox)
    // =====================================================================

    [SkippableFact]
    public void Validate_RoundtripReadWriteRead_Passes()
    {
        Skip.IfNot(SandboxExists, "WELLVERSED_SANDBOX not set");

        var assetFile = FindFirstUassetInSandbox();
        Skip.If(assetFile == null, "No .uasset files found in sandbox");

        // Read the original
        var asset = new UAsset(assetFile, EngineVersion.VER_UE5_4);
        var snapshot = _validator.CaptureSnapshot(asset, "roundtrip_test");

        // Write to temp
        var tempFile = Path.Combine(Path.GetTempPath(), $"wv_roundtrip_{Guid.NewGuid():N}.uasset");
        _tempFiles.Add(tempFile);
        asset.Write(tempFile);

        // Validate
        var result = _validator.Validate(tempFile, snapshot);

        Assert.True(result.IsValid,
            $"Roundtrip validation failed: {string.Join("; ", result.Errors)}");
        Assert.True(result.FileSizeBytes > 0);
    }

    // =====================================================================
    //  ValidationResult model
    // =====================================================================

    [Fact]
    public void ValidationResult_ToString_IncludesStatus()
    {
        var result = new ValidationResult
        {
            FilePath = "test.uasset",
            IsValid = true
        };
        Assert.Contains("VALID", result.ToString());

        var invalidResult = new ValidationResult
        {
            FilePath = "test.uasset",
            IsValid = false,
            Errors = { "Some error" }
        };
        Assert.Contains("INVALID", invalidResult.ToString());
    }

    [Fact]
    public void AssetSnapshot_DefaultValues()
    {
        var snapshot = new AssetSnapshot();
        Assert.Equal("", snapshot.Context);
        Assert.Equal(0, snapshot.ExportCount);
        Assert.Equal(0, snapshot.ImportCount);
        Assert.Empty(snapshot.ExportClassTypes);
        Assert.Empty(snapshot.ExportNames);
        Assert.False(snapshot.HasLevelExport);
        Assert.Equal(0, snapshot.LevelActorCount);
    }

    // =====================================================================
    //  Helper
    // =====================================================================

    private static string? FindFirstUassetInSandbox()
    {
        if (SandboxPath == null) return null;

        var projects = WellVersed.Core.Config.WellVersedConfig.DiscoverProjects(SandboxPath);
        foreach (var project in projects.Take(5))
        {
            var contentPath = Path.Combine(project, "Content");
            if (!Directory.Exists(contentPath)) continue;

            var files = Directory.EnumerateFiles(contentPath, "*.uasset", SearchOption.AllDirectories).Take(1);
            var first = files.FirstOrDefault();
            if (first != null) return first;
        }
        return null;
    }
}
