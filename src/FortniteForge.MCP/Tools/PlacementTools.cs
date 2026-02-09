using FortniteForge.Core.Models;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for placing and scattering actors in levels.
/// Uses the clone-and-modify pattern — requires a template actor already in the level.
/// </summary>
[McpServerToolType]
public class PlacementTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Preview cloning an existing actor to a new location. " +
        "The source actor must already exist in the level (place one manually in UEFN as a template). " +
        "Returns what will happen without modifying anything.")]
    public string preview_clone_actor(
        ActorPlacementService placementService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Name of the existing actor to clone (e.g., 'StaticMeshActor_3')")] string sourceActorName,
        [Description("X coordinate for the clone")] float x,
        [Description("Y coordinate for the clone")] float y,
        [Description("Z coordinate for the clone")] float z,
        [Description("Optional yaw rotation in degrees")] float yaw = 0)
    {
        var rotation = yaw != 0 ? new Vector3Info(0, yaw, 0) : null;
        var preview = placementService.PreviewCloneActor(
            levelPath, sourceActorName,
            new Vector3Info(x, y, z), rotation);

        return JsonSerializer.Serialize(preview, JsonOpts);
    }

    [McpServerTool, Description(
        "Clone an existing actor and place the copy at a new location. " +
        "Creates a backup before modifying. The source actor stays untouched.")]
    public string clone_actor(
        ActorPlacementService placementService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Name of the existing actor to clone")] string sourceActorName,
        [Description("X coordinate for the clone")] float x,
        [Description("Y coordinate for the clone")] float y,
        [Description("Z coordinate for the clone")] float z,
        [Description("Optional yaw rotation in degrees")] float yaw = 0,
        [Description("Optional uniform scale factor (1.0 = normal)")] float scale = 1.0f)
    {
        var rotation = yaw != 0 ? new Vector3Info(0, yaw, 0) : null;
        var scaleVec = scale != 1.0f ? new Vector3Info(scale, scale, scale) : null;

        var result = placementService.CloneActor(
            levelPath, sourceActorName,
            new Vector3Info(x, y, z), rotation, scaleVec);

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    [McpServerTool, Description(
        "Preview scatter-placing multiple copies of an actor across an area. " +
        "Great for generating forests, rock fields, prop clusters, etc. " +
        "The source actor must already exist in the level as a template. " +
        "Returns the planned positions WITHOUT modifying anything.")]
    public string preview_scatter_place(
        ActorPlacementService placementService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Name of the actor to scatter-copy")] string sourceActorName,
        [Description("Center X of the scatter area")] float centerX,
        [Description("Center Y of the scatter area")] float centerY,
        [Description("Center Z (height) of the scatter area")] float centerZ,
        [Description("Width of the scatter area (X axis)")] float areaWidth,
        [Description("Length of the scatter area (Y axis)")] float areaLength,
        [Description("Number of copies to place")] int count,
        [Description("Minimum distance between copies (default 100)")] float minSpacing = 100f,
        [Description("Randomize yaw rotation (default true)")] bool randomRotation = true,
        [Description("Minimum random scale factor (default 0.8)")] float scaleMin = 0.8f,
        [Description("Maximum random scale factor (default 1.2)")] float scaleMax = 1.2f)
    {
        var result = placementService.PreviewScatterPlace(
            levelPath, sourceActorName,
            new Vector3Info(centerX, centerY, centerZ),
            areaWidth, areaLength, count,
            minSpacing, randomRotation, scaleMin, scaleMax);

        return JsonSerializer.Serialize(new
        {
            result.SourceActor,
            result.Count,
            result.Description,
            result.IsBlocked,
            result.BlockReason,
            instancePreview = result.Instances.Select((inst, i) => new
            {
                index = i,
                location = $"({inst.Location.X:F0}, {inst.Location.Y:F0}, {inst.Location.Z:F0})",
                rotation = $"({inst.Rotation.X:F0}, {inst.Rotation.Y:F0}, {inst.Rotation.Z:F0})",
                scale = $"{inst.Scale.X:F2}"
            }),
            nextStep = result.IsBlocked
                ? "BLOCKED. See blockReason."
                : $"Call apply_scatter_place with these instances to place {count} copies."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Apply a scatter placement — places all instances from a preview. " +
        "Creates a backup before modifying.")]
    public string apply_scatter_place(
        ActorPlacementService placementService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Name of the actor to scatter-copy")] string sourceActorName,
        [Description("Center X of the scatter area")] float centerX,
        [Description("Center Y of the scatter area")] float centerY,
        [Description("Center Z (height)")] float centerZ,
        [Description("Width of the scatter area")] float areaWidth,
        [Description("Length of the scatter area")] float areaLength,
        [Description("Number of copies to place")] int count,
        [Description("Minimum distance between copies")] float minSpacing = 100f,
        [Description("Randomize yaw rotation")] bool randomRotation = true,
        [Description("Minimum random scale")] float scaleMin = 0.8f,
        [Description("Maximum random scale")] float scaleMax = 1.2f)
    {
        // Generate the scatter positions
        var preview = placementService.PreviewScatterPlace(
            levelPath, sourceActorName,
            new Vector3Info(centerX, centerY, centerZ),
            areaWidth, areaLength, count,
            minSpacing, randomRotation, scaleMin, scaleMax);

        if (preview.IsBlocked)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                blocked = true,
                reason = preview.BlockReason
            }, JsonOpts);
        }

        // Apply the placement
        var result = placementService.ApplyScatterPlace(
            levelPath, sourceActorName, preview.Instances);

        return JsonSerializer.Serialize(new
        {
            success = result.PlacedCount == result.Count,
            result.Description,
            result.PlacedCount,
            totalRequested = result.Count,
            result.BackupPath,
            errors = result.Errors
        }, JsonOpts);
    }
}
