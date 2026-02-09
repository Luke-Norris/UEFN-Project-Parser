namespace FortniteForge.Core.Models;

/// <summary>
/// Represents a Creative Device placed in a UEFN level.
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// The actor/object name in the level (e.g., "TriggerDevice_3").
    /// </summary>
    public string ActorName { get; set; } = "";

    /// <summary>
    /// The device class type (e.g., "BP_TriggerDevice_C", "MutatorZone_C").
    /// </summary>
    public string DeviceClass { get; set; } = "";

    /// <summary>
    /// Friendly device type name (e.g., "Trigger Device", "Mutator Zone").
    /// </summary>
    public string DeviceType { get; set; } = "";

    /// <summary>
    /// The label/display name set by the user in the editor.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// World location (X, Y, Z).
    /// </summary>
    public Vector3Info Location { get; set; } = new();

    /// <summary>
    /// World rotation (Pitch, Yaw, Roll).
    /// </summary>
    public Vector3Info Rotation { get; set; } = new();

    /// <summary>
    /// World scale (X, Y, Z).
    /// </summary>
    public Vector3Info Scale { get; set; } = new(1, 1, 1);

    /// <summary>
    /// Configurable properties on this device.
    /// </summary>
    public List<PropertyInfo> Properties { get; set; } = new();

    /// <summary>
    /// Signal connections / wiring from this device.
    /// </summary>
    public List<DeviceWiring> Wiring { get; set; } = new();

    /// <summary>
    /// The .umap file this device lives in.
    /// </summary>
    public string LevelPath { get; set; } = "";

    /// <summary>
    /// Whether this is a Verse-scripted device.
    /// </summary>
    public bool IsVerseDevice { get; set; }

    /// <summary>
    /// Path to the Verse class backing this device (if IsVerseDevice).
    /// </summary>
    public string? VerseClassPath { get; set; }
}

/// <summary>
/// Represents a signal/event wire between two devices.
/// </summary>
public class DeviceWiring
{
    /// <summary>
    /// The output event on the source device (e.g., "OnTriggered").
    /// </summary>
    public string OutputEvent { get; set; } = "";

    /// <summary>
    /// The target device actor name.
    /// </summary>
    public string TargetDevice { get; set; } = "";

    /// <summary>
    /// The input action on the target device (e.g., "Enable").
    /// </summary>
    public string InputAction { get; set; } = "";

    /// <summary>
    /// The channel used for this wiring (if applicable).
    /// </summary>
    public string? Channel { get; set; }
}

/// <summary>
/// Simple 3D vector.
/// </summary>
public class Vector3Info
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3Info() { }

    public Vector3Info(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
