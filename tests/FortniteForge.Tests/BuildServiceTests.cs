using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using FortniteForge.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortniteForge.Tests;

public class BuildServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ForgeConfig _config;
    private readonly BuildService _service;

    public BuildServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBuildTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _config = new ForgeConfig { ProjectPath = _tempDir };
        _service = new BuildService(_config, NullLogger<BuildService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task TriggerBuild_NoBuildCommand_ReturnsNotStarted()
    {
        var result = await _service.TriggerBuildAsync();

        Assert.False(result.Success);
        Assert.Equal(BuildStatus.NotStarted, result.Status);
        Assert.Contains("not configured", result.RawOutput);
    }

    [Fact]
    public void ReadLatestBuildLog_ExplicitMissingDir_ReturnsNotStarted()
    {
        // Set an explicit non-existent log directory to avoid auto-detection
        _config.Build.LogDirectory = Path.Combine(_tempDir, "nonexistent_logs");

        var result = _service.ReadLatestBuildLog();

        Assert.Equal(BuildStatus.NotStarted, result.Status);
    }

    [Fact]
    public void ReadLatestBuildLog_WithLogFile_ParsesErrors()
    {
        var logDir = Path.Combine(_tempDir, "Saved", "Logs");
        Directory.CreateDirectory(logDir);
        _config.Build.LogDirectory = logDir;

        var logContent = @"[2024-01-01 12:00:00] LogInit: Starting build
[2024-01-01 12:00:01] LogCompile: error C2065: undeclared identifier
[2024-01-01 12:00:02] LogCompile: warning: unused variable
[2024-01-01 12:00:03] Build complete.";
        File.WriteAllText(Path.Combine(logDir, "build.log"), logContent);

        var result = _service.ReadLatestBuildLog();

        Assert.NotEmpty(result.Errors);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void GetVerseErrors_ParsesVerseErrorFormat()
    {
        var logDir = Path.Combine(_tempDir, "Logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "verse_errors.log");

        var content = @"
C:\Projects\Content\my_device.verse(15:8): Unknown identifier 'Foo'
C:\Projects\Content\game_manager.verse(42): Type mismatch in assignment
Some other log line
";
        File.WriteAllText(logFile, content);

        var errors = _service.GetVerseErrors(logFile);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.FilePath!.Contains("my_device.verse") && e.Line == 15);
        Assert.Contains(errors, e => e.FilePath!.Contains("game_manager.verse") && e.Line == 42);
    }

    [Fact]
    public void GetVerseErrors_NoFile_ReturnsEmpty()
    {
        var errors = _service.GetVerseErrors(@"C:\nonexistent_log.log");

        Assert.Empty(errors);
    }

    [Fact]
    public void GetBuildSummary_NoBuildLog_ReturnsMessage()
    {
        var summary = _service.GetBuildSummary();

        Assert.NotNull(summary);
        Assert.NotEmpty(summary);
    }
}
