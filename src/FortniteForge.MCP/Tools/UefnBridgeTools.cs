using WellVersed.Core.Models;
using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools that expose the UEFN bridge — live connection to a running UEFN instance
/// through a Python bridge plugin at localhost:9220.
/// </summary>
[McpServerToolType]
public class UefnBridgeTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string NotConnectedMessage =>
        "UEFN bridge is not connected. Call connect_uefn first to establish a connection. " +
        "The bridge requires the Python bridge plugin to be running inside UEFN (localhost:9220).";

    [McpServerTool, Description(
        "Connect to the UEFN bridge — a Python plugin running inside UEFN that allows " +
        "live manipulation of the level. Scans ports 9220-9222. Call this before using " +
        "any other bridge commands.")]
    public async Task<string> connect_uefn(
        UefnBridge bridge,
        [Description("Port to connect on (default 9220, will also scan 9221-9222)")] int? port = null)
    {
        var result = await bridge.Connect(port ?? 9220);
        return JsonSerializer.Serialize(new
        {
            connected = result.IsOk,
            status = result.Status,
            message = result.IsOk
                ? "Connected to UEFN bridge. You can now use bridge commands to manipulate the live level."
                : result.Error,
            data = result.Data
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Check the current UEFN bridge connection status. Returns whether the bridge " +
        "is connected, UEFN is running, and the level state.")]
    public async Task<string> uefn_status(UefnBridge bridge)
    {
        var connected = await bridge.IsConnected();
        if (!connected)
        {
            return JsonSerializer.Serialize(new
            {
                connected = false,
                message = "Bridge not connected. Use connect_uefn to establish connection."
            }, JsonOpts);
        }

        var status = await bridge.SendCommand("status");
        return JsonSerializer.Serialize(new
        {
            connected = true,
            bridgeStatus = status.Status,
            data = status.Data,
            error = status.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Spawn a Creative device or actor live in UEFN. Provide the device class path " +
        "and world location. Optionally set initial properties and rotation.")]
    public async Task<string> place_device(
        UefnBridge bridge,
        [Description("The device class path (e.g., '/Game/Devices/BP_TriggerDevice.BP_TriggerDevice_C')")] string classPath,
        [Description("X world coordinate")] float x,
        [Description("Y world coordinate")] float y,
        [Description("Z world coordinate")] float z,
        [Description("JSON object of property name-value pairs to set on spawn")] string? properties = null)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        Dictionary<string, string>? props = null;
        if (!string.IsNullOrEmpty(properties))
        {
            try { props = JsonSerializer.Deserialize<Dictionary<string, string>>(properties); }
            catch { return "Invalid properties JSON. Expected a JSON object like {\"Key\": \"Value\"}."; }
        }

        var result = await bridge.SpawnActor(
            classPath,
            new Vector3Info(x, y, z),
            properties: props);

        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            result.Status,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Remove an actor from the UEFN level by name.")]
    public async Task<string> delete_device(
        UefnBridge bridge,
        [Description("The actor name to delete (e.g., 'TriggerDevice_3')")] string actorName)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.DeleteActors(new List<string> { actorName });
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            result.Status,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Set properties on an existing device/actor in the UEFN level. " +
        "Properties are provided as a JSON string of key-value pairs.")]
    public async Task<string> set_device_properties(
        UefnBridge bridge,
        [Description("The actor name to modify")] string actorName,
        [Description("JSON object of property name-value pairs")] string properties)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        Dictionary<string, string> props;
        try
        {
            props = JsonSerializer.Deserialize<Dictionary<string, string>>(properties)
                    ?? new Dictionary<string, string>();
        }
        catch
        {
            return "Invalid properties JSON. Expected a JSON object like {\"Key\": \"Value\"}.";
        }

        var result = await bridge.SetActorProperties(actorName, props);
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            result.Status,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Read all properties from a device/actor in the UEFN level.")]
    public async Task<string> get_device_properties(
        UefnBridge bridge,
        [Description("The actor name to inspect")] string actorName)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var props = await bridge.GetActorProperties(actorName);
        return JsonSerializer.Serialize(new
        {
            actorName,
            propertyCount = props.Count,
            properties = props
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Export the complete world state of the current UEFN level as JSON. " +
        "Includes all actors, their properties, transforms, and wiring.")]
    public async Task<string> export_world_state(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        return await bridge.ExportWorldState();
    }

    [McpServerTool, Description(
        "Discover all available Creative device classes that can be spawned. " +
        "Returns the full catalog of devices the bridge can place.")]
    public async Task<string> scan_device_catalog(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.DeviceCatalogScan();
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Create geometry primitives in the UEFN level — floor, wall, or box. " +
        "Useful for quickly building level structures.")]
    public async Task<string> create_geometry(
        UefnBridge bridge,
        [Description("Geometry type: 'floor', 'wall', or 'box'")] string type,
        [Description("Width in unreal units")] float width,
        [Description("X world coordinate")] float x,
        [Description("Y world coordinate")] float y,
        [Description("Z world coordinate")] float z,
        [Description("Height (required for wall and box)")] float? height = null,
        [Description("Depth (required for box)")] float? depth = null,
        [Description("Yaw rotation in degrees (for walls)")] float? yaw = null)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var location = new Vector3Info(x, y, z);
        BridgeResponse result;

        switch (type.ToLowerInvariant())
        {
            case "floor":
                result = await bridge.CreateFloor(width, depth ?? width, location);
                break;
            case "wall":
                if (height == null) return "Height is required for wall geometry.";
                result = await bridge.CreateWall(width, height.Value, location, yaw ?? 0);
                break;
            case "box":
                if (height == null) return "Height is required for box geometry.";
                result = await bridge.CreateBox(width, height.Value, depth ?? width, location);
                break;
            default:
                return $"Unknown geometry type '{type}'. Use 'floor', 'wall', or 'box'.";
        }

        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            type,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Generate a complete arena with spawns, boundaries, and team areas. " +
        "Size presets: 'small' (1v1), 'medium' (2v2-4v4), 'large' (8v8+), 'huge' (16v16+).")]
    public async Task<string> create_arena(
        UefnBridge bridge,
        [Description("Size preset: 'small', 'medium', 'large', or 'huge'")] string sizePreset,
        [Description("Number of teams (2-16)")] int teamCount)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.CreateArena(sizePreset, teamCount);
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            sizePreset,
            teamCount,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Save selected actors in UEFN as a reusable stamp (template) for later placement.")]
    public async Task<string> stamp_save(
        UefnBridge bridge,
        [Description("Name for the stamp")] string name)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.StampSave(name);
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            name,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Place a previously saved stamp at a location with optional rotation and scale.")]
    public async Task<string> stamp_place(
        UefnBridge bridge,
        [Description("Name of the stamp to place")] string name,
        [Description("X world coordinate")] float x,
        [Description("Y world coordinate")] float y,
        [Description("Z world coordinate")] float z,
        [Description("Yaw rotation offset in degrees")] float? yawOffset = null,
        [Description("Uniform scale multiplier")] float? scale = null)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.StampPlace(name, new Vector3Info(x, y, z), yawOffset ?? 0, scale ?? 1);
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            name,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "List all saved stamps (reusable actor templates).")]
    public async Task<string> stamp_list(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.SendCommand("stamp_list");
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Write a Verse source file to the UEFN project and trigger a build. " +
        "The file will be written to the project's Verse source directory.")]
    public async Task<string> deploy_verse(
        UefnBridge bridge,
        [Description("Filename for the Verse file (e.g., 'my_device.verse')")] string filename,
        [Description("Complete Verse source code content")] string content)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        // Write the file
        var writeResult = await bridge.WriteVerseFile(filename, content);
        if (!writeResult.IsOk)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                step = "write",
                error = writeResult.Error
            }, JsonOpts);
        }

        // Trigger build
        var buildResult = await bridge.TriggerBuild();
        return JsonSerializer.Serialize(new
        {
            success = buildResult.IsOk,
            fileWritten = true,
            filename,
            buildTriggered = true,
            buildStatus = buildResult.Status,
            buildData = buildResult.Data,
            buildError = buildResult.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Force a Verse compilation in UEFN. Returns build status and any errors.")]
    public async Task<string> trigger_verse_build(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.TriggerBuild();
        if (!result.IsOk)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error
            }, JsonOpts);
        }

        // Also fetch errors
        var errors = await bridge.GetBuildErrors();
        return JsonSerializer.Serialize(new
        {
            success = true,
            buildResult = result.Data,
            errors = errors.Data,
            hasErrors = errors.IsOk && errors.Data.HasValue
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Run a pre-publish validation audit (9 checks). Identifies issues that would " +
        "prevent successful publishing of the UEFN project.")]
    public async Task<string> run_publish_audit(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.RunPublishAudit();
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Detect orphaned or broken asset references in the UEFN project.")]
    public async Task<string> find_broken_references(UefnBridge bridge)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var result = await bridge.SendCommand("find_broken_references");
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Capture a screenshot of the UEFN viewport. Returns the file path to the saved image.")]
    public async Task<string> take_screenshot(
        UefnBridge bridge,
        [Description("Output path for the screenshot file. Defaults to a temp directory.")] string? outputPath = null)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        var path = outputPath ?? Path.Combine(Path.GetTempPath(), $"uefn_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var result = await bridge.TakeScreenshot(path);
        return JsonSerializer.Serialize(new
        {
            success = !result.StartsWith("Failed"),
            path = result
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "THE BIG ONE: Execute a complete generation plan to build game systems in UEFN. " +
        "Either provide a JSON plan directly, or describe what you want and a plan will be " +
        "structured from the description. Plans include devices to place, wiring to connect, " +
        "and optional Verse code to deploy.")]
    public async Task<string> execute_generation_plan(
        UefnBridge bridge,
        [Description("Human-readable description of the system to generate (e.g., 'Create a capture point with timer and score display')")] string? description = null,
        [Description("JSON string of a structured GenerationPlan object. If provided, this is executed directly.")] string? planJson = null)
    {
        if (!await bridge.IsConnected())
            return NotConnectedMessage;

        if (string.IsNullOrEmpty(description) && string.IsNullOrEmpty(planJson))
            return "Provide either a description or a planJson. Description will be used to structure a plan; planJson will be executed directly.";

        if (!string.IsNullOrEmpty(planJson))
        {
            // Execute the provided plan directly
            GenerationPlan plan;
            try
            {
                plan = JsonSerializer.Deserialize<GenerationPlan>(planJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new JsonException("Deserialized to null");
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Invalid plan JSON: {ex.Message}"
                }, JsonOpts);
            }

            var execResult = await bridge.ExecuteGenerationPlan(plan);
            return JsonSerializer.Serialize(new
            {
                success = execResult.IsOk,
                mode = "execute",
                data = execResult.Data,
                error = execResult.Error
            }, JsonOpts);
        }

        // Description mode — send to bridge for plan generation
        var result = await bridge.SendCommand("generate_plan", new { description });
        return JsonSerializer.Serialize(new
        {
            success = result.IsOk,
            mode = "generate",
            description,
            data = result.Data,
            error = result.Error
        }, JsonOpts);
    }
}
