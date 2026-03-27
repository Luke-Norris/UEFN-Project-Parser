using WellVersed.Core.Config;
using WellVersed.Core.Models;
using WellVersed.Core.Safety;
using Microsoft.Extensions.Logging;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Handles all asset modifications with mandatory safety checks, dry-run support, and backups.
///
/// Modification flow:
///   1. Client calls PreviewModification() — gets a dry-run showing what will change
///   2. Client reviews the preview
///   3. Client calls ApplyModification() with the preview ID to execute
///
/// Modifications are NEVER applied without going through this flow.
/// </summary>
public class ModificationService
{
    private readonly WellVersedConfig _config;
    private readonly AssetService _assetService;
    private readonly BackupService _backupService;
    private readonly AssetGuard _guard;
    private readonly SafeFileAccess _fileAccess;
    private readonly DigestService _digestService;
    private readonly ActorPlacementService _placementService;
    private readonly AssetValidator _validator;
    private readonly ILogger<ModificationService> _logger;

    // Pending previews awaiting approval
    private readonly Dictionary<string, (ModificationRequest Request, ModificationPreview Preview)> _pendingPreviews = new();

    public ModificationService(
        WellVersedConfig config,
        AssetService assetService,
        BackupService backupService,
        AssetGuard guard,
        SafeFileAccess fileAccess,
        DigestService digestService,
        ActorPlacementService placementService,
        AssetValidator validator,
        ILogger<ModificationService> logger)
    {
        _config = config;
        _assetService = assetService;
        _backupService = backupService;
        _guard = guard;
        _fileAccess = fileAccess;
        _digestService = digestService;
        _placementService = placementService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Step 1: Preview a modification without applying it.
    /// Returns a detailed preview of what will change.
    /// </summary>
    public ModificationPreview PreviewModification(ModificationRequest request)
    {
        var preview = new ModificationPreview
        {
            RequestId = request.Id,
            AffectedFiles = new List<string> { request.AssetPath }
        };

        // Safety check
        var safetyResult = _guard.CanModify(request.AssetPath, _fileAccess);
        if (!safetyResult.IsAllowed)
        {
            preview.IsSafe = false;
            preview.BlockReasons.AddRange(safetyResult.Reasons);
            preview.Description = $"BLOCKED: {string.Join("; ", safetyResult.Reasons)}";
            return preview;
        }

        try
        {
            switch (request.Type)
            {
                case ModificationType.SetProperty:
                    PreviewSetProperty(request, preview);
                    break;
                case ModificationType.AddDevice:
                    PreviewAddDevice(request, preview);
                    break;
                case ModificationType.RemoveDevice:
                    PreviewRemoveDevice(request, preview);
                    break;
                case ModificationType.WireDevices:
                    PreviewWireDevices(request, preview);
                    break;
                case ModificationType.UnwireDevices:
                    PreviewUnwireDevices(request, preview);
                    break;
                case ModificationType.DuplicateDevice:
                    PreviewDuplicateDevice(request, preview);
                    break;
                default:
                    preview.IsSafe = false;
                    preview.BlockReasons.Add($"Unknown modification type: {request.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add($"Error during preview: {ex.Message}");
            _logger.LogError(ex, "Failed to preview modification {Id}", request.Id);
        }

        if (preview.IsSafe)
        {
            _pendingPreviews[request.Id] = (request, preview);
        }

        return preview;
    }

    /// <summary>
    /// Step 2: Apply a previously previewed modification.
    /// The requestId must match a valid, approved preview.
    /// </summary>
    public ModificationResult ApplyModification(string requestId)
    {
        if (!_pendingPreviews.TryGetValue(requestId, out var pending))
        {
            return new ModificationResult
            {
                RequestId = requestId,
                Success = false,
                Message = $"No pending preview found for ID '{requestId}'. Run PreviewModification first.",
                Errors = new List<string> { "Preview not found or expired." }
            };
        }

        var (request, preview) = pending;
        _pendingPreviews.Remove(requestId);

        if (!preview.IsSafe)
        {
            return new ModificationResult
            {
                RequestId = requestId,
                Success = false,
                Message = "Cannot apply an unsafe modification.",
                Errors = preview.BlockReasons
            };
        }

        var result = new ModificationResult { RequestId = requestId };

        try
        {
            // Create backup
            string? backupPath = null;
            if (_config.AutoBackup && File.Exists(request.AssetPath))
            {
                backupPath = _backupService.CreateBackup(request.AssetPath);
                result.BackupPath = backupPath;
            }

            // Apply the modification
            switch (request.Type)
            {
                case ModificationType.SetProperty:
                    ApplySetProperty(request);
                    break;
                case ModificationType.AddDevice:
                    ApplyAddDevice(request);
                    break;
                case ModificationType.RemoveDevice:
                    ApplyRemoveDevice(request);
                    break;
                case ModificationType.WireDevices:
                    ApplyWireDevices(request);
                    break;
                case ModificationType.UnwireDevices:
                    ApplyUnwireDevices(request);
                    break;
                case ModificationType.DuplicateDevice:
                    ApplyDuplicateDevice(request);
                    break;
            }

            result.Success = true;
            result.Message = $"Modification applied successfully. Backup at: {backupPath ?? "N/A"}";
            result.ModifiedFiles.Add(request.AssetPath);

            _logger.LogInformation("Applied modification {Id}: {Type} on {Path}",
                requestId, request.Type, request.AssetPath);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Modification failed: {ex.Message}";
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Failed to apply modification {Id}", requestId);

            // If we have a backup and the modification failed, offer restore guidance
            if (result.BackupPath != null)
            {
                result.Message += $" A backup was created at: {result.BackupPath}";
            }
        }

        return result;
    }

    /// <summary>
    /// Lists all pending (unapplied) previews.
    /// </summary>
    public List<ModificationPreview> ListPendingPreviews()
    {
        return _pendingPreviews.Values.Select(p => p.Preview).ToList();
    }

    /// <summary>
    /// Cancels a pending preview.
    /// </summary>
    public bool CancelPreview(string requestId)
    {
        return _pendingPreviews.Remove(requestId);
    }

    // ========= Preview Methods =========

    private void PreviewSetProperty(ModificationRequest request, ModificationPreview preview)
    {
        if (string.IsNullOrEmpty(request.TargetObject))
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add("TargetObject (export/actor name) is required for SetProperty.");
            return;
        }

        if (string.IsNullOrEmpty(request.PropertyName))
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add("PropertyName is required for SetProperty.");
            return;
        }

        var asset = _assetService.OpenAsset(request.AssetPath);
        var targetExport = FindExport(asset, request.TargetObject);

        if (targetExport == null)
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add($"Export/actor '{request.TargetObject}' not found in asset.");
            return;
        }

        var existingProp = FindProperty(targetExport, request.PropertyName);
        var oldValue = existingProp != null ? FormatPropertyValue(existingProp) : "(not set)";

        preview.IsSafe = true;
        preview.Description = $"Change property '{request.PropertyName}' on '{request.TargetObject}'";
        preview.Changes.Add(new ChangeDetail
        {
            What = $"{request.TargetObject}.{request.PropertyName}",
            OldValue = oldValue,
            NewValue = request.NewValue ?? "null"
        });

        if (existingProp == null)
        {
            preview.Warnings.Add($"Property '{request.PropertyName}' does not currently exist on this export. It will be created.");
        }
    }

    private void PreviewAddDevice(ModificationRequest request, ModificationPreview preview)
    {
        if (string.IsNullOrEmpty(request.DeviceClass))
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add("DeviceClass is required for AddDevice.");
            return;
        }

        preview.IsSafe = true;
        preview.Description = $"Add new '{request.DeviceClass}' device to level";
        preview.Changes.Add(new ChangeDetail
        {
            What = $"New actor: {request.DeviceClass}",
            NewValue = $"At location: {request.Location ?? new Vector3Info()}"
        });

        if (request.InitialProperties != null)
        {
            foreach (var prop in request.InitialProperties)
            {
                preview.Changes.Add(new ChangeDetail
                {
                    What = $"Initial property: {prop.Key}",
                    NewValue = prop.Value
                });
            }
        }

        preview.Warnings.Add("Adding devices to levels is an advanced operation. Verify the result in UEFN after applying.");
    }

    private void PreviewRemoveDevice(ModificationRequest request, ModificationPreview preview)
    {
        if (string.IsNullOrEmpty(request.TargetObject))
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add("TargetObject (actor name) is required for RemoveDevice.");
            return;
        }

        var asset = _assetService.OpenAsset(request.AssetPath);
        var target = FindExport(asset, request.TargetObject);

        if (target == null)
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add($"Actor '{request.TargetObject}' not found in level.");
            return;
        }

        preview.IsSafe = true;
        preview.Description = $"Remove actor '{request.TargetObject}' from level";
        preview.Changes.Add(new ChangeDetail
        {
            What = $"Remove: {request.TargetObject}",
            OldValue = target.GetExportClassType()?.ToString() ?? "Unknown",
            NewValue = "(deleted)"
        });
        preview.Warnings.Add("Removing devices may break wiring connections from other devices.");
    }

    private void PreviewWireDevices(ModificationRequest request, ModificationPreview preview)
    {
        preview.IsSafe = true;
        preview.Description = $"Wire '{request.SourceDevice}' → '{request.TargetDevice}'";
        preview.Changes.Add(new ChangeDetail
        {
            What = "New connection",
            NewValue = $"{request.SourceDevice}.{request.OutputEvent} → {request.TargetDevice}.{request.InputAction}"
        });
        preview.Warnings.Add("Wiring format depends on the specific device types. Verify in UEFN.");
    }

    private void PreviewUnwireDevices(ModificationRequest request, ModificationPreview preview)
    {
        preview.IsSafe = true;
        preview.Description = $"Remove wire from '{request.SourceDevice}' to '{request.TargetDevice}'";
        preview.Changes.Add(new ChangeDetail
        {
            What = "Remove connection",
            OldValue = $"{request.SourceDevice}.{request.OutputEvent} → {request.TargetDevice}.{request.InputAction}",
            NewValue = "(disconnected)"
        });
    }

    private void PreviewDuplicateDevice(ModificationRequest request, ModificationPreview preview)
    {
        if (string.IsNullOrEmpty(request.TargetObject))
        {
            preview.IsSafe = false;
            preview.BlockReasons.Add("TargetObject (actor to duplicate) is required.");
            return;
        }

        preview.IsSafe = true;
        preview.Description = $"Duplicate actor '{request.TargetObject}'";
        preview.Changes.Add(new ChangeDetail
        {
            What = $"Duplicate: {request.TargetObject}",
            NewValue = $"Copy at {request.Location ?? new Vector3Info()}"
        });
        preview.Warnings.Add("Duplicated devices will need their wiring configured separately.");
    }

    // ========= Apply Methods =========

    private void ApplySetProperty(ModificationRequest request)
    {
        var (asset, writePath) = _assetService.OpenAssetForWrite(request.AssetPath);
        var target = FindExport(asset, request.TargetObject!)
                     ?? throw new InvalidOperationException($"Export '{request.TargetObject}' not found.");

        if (target is NormalExport normalExport)
        {
            var prop = FindProperty(normalExport, request.PropertyName!);
            if (prop != null)
            {
                SetPropertyValue(asset, prop, request.NewValue!);
            }
            else
            {
                _logger.LogWarning("Property '{Prop}' not found. Creating new properties is not yet supported for arbitrary types.",
                    request.PropertyName);
                throw new NotSupportedException(
                    $"Property '{request.PropertyName}' does not exist on '{request.TargetObject}'. " +
                    "Creating new properties requires knowing the exact type. Use an existing property name.");
            }
        }

        var snapshot = _validator.CaptureSnapshot(asset, $"SetProperty:{request.PropertyName}");
        asset.Write(writePath);
        var validation = _validator.Validate(writePath, snapshot);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Post-write validation failed: {string.Join("; ", validation.Errors)}");
        _logger.LogInformation("Wrote and validated modified asset: {Path}", writePath);
    }

    private void ApplyAddDevice(ModificationRequest request)
    {
        // AddDevice uses the clone-and-modify pattern via ActorPlacementService.
        // It requires a SourceDevice (template actor already in the level) to clone from.
        // If no source is specified, we explain the requirement.
        if (string.IsNullOrEmpty(request.SourceDevice))
        {
            throw new InvalidOperationException(
                "AddDevice requires a 'SourceDevice' — an existing actor in the level to clone from. " +
                "Place one instance of the device manually in UEFN, then reference it as the source. " +
                "WellVersed will clone it with your specified properties and location.");
        }

        var result = _placementService.CloneActor(
            request.AssetPath,
            request.SourceDevice,
            request.Location ?? new Vector3Info());

        if (!result.Success)
            throw new InvalidOperationException(result.Message);
    }

    private void ApplyRemoveDevice(ModificationRequest request)
    {
        var (asset, writePath) = _assetService.OpenAssetForWrite(request.AssetPath);
        var targetIndex = -1;

        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName?.ToString() == request.TargetObject)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
            throw new InvalidOperationException($"Actor '{request.TargetObject}' not found.");

        // Find child components owned by this actor
        var targetRef = FPackageIndex.FromExport(targetIndex);
        var childIndices = new List<int>();
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].OuterIndex == targetRef)
                childIndices.Add(i);
        }

        // Deregister from LevelExport.Actors
        var levelExport = asset.Exports.OfType<LevelExport>().FirstOrDefault();
        levelExport?.Actors.Remove(targetRef);

        var snapshot = _validator.CaptureSnapshot(asset, $"RemoveDevice:{request.TargetObject}");

        // Remove exports in reverse order to preserve indices during removal
        var toRemove = new List<int> { targetIndex };
        toRemove.AddRange(childIndices);
        foreach (var idx in toRemove.OrderByDescending(i => i))
        {
            asset.Exports.RemoveAt(idx);
        }

        asset.Write(writePath);
        var validation = _validator.Validate(writePath, snapshot);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Post-write validation failed: {string.Join("; ", validation.Errors)}");
        _logger.LogInformation("Wrote and validated modified asset: {Path}", writePath);
    }

    private void ApplyWireDevices(ModificationRequest request)
    {
        if (string.IsNullOrEmpty(request.SourceDevice) || string.IsNullOrEmpty(request.TargetDevice))
            throw new InvalidOperationException("SourceDevice and TargetDevice are required for wiring.");
        if (string.IsNullOrEmpty(request.OutputEvent) || string.IsNullOrEmpty(request.InputAction))
            throw new InvalidOperationException("OutputEvent and InputAction are required for wiring.");

        var (asset, writePath) = _assetService.OpenAssetForWrite(request.AssetPath);

        // Find source and target device exports
        var sourceExport = FindExport(asset, request.SourceDevice)
            ?? throw new InvalidOperationException($"Source device '{request.SourceDevice}' not found.");
        var targetExport = FindExport(asset, request.TargetDevice)
            ?? throw new InvalidOperationException($"Target device '{request.TargetDevice}' not found.");

        int targetIndex = -1;
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName?.ToString() == request.TargetDevice)
            { targetIndex = i; break; }
        }

        // Find or create the binding array property on the source device
        var bindingArrayName = FindBindingArrayProperty(sourceExport);
        ArrayPropertyData? bindingArray = null;

        if (bindingArrayName != null)
        {
            bindingArray = sourceExport.Data
                .OfType<ArrayPropertyData>()
                .FirstOrDefault(a => a.Name?.ToString() == bindingArrayName);
        }

        if (bindingArray == null)
        {
            // Create a new binding array — use "EventBindings" as default
            var arrayName = bindingArrayName ?? "EventBindings";
            bindingArray = new ArrayPropertyData(FName.FromString(asset, arrayName))
            {
                ArrayType = FName.FromString(asset, "StructProperty"),
                Value = Array.Empty<PropertyData>()
            };
            sourceExport.Data.Add(bindingArray);
        }

        // Build the wiring struct entry
        var wiringEntry = new StructPropertyData(FName.FromString(asset, "EventBindings"))
        {
            StructType = FName.FromString(asset, "DeviceEventBinding"),
            Value = new List<PropertyData>
            {
                new NamePropertyData(FName.FromString(asset, "OutputEvent"))
                    { Value = FName.FromString(asset, request.OutputEvent) },
                new ObjectPropertyData(FName.FromString(asset, "TargetDevice"))
                    { Value = FPackageIndex.FromExport(targetIndex) },
                new NamePropertyData(FName.FromString(asset, "InputAction"))
                    { Value = FName.FromString(asset, request.InputAction) },
            }
        };

        // Add to the array
        var existing = bindingArray.Value?.ToList() ?? new List<PropertyData>();
        existing.Add(wiringEntry);
        bindingArray.Value = existing.ToArray();

        var snapshot = _validator.CaptureSnapshot(asset, $"WireDevices:{request.SourceDevice}→{request.TargetDevice}");
        asset.Write(writePath);
        var validation = _validator.Validate(writePath, snapshot);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Post-write validation failed: {string.Join("; ", validation.Errors)}");
        _logger.LogInformation("Wired {Source}.{Event} → {Target}.{Action}",
            request.SourceDevice, request.OutputEvent, request.TargetDevice, request.InputAction);
    }

    private void ApplyUnwireDevices(ModificationRequest request)
    {
        if (string.IsNullOrEmpty(request.SourceDevice) || string.IsNullOrEmpty(request.TargetDevice))
            throw new InvalidOperationException("SourceDevice and TargetDevice are required.");

        var (asset, writePath) = _assetService.OpenAssetForWrite(request.AssetPath);
        var sourceExport = FindExport(asset, request.SourceDevice)
            ?? throw new InvalidOperationException($"Source device '{request.SourceDevice}' not found.");

        // Find the binding array
        var bindingArrayName = FindBindingArrayProperty(sourceExport);
        if (bindingArrayName == null)
            throw new InvalidOperationException($"No binding properties found on '{request.SourceDevice}'.");

        var bindingArray = sourceExport.Data
            .OfType<ArrayPropertyData>()
            .FirstOrDefault(a => a.Name?.ToString() == bindingArrayName);

        if (bindingArray?.Value == null || bindingArray.Value.Length == 0)
            throw new InvalidOperationException("No wiring entries to remove.");

        // Find target export index
        int targetIndex = -1;
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].ObjectName?.ToString() == request.TargetDevice)
            { targetIndex = i; break; }
        }

        // Remove entries that match the target device
        var remaining = bindingArray.Value.Where(entry =>
        {
            if (entry is StructPropertyData structEntry)
            {
                foreach (var prop in structEntry.Value)
                {
                    if (prop is ObjectPropertyData objProp && objProp.Value.IsExport())
                    {
                        if (objProp.Value.Index - 1 == targetIndex)
                            return false; // Remove this entry
                    }
                }
            }
            return true;
        }).ToArray();

        if (remaining.Length == bindingArray.Value.Length)
            throw new InvalidOperationException($"No wiring found from '{request.SourceDevice}' to '{request.TargetDevice}'.");

        bindingArray.Value = remaining;

        var snapshot = _validator.CaptureSnapshot(asset, $"UnwireDevices:{request.SourceDevice}→{request.TargetDevice}");
        asset.Write(writePath);
        var validation = _validator.Validate(writePath, snapshot);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Post-write validation failed: {string.Join("; ", validation.Errors)}");
        _logger.LogInformation("Unwired {Source} → {Target}", request.SourceDevice, request.TargetDevice);
    }

    private static string? FindBindingArrayProperty(NormalExport export)
    {
        // Search for existing array properties that look like wiring containers
        foreach (var prop in export.Data)
        {
            if (prop is ArrayPropertyData)
            {
                var name = prop.Name?.ToString() ?? "";
                if (name.Contains("Binding", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Wire", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Link", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Event", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }
        return null;
    }

    private void ApplyDuplicateDevice(ModificationRequest request)
    {
        if (string.IsNullOrEmpty(request.TargetObject))
            throw new InvalidOperationException("TargetObject (actor to duplicate) is required.");

        var result = _placementService.CloneActor(
            request.AssetPath,
            request.TargetObject,
            request.Location ?? new Vector3Info());

        if (!result.Success)
            throw new InvalidOperationException(result.Message);
    }

    // ========= Helpers =========

    private static NormalExport? FindExport(UAsset asset, string objectName)
    {
        return asset.Exports
            .OfType<NormalExport>()
            .FirstOrDefault(e => e.ObjectName?.ToString()?.Equals(objectName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static PropertyData? FindProperty(NormalExport export, string propertyName)
    {
        return export.Data.FirstOrDefault(p =>
            p.Name?.ToString()?.Equals(propertyName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void SetPropertyValue(UAsset asset, PropertyData prop, string newValue)
    {
        switch (prop)
        {
            case BoolPropertyData boolProp:
                boolProp.Value = bool.Parse(newValue);
                break;
            case IntPropertyData intProp:
                intProp.Value = int.Parse(newValue);
                break;
            case FloatPropertyData floatProp:
                floatProp.Value = float.Parse(newValue);
                break;
            case DoublePropertyData doubleProp:
                doubleProp.Value = double.Parse(newValue);
                break;
            case StrPropertyData strProp:
                strProp.Value = FString.FromString(newValue);
                break;
            case NamePropertyData nameProp:
                nameProp.Value = FName.FromString(asset, newValue);
                break;
            case EnumPropertyData enumProp:
                enumProp.Value = FName.FromString(asset, newValue);
                break;
            case BytePropertyData byteProp:
                if (byte.TryParse(newValue, out var byteVal))
                    byteProp.Value = byteVal;
                else
                    byteProp.EnumValue = FName.FromString(asset, newValue);
                break;
            default:
                throw new NotSupportedException(
                    $"Cannot set value on property type '{prop.GetType().Name}'. " +
                    $"Supported types: Bool, Int, Float, Double, String, Name, Enum, Byte.");
        }
    }

    private static string FormatPropertyValue(PropertyData prop)
    {
        return prop switch
        {
            BoolPropertyData b => b.Value.ToString(),
            IntPropertyData i => i.Value.ToString(),
            FloatPropertyData f => f.Value.ToString(),
            DoublePropertyData d => d.Value.ToString(),
            StrPropertyData s => s.Value?.ToString() ?? "null",
            NamePropertyData n => n.Value?.ToString() ?? "null",
            EnumPropertyData e => e.Value?.ToString() ?? "null",
            _ => prop.ToString() ?? "unknown"
        };
    }
}
