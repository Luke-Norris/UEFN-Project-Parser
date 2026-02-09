using FortniteForge.Core.Config;
using FortniteForge.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FortniteForge.Core.Services;

/// <summary>
/// Handles UEFN build operations — triggering builds, capturing output, and parsing errors.
/// Also handles reading build logs from previous builds.
/// </summary>
public class BuildService
{
    private readonly ForgeConfig _config;
    private readonly ILogger<BuildService> _logger;

    public BuildService(ForgeConfig config, ILogger<BuildService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Triggers a UEFN build and captures the output.
    /// </summary>
    public async Task<BuildResult> TriggerBuildAsync(CancellationToken cancellationToken = default)
    {
        var result = new BuildResult();
        var buildConfig = _config.Build;

        if (string.IsNullOrEmpty(buildConfig.BuildCommand))
        {
            result.Status = BuildStatus.NotStarted;
            result.Success = false;
            result.RawOutput = "Build command not configured. Set 'build.buildCommand' in forge.config.json.";

            // Try to find UEFN automatically
            var autoDetected = TryAutoDetectUefn();
            if (autoDetected != null)
            {
                result.RawOutput += $"\nAuto-detected UEFN at: {autoDetected}";
                result.RawOutput += "\nUpdate forge.config.json with this path to enable builds.";
            }

            return result;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = buildConfig.BuildCommand,
                Arguments = buildConfig.BuildArguments,
                WorkingDirectory = _config.ProjectPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            var output = new List<string>();
            var errors = new List<string>();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    output.Add(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    errors.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeout = TimeSpan.FromSeconds(buildConfig.TimeoutSeconds);
            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), cancellationToken);

            stopwatch.Stop();
            result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

            if (!completed)
            {
                process.Kill();
                result.Status = BuildStatus.Timeout;
                result.Success = false;
                result.RawOutput = $"Build timed out after {buildConfig.TimeoutSeconds}s.\n" +
                                   string.Join("\n", output.TakeLast(50));
                return result;
            }

            var allOutput = string.Join("\n", output);
            var allErrors = string.Join("\n", errors);
            result.RawOutput = allOutput + (string.IsNullOrEmpty(allErrors) ? "" : $"\n--- STDERR ---\n{allErrors}");

            // Parse output for errors and warnings
            ParseBuildOutput(output.Concat(errors), result);

            result.Success = process.ExitCode == 0;
            result.Status = process.ExitCode == 0
                ? (result.Warnings.Count > 0 ? BuildStatus.SuccessWithWarnings : BuildStatus.Success)
                : BuildStatus.Failed;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
            result.Status = BuildStatus.Failed;
            result.Success = false;
            result.RawOutput = $"Build process error: {ex.Message}";
            _logger.LogError(ex, "Build process failed");
        }

        return result;
    }

    /// <summary>
    /// Reads and parses the most recent build log file.
    /// </summary>
    public BuildResult ReadLatestBuildLog()
    {
        var result = new BuildResult();
        var logDir = _config.Build.LogDirectory;

        if (string.IsNullOrEmpty(logDir))
        {
            // Try common UEFN log locations
            var possibleDirs = new[]
            {
                Path.Combine(_config.ProjectPath, "Saved", "Logs"),
                Path.Combine(_config.ProjectPath, "Intermediate", "Build"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FortniteGame", "Saved", "Logs")
            };

            logDir = possibleDirs.FirstOrDefault(Directory.Exists) ?? "";
        }

        if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
        {
            result.RawOutput = "Build log directory not found. Configure 'build.logDirectory' in forge.config.json.";
            result.Status = BuildStatus.NotStarted;
            return result;
        }

        // Find most recent log file
        var logFile = Directory.EnumerateFiles(logDir, "*.log")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .FirstOrDefault();

        if (logFile == null)
        {
            result.RawOutput = $"No log files found in: {logDir}";
            result.Status = BuildStatus.NotStarted;
            return result;
        }

        result.LogFilePath = logFile;

        try
        {
            var lines = File.ReadAllLines(logFile);
            result.RawOutput = string.Join("\n", lines.TakeLast(200)); // Last 200 lines
            ParseBuildOutput(lines, result);

            result.Status = result.Errors.Count > 0
                ? BuildStatus.Failed
                : result.Warnings.Count > 0
                    ? BuildStatus.SuccessWithWarnings
                    : BuildStatus.Success;
            result.Success = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            result.RawOutput = $"Failed to read log file: {ex.Message}";
            result.Status = BuildStatus.Failed;
        }

        return result;
    }

    /// <summary>
    /// Gets Verse compilation errors from the build log.
    /// Returns them in a format suitable for forwarding to a Verse-focused Claude session.
    /// </summary>
    public List<VerseError> GetVerseErrors(string? logFilePath = null)
    {
        var result = new List<VerseError>();

        if (logFilePath == null)
        {
            var buildResult = ReadLatestBuildLog();
            return buildResult.VerseErrors;
        }

        if (!File.Exists(logFilePath))
            return result;

        var lines = File.ReadAllLines(logFilePath);
        foreach (var line in lines)
        {
            var verseError = TryParseVerseError(line);
            if (verseError != null)
                result.Add(verseError);
        }

        return result;
    }

    /// <summary>
    /// Gets a summary of the build log suitable for Claude to read quickly.
    /// </summary>
    public string GetBuildSummary()
    {
        var buildResult = ReadLatestBuildLog();

        if (buildResult.Status == BuildStatus.NotStarted)
            return buildResult.RawOutput;

        var summary = $"Build Status: {buildResult.Status}\n";
        summary += $"Log File: {buildResult.LogFilePath ?? "N/A"}\n\n";

        if (buildResult.Errors.Count > 0)
        {
            summary += $"=== ERRORS ({buildResult.Errors.Count}) ===\n";
            foreach (var error in buildResult.Errors)
            {
                summary += $"  [{error.Code ?? "ERR"}] {error.Message}";
                if (error.File != null)
                    summary += $" ({error.File}:{error.Line})";
                summary += "\n";
            }
        }

        if (buildResult.VerseErrors.Count > 0)
        {
            summary += $"\n=== VERSE ERRORS ({buildResult.VerseErrors.Count}) ===\n";
            foreach (var error in buildResult.VerseErrors)
            {
                summary += $"  {error.FilePath}:{error.Line} — {error.Message}\n";
                if (error.Snippet != null)
                    summary += $"    > {error.Snippet}\n";
            }
        }

        if (buildResult.Warnings.Count > 0)
        {
            summary += $"\n=== WARNINGS ({buildResult.Warnings.Count}) ===\n";
            foreach (var warning in buildResult.Warnings.Take(20))
            {
                summary += $"  {warning.Message}\n";
            }
            if (buildResult.Warnings.Count > 20)
                summary += $"  ... and {buildResult.Warnings.Count - 20} more\n";
        }

        return summary;
    }

    private void ParseBuildOutput(IEnumerable<string> lines, BuildResult result)
    {
        foreach (var line in lines)
        {
            // UE/UEFN error pattern: LogCategory: Error: message
            // or: file(line): error code: message
            if (Regex.IsMatch(line, @"\berror\b", RegexOptions.IgnoreCase) &&
                !line.Contains("Error: 0", StringComparison.OrdinalIgnoreCase)) // Skip "0 errors"
            {
                var error = ParseBuildMessage(line);
                result.Errors.Add(error);
            }
            else if (Regex.IsMatch(line, @"\bwarning\b", RegexOptions.IgnoreCase) &&
                     !line.Contains("Warning: 0", StringComparison.OrdinalIgnoreCase))
            {
                var warning = ParseBuildMessage(line);
                result.Warnings.Add(warning);
            }

            // Verse-specific errors
            var verseError = TryParseVerseError(line);
            if (verseError != null)
                result.VerseErrors.Add(verseError);
        }
    }

    private static BuildMessage ParseBuildMessage(string line)
    {
        var msg = new BuildMessage { Message = line.Trim() };

        // Try to extract file:line pattern
        var fileMatch = Regex.Match(line, @"([A-Za-z]:\\[^(]+|/[^(]+)\((\d+)(?:,(\d+))?\)");
        if (fileMatch.Success)
        {
            msg.File = fileMatch.Groups[1].Value;
            msg.Line = int.TryParse(fileMatch.Groups[2].Value, out var l) ? l : null;
            msg.Column = fileMatch.Groups[3].Success && int.TryParse(fileMatch.Groups[3].Value, out var c) ? c : null;
        }

        // Try to extract error code
        var codeMatch = Regex.Match(line, @"\b(E\d{4}|C\d{4}|LNK\d{4}|MSB\d{4})\b");
        if (codeMatch.Success)
            msg.Code = codeMatch.Groups[1].Value;

        return msg;
    }

    private static VerseError? TryParseVerseError(string line)
    {
        // Verse error pattern: filepath(line:col): error message
        // or: Verse: Error: message at filepath:line
        var verseMatch = Regex.Match(line,
            @"(.+\.verse)\((\d+)(?::(\d+))?\)\s*:\s*(?:error\s*)?(.+)",
            RegexOptions.IgnoreCase);

        if (verseMatch.Success)
        {
            return new VerseError
            {
                FilePath = verseMatch.Groups[1].Value.Trim(),
                Line = int.TryParse(verseMatch.Groups[2].Value, out var l) ? l : null,
                Column = verseMatch.Groups[3].Success && int.TryParse(verseMatch.Groups[3].Value, out var c) ? c : null,
                Message = verseMatch.Groups[4].Value.Trim()
            };
        }

        // Alternative Verse error format
        var altMatch = Regex.Match(line,
            @"Verse.*?(?:Error|error)\s*:?\s*(.+?)(?:\s+at\s+(.+\.verse):(\d+))?$",
            RegexOptions.IgnoreCase);

        if (altMatch.Success)
        {
            return new VerseError
            {
                Message = altMatch.Groups[1].Value.Trim(),
                FilePath = altMatch.Groups[2].Success ? altMatch.Groups[2].Value : null,
                Line = altMatch.Groups[3].Success && int.TryParse(altMatch.Groups[3].Value, out var l2) ? l2 : null
            };
        }

        return null;
    }

    private string? TryAutoDetectUefn()
    {
        // Common UEFN installation paths
        var possiblePaths = new[]
        {
            @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\UnrealEditor-Cmd.exe",
            @"C:\Program Files (x86)\Epic Games\Fortnite\FortniteGame\Binaries\Win64\UnrealEditor-Cmd.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Epic Games", "Fortnite", "FortniteGame", "Binaries", "Win64", "UnrealEditor-Cmd.exe")
        };

        return possiblePaths.FirstOrDefault(File.Exists);
    }
}
