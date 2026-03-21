using FortniteForge.Core.Models;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for modifying UEFN project assets.
/// All modifications follow a mandatory two-step flow:
///   1. Preview (dry-run) — shows exactly what will change
///   2. Apply — executes the change after review
///
/// NEVER modifies cooked/Epic assets. ALWAYS creates backups.
/// </summary>
[McpServerToolType]
public class ModifyTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "STEP 1: Preview a property change on a device or asset. " +
        "Returns what will change WITHOUT modifying anything. " +
        "You MUST call apply_modification with the returned requestId to actually make the change.")]
    public string preview_set_property(
        ModificationService modService,
        [Description("Path to the .uasset or .umap file")] string assetPath,
        [Description("The export/actor name containing the property")] string targetObject,
        [Description("The property name to change")] string propertyName,
        [Description("The new value to set")] string newValue)
    {
        var request = new ModificationRequest
        {
            Type = ModificationType.SetProperty,
            AssetPath = assetPath,
            TargetObject = targetObject,
            PropertyName = propertyName,
            NewValue = newValue
        };

        var preview = modService.PreviewModification(request);
        return JsonSerializer.Serialize(new
        {
            preview.RequestId,
            preview.IsSafe,
            preview.Description,
            preview.AffectedFiles,
            preview.Changes,
            preview.Warnings,
            preview.BlockReasons,
            nextStep = preview.IsSafe
                ? $"Call apply_modification with requestId '{preview.RequestId}' to apply this change."
                : "This modification was BLOCKED. See blockReasons above."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 1: Preview adding a new device to a level. " +
        "Returns what will change WITHOUT modifying anything.")]
    public string preview_add_device(
        ModificationService modService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Device class to add (e.g., 'BP_TriggerDevice_C')")] string deviceClass,
        [Description("X coordinate for placement")] float x = 0,
        [Description("Y coordinate for placement")] float y = 0,
        [Description("Z coordinate for placement")] float z = 0,
        [Description("Optional initial properties as JSON object (e.g., '{\"Duration\": \"5.0\"}')")] string? initialProperties = null)
    {
        var request = new ModificationRequest
        {
            Type = ModificationType.AddDevice,
            AssetPath = levelPath,
            DeviceClass = deviceClass,
            Location = new Vector3Info(x, y, z)
        };

        if (!string.IsNullOrEmpty(initialProperties))
        {
            try
            {
                request.InitialProperties = JsonSerializer.Deserialize<Dictionary<string, string>>(initialProperties);
            }
            catch
            {
                return "Error: initialProperties must be a valid JSON object with string values.";
            }
        }

        var preview = modService.PreviewModification(request);
        return JsonSerializer.Serialize(new
        {
            preview.RequestId,
            preview.IsSafe,
            preview.Description,
            preview.Changes,
            preview.Warnings,
            preview.BlockReasons,
            nextStep = preview.IsSafe
                ? $"Call apply_modification with requestId '{preview.RequestId}' to apply."
                : "BLOCKED. See blockReasons."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 1: Preview removing a device from a level.")]
    public string preview_remove_device(
        ModificationService modService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Actor name of the device to remove")] string actorName)
    {
        var request = new ModificationRequest
        {
            Type = ModificationType.RemoveDevice,
            AssetPath = levelPath,
            TargetObject = actorName
        };

        var preview = modService.PreviewModification(request);
        return JsonSerializer.Serialize(new
        {
            preview.RequestId,
            preview.IsSafe,
            preview.Description,
            preview.Changes,
            preview.Warnings,
            preview.BlockReasons,
            nextStep = preview.IsSafe
                ? $"Call apply_modification with requestId '{preview.RequestId}' to apply."
                : "BLOCKED. See blockReasons."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 1: Preview wiring two devices together (signal connection).")]
    public string preview_wire_devices(
        ModificationService modService,
        [Description("Path to the .umap level file")] string levelPath,
        [Description("Source device actor name")] string sourceDevice,
        [Description("Output event name on source (e.g., 'OnTriggered')")] string outputEvent,
        [Description("Target device actor name")] string targetDevice,
        [Description("Input action name on target (e.g., 'Enable')")] string inputAction)
    {
        var request = new ModificationRequest
        {
            Type = ModificationType.WireDevices,
            AssetPath = levelPath,
            SourceDevice = sourceDevice,
            OutputEvent = outputEvent,
            TargetDevice = targetDevice,
            InputAction = inputAction
        };

        var preview = modService.PreviewModification(request);
        return JsonSerializer.Serialize(new
        {
            preview.RequestId,
            preview.IsSafe,
            preview.Description,
            preview.Changes,
            preview.Warnings,
            preview.BlockReasons,
            nextStep = preview.IsSafe
                ? $"Call apply_modification with requestId '{preview.RequestId}' to apply."
                : "BLOCKED. See blockReasons."
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "STEP 2: Applies a previously previewed modification. " +
        "Requires a valid requestId from a preview call. " +
        "Creates a backup before making changes. " +
        "Changes are staged for review — approve in the WellVersed app before they're applied to project files.")]
    public string apply_modification(
        ModificationService modService,
        [Description("The requestId returned from a preview call")] string requestId)
    {
        var result = modService.ApplyModification(requestId);
        return JsonSerializer.Serialize(new
        {
            result.RequestId,
            result.Success,
            result.Message,
            result.BackupPath,
            result.ModifiedFiles,
            result.Errors
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Lists all pending modification previews that haven't been applied yet.")]
    public string list_pending_modifications(ModificationService modService)
    {
        var pending = modService.ListPendingPreviews();

        if (pending.Count == 0)
            return "No pending modifications.";

        return JsonSerializer.Serialize(new
        {
            count = pending.Count,
            pending = pending.Select(p => new
            {
                p.RequestId,
                p.Description,
                p.IsSafe,
                p.Changes
            })
        }, JsonOpts);
    }

    [McpServerTool, Description("Cancels a pending modification preview.")]
    public string cancel_modification(
        ModificationService modService,
        [Description("The requestId to cancel")] string requestId)
    {
        var cancelled = modService.CancelPreview(requestId);
        return cancelled
            ? $"Modification '{requestId}' cancelled."
            : $"No pending modification found with ID '{requestId}'.";
    }
}
