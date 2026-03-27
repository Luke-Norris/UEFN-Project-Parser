using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WellVersed.Core.Models;

namespace WellVersed.Core.Services;

/// <summary>
/// HTTP client that connects to the Python bridge running inside UEFN at localhost:9220.
/// Provides typed convenience methods for all bridge commands.
/// </summary>
public class UefnBridge
{
    private readonly HttpClient _client;
    private readonly ILogger<UefnBridge> _logger;
    private int _port = 9220;
    private bool _connected;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public UefnBridge(ILogger<UefnBridge> logger)
    {
        _logger = logger;
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Check if the bridge is currently connected.
    /// </summary>
    public async Task<bool> IsConnected()
    {
        if (!_connected) return false;

        try
        {
            var response = await _client.GetAsync($"http://localhost:{_port}/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            _connected = false;
            return false;
        }
    }

    /// <summary>
    /// Test connection to the bridge, scanning ports 9220-9222.
    /// </summary>
    public async Task<BridgeResponse> Connect(int port = 9220)
    {
        // Try the specified port first, then scan nearby ports
        var portsToTry = new[] { port, 9220, 9221, 9222 }.Distinct();

        foreach (var p in portsToTry)
        {
            try
            {
                _logger.LogDebug("Attempting bridge connection on port {Port}", p);
                var response = await _client.GetAsync($"http://localhost:{p}/status");
                if (response.IsSuccessStatusCode)
                {
                    _port = p;
                    _connected = true;
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Bridge connected on port {Port}", p);

                    JsonElement? data = null;
                    try { data = JsonSerializer.Deserialize<JsonElement>(body); } catch { }

                    return new BridgeResponse
                    {
                        Status = "ok",
                        Data = data
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Port {Port} failed: {Error}", p, ex.Message);
            }
        }

        _connected = false;
        return new BridgeResponse
        {
            Status = "error",
            Error = $"Could not connect to UEFN bridge on ports {string.Join(", ", portsToTry)}. Is the bridge plugin running in UEFN?"
        };
    }

    /// <summary>
    /// Clear connection state.
    /// </summary>
    public void Disconnect()
    {
        _connected = false;
        _logger.LogDebug("Bridge disconnected");
    }

    /// <summary>
    /// Send a command to the bridge via HTTP POST.
    /// </summary>
    public async Task<BridgeResponse> SendCommand(string command, object? @params = null)
    {
        if (!_connected)
        {
            return new BridgeResponse
            {
                Status = "error",
                Error = "Not connected to UEFN bridge. Call Connect() first."
            };
        }

        var requestId = Guid.NewGuid().ToString("N")[..8];
        var payload = new
        {
            command,
            @params,
            request_id = requestId
        };

        _logger.LogDebug("Bridge command: {Command} (id={RequestId})", command, requestId);

        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"http://localhost:{_port}/command", content);
            var body = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<BridgeResponse>(body, JsonOpts);
            if (result == null)
            {
                return new BridgeResponse
                {
                    Status = "error",
                    Error = "Failed to parse bridge response"
                };
            }

            _logger.LogDebug("Bridge response: {Status} for {Command}", result.Status, command);
            return result;
        }
        catch (TaskCanceledException)
        {
            return new BridgeResponse
            {
                Status = "error",
                Error = $"Bridge command '{command}' timed out after 30 seconds"
            };
        }
        catch (HttpRequestException ex)
        {
            _connected = false;
            return new BridgeResponse
            {
                Status = "error",
                Error = $"Bridge connection lost: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new BridgeResponse
            {
                Status = "error",
                Error = $"Bridge error: {ex.Message}"
            };
        }
    }

    // ─── Actor Management ──────────────────────────────────────────────────

    /// <summary>
    /// Spawn a Creative device or actor in the UEFN level.
    /// </summary>
    public Task<BridgeResponse> SpawnActor(
        string classPath,
        Vector3Info location,
        Vector3Info? rotation = null,
        Dictionary<string, string>? properties = null)
    {
        return SendCommand("spawn_actor", new
        {
            classPath,
            location = new { location.X, location.Y, location.Z },
            rotation = rotation != null ? new { rotation.X, rotation.Y, rotation.Z } : null,
            properties
        });
    }

    /// <summary>
    /// Delete actors by name from the level.
    /// </summary>
    public Task<BridgeResponse> DeleteActors(List<string> actorNames)
    {
        return SendCommand("delete_actors", new { actorNames });
    }

    /// <summary>
    /// Set properties on an existing actor.
    /// </summary>
    public Task<BridgeResponse> SetActorProperties(string actorName, Dictionary<string, string> properties)
    {
        return SendCommand("set_actor_properties", new { actorName, properties });
    }

    /// <summary>
    /// Get all properties from an actor. Returns property key-value pairs.
    /// </summary>
    public async Task<Dictionary<string, string>> GetActorProperties(string actorName)
    {
        var result = await SendCommand("get_actor_properties", new { actorName });
        if (result.IsOk && result.Data.HasValue)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(result.Data.Value.GetRawText(), JsonOpts)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Get a summary of all actors in the current level.
    /// </summary>
    public Task<BridgeResponse> GetAllActors()
    {
        return SendCommand("get_all_actors");
    }

    /// <summary>
    /// Export the full world state as JSON.
    /// </summary>
    public async Task<string> ExportWorldState()
    {
        var result = await SendCommand("export_world_state");
        if (result.IsOk && result.Data.HasValue)
            return result.Data.Value.GetRawText();
        return result.Error ?? "Failed to export world state";
    }

    // ─── Device Catalog ────────────────────────────────────────────────────

    /// <summary>
    /// Scan for all available Creative device classes.
    /// </summary>
    public Task<BridgeResponse> DeviceCatalogScan()
    {
        return SendCommand("device_catalog_scan");
    }

    // ─── Stamps ────────────────────────────────────────────────────────────

    /// <summary>
    /// Save the selected actors as a reusable stamp.
    /// </summary>
    public Task<BridgeResponse> StampSave(string name)
    {
        return SendCommand("stamp_save", new { name });
    }

    /// <summary>
    /// Place a saved stamp at a location.
    /// </summary>
    public Task<BridgeResponse> StampPlace(
        string name,
        Vector3Info location,
        float yawOffset = 0,
        float scale = 1)
    {
        return SendCommand("stamp_place", new
        {
            name,
            location = new { location.X, location.Y, location.Z },
            yawOffset,
            scale
        });
    }

    // ─── Geometry Primitives ───────────────────────────────────────────────

    /// <summary>
    /// Create a floor plane.
    /// </summary>
    public Task<BridgeResponse> CreateFloor(float width, float depth, Vector3Info location)
    {
        return SendCommand("create_floor", new
        {
            width,
            depth,
            location = new { location.X, location.Y, location.Z }
        });
    }

    /// <summary>
    /// Create a wall.
    /// </summary>
    public Task<BridgeResponse> CreateWall(float width, float height, Vector3Info location, float yaw)
    {
        return SendCommand("create_wall", new
        {
            width,
            height,
            location = new { location.X, location.Y, location.Z },
            yaw
        });
    }

    /// <summary>
    /// Create a box.
    /// </summary>
    public Task<BridgeResponse> CreateBox(float width, float height, float depth, Vector3Info location)
    {
        return SendCommand("create_box", new
        {
            width,
            height,
            depth,
            location = new { location.X, location.Y, location.Z }
        });
    }

    /// <summary>
    /// Create a full arena with spawns.
    /// </summary>
    public Task<BridgeResponse> CreateArena(string sizePreset, int teamCount)
    {
        return SendCommand("create_arena", new { sizePreset, teamCount });
    }

    // ─── Verse ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Write a Verse source file to the project.
    /// </summary>
    public Task<BridgeResponse> WriteVerseFile(string filename, string content)
    {
        return SendCommand("write_verse_file", new { filename, content });
    }

    // ─── Build ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger a Verse build and return the result.
    /// </summary>
    public Task<BridgeResponse> TriggerBuild()
    {
        return SendCommand("trigger_build");
    }

    /// <summary>
    /// Get structured build errors from the last compilation.
    /// </summary>
    public Task<BridgeResponse> GetBuildErrors()
    {
        return SendCommand("get_build_errors");
    }

    // ─── Publishing ────────────────────────────────────────────────────────

    /// <summary>
    /// Run a pre-publish audit (9 checks).
    /// </summary>
    public Task<BridgeResponse> RunPublishAudit()
    {
        return SendCommand("run_publish_audit");
    }

    // ─── Generation Plan ───────────────────────────────────────────────────

    /// <summary>
    /// Execute a structured generation plan through the bridge.
    /// </summary>
    public Task<BridgeResponse> ExecuteGenerationPlan(GenerationPlan plan)
    {
        return SendCommand("execute_generation_plan", plan);
    }

    // ─── Viewport ──────────────────────────────────────────────────────────

    /// <summary>
    /// Get the current viewport camera position and rotation.
    /// </summary>
    public Task<BridgeResponse> GetViewportCamera()
    {
        return SendCommand("get_viewport_camera");
    }

    /// <summary>
    /// Set the viewport camera to a specific position and rotation.
    /// </summary>
    public Task<BridgeResponse> SetViewportCamera(Vector3Info location, Vector3Info rotation)
    {
        return SendCommand("set_viewport_camera", new
        {
            location = new { location.X, location.Y, location.Z },
            rotation = new { rotation.X, rotation.Y, rotation.Z }
        });
    }

    /// <summary>
    /// Take a screenshot of the UEFN viewport.
    /// </summary>
    public async Task<string> TakeScreenshot(string outputPath)
    {
        var result = await SendCommand("take_screenshot", new { outputPath });
        if (result.IsOk && result.Data.HasValue)
        {
            try
            {
                var path = result.Data.Value.GetProperty("path").GetString();
                return path ?? outputPath;
            }
            catch
            {
                return outputPath;
            }
        }
        return result.Error ?? "Failed to take screenshot";
    }
}

/// <summary>
/// Response from the UEFN bridge.
/// </summary>
public class BridgeResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonIgnore]
    public bool IsOk => Status == "ok";
}
