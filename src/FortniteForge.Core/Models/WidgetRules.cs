namespace FortniteForge.Core.Models;

/// <summary>
/// Static containment rules for UEFN UMG widgets.
/// Derived from analyzing 883 real Widget Blueprints across 61 UEFN projects.
/// Encodes which widget types can be children of which slot types,
/// which containers are single-child, and slot class mappings.
/// </summary>
public static class WidgetRules
{
    /// <summary>
    /// Containers that accept only a single child widget.
    /// </summary>
    public static readonly HashSet<WidgetType> SingleChildContainers = new()
    {
        WidgetType.SizeBox,
        WidgetType.ScaleBox
    };

    /// <summary>
    /// Verified child types per slot, from real UEFN projects.
    /// Key is the slot class name, value is the set of allowed child WidgetTypes.
    /// </summary>
    private static readonly Dictionary<string, HashSet<WidgetType>> AllowedChildrenBySlot = new()
    {
        ["CanvasPanelSlot"] = new HashSet<WidgetType>
        {
            WidgetType.CanvasPanel, WidgetType.Image, WidgetType.Overlay,
            WidgetType.TextBlock, WidgetType.ButtonLoud, WidgetType.ButtonQuiet,
            WidgetType.StackBox, WidgetType.SizeBox, WidgetType.GridPanel
        },
        ["OverlaySlot"] = new HashSet<WidgetType>
        {
            WidgetType.Image, WidgetType.Overlay, WidgetType.StackBox,
            WidgetType.TextBlock, WidgetType.ButtonRegular
        },
        ["StackBoxSlot"] = new HashSet<WidgetType>
        {
            WidgetType.Image, WidgetType.Overlay, WidgetType.StackBox,
            WidgetType.TextBlock, WidgetType.ButtonQuiet
        },
        ["GridSlot"] = new HashSet<WidgetType>
        {
            WidgetType.Image
        },
        ["SizeBoxSlot"] = new HashSet<WidgetType>
        {
            WidgetType.Image, WidgetType.Overlay, WidgetType.SizeBox
        },
        ["ScaleBoxSlot"] = new HashSet<WidgetType>
        {
            WidgetType.CanvasPanel
        }
    };

    /// <summary>
    /// Maps a parent widget type to its slot class name.
    /// Extracted from WidgetBlueprintBuilder.GetSlotClassName.
    /// </summary>
    public static string GetSlotType(WidgetType parentType) => parentType switch
    {
        WidgetType.CanvasPanel => "CanvasPanelSlot",
        WidgetType.Overlay => "OverlaySlot",
        WidgetType.StackBox => "StackBoxSlot",
        WidgetType.SizeBox => "SizeBoxSlot",
        WidgetType.ScaleBox => "ScaleBoxSlot",
        WidgetType.GridPanel => "GridSlot",
        _ => "CanvasPanelSlot"
    };

    /// <summary>
    /// Whether a widget type is a container (has a Slots array and can hold children).
    /// Leaf types (Image, TextBlock, Buttons) appear as children but don't hold their own.
    /// </summary>
    public static bool IsContainer(WidgetType type) =>
        type is WidgetType.CanvasPanel or WidgetType.Overlay or WidgetType.StackBox
            or WidgetType.SizeBox or WidgetType.ScaleBox or WidgetType.GridPanel;

    /// <summary>
    /// Whether a parent widget type can contain a given child widget type,
    /// based on the verified child types per slot from real UEFN projects.
    /// </summary>
    public static bool CanContain(WidgetType parent, WidgetType child)
    {
        var slotType = GetSlotType(parent);
        return AllowedChildrenBySlot.TryGetValue(slotType, out var allowed) && allowed.Contains(child);
    }
}
