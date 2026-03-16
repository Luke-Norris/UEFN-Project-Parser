using System.Text.Json.Serialization;

namespace FortniteForge.Core.Services;

/// <summary>
/// Comprehensive reference for UEFN's Verse widget API.
/// Provides widget type definitions, layout patterns, anchoring presets,
/// and reactive binding patterns for Claude to use when generating UI code.
/// </summary>
public static class VerseWidgetReference
{
    public static List<WidgetTypeRef> GetAllWidgets() => new()
    {
        // === Container Widgets ===
        new()
        {
            Name = "canvas",
            Category = "Container",
            Description = "Root container for UI. Positions child widgets using absolute anchors on screen. Every UI starts with a canvas.",
            Properties = new()
            {
                new("Slots", "[]canvas_slot", "Array of positioned child widgets")
            },
            UsagePattern = @"MyCanvas := canvas:
    Slots := array:
        canvas_slot:
            Widget := MyTextBlock
            Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.5}, Maximum := vector2{X := 0.5, Y := 0.5}}
            Alignment := vector2{X := 0.5, Y := 0.5}",
            Notes = "Canvas is the only widget that can be added to player_ui via AddWidget(). All UI trees start here."
        },
        new()
        {
            Name = "canvas_slot",
            Category = "Layout",
            Description = "Positions a widget within a canvas using screen-space anchors (0.0 = left/top, 1.0 = right/bottom).",
            Properties = new()
            {
                new("Widget", "widget", "The child widget to position"),
                new("Anchors", "anchors", "Screen position — Minimum and Maximum as vector2"),
                new("Offsets", "margin", "Pixel offsets from anchor position"),
                new("Alignment", "vector2", "Pivot point of the widget (0.5, 0.5 = centered on anchor)"),
                new("SizeToContent", "logic", "If true, widget sizes to its content"),
                new("ZOrder", "int", "Draw order (higher = on top)")
            },
            UsagePattern = @"canvas_slot:
    Widget := MyWidget
    Anchors := anchors{Minimum := vector2{X := 0.0, Y := 0.0}, Maximum := vector2{X := 1.0, Y := 1.0}}
    # This stretches the widget across the full screen",
            Notes = "Anchor values: 0.0 = left/top edge, 0.5 = center, 1.0 = right/bottom edge. For fixed-position elements, set Min = Max."
        },
        new()
        {
            Name = "overlay",
            Category = "Container",
            Description = "Stacks children on top of each other. All children fill the overlay's bounds. Good for layering backgrounds behind text.",
            Properties = new()
            {
                new("Slots", "[]overlay_slot", "Child widgets stacked in order")
            },
            UsagePattern = @"MyOverlay := overlay:
    Slots := array:
        overlay_slot{Widget := BackgroundBlock}
        overlay_slot{Widget := ForegroundText}",
            Notes = "First slot is bottom layer, last slot is top. Use for backgrounds, borders, or layered effects."
        },
        new()
        {
            Name = "stack_box",
            Category = "Container",
            Description = "Lays out children in a horizontal or vertical stack. The primary layout container for lists and rows.",
            Properties = new()
            {
                new("Orientation", "orientation", "orientation.Horizontal or orientation.Vertical"),
                new("Slots", "[]stack_box_slot", "Child widgets in stack order")
            },
            UsagePattern = @"Row := stack_box:
    Orientation := orientation.Horizontal
    Slots := array:
        stack_box_slot{Widget := LeftText}
        stack_box_slot{Widget := RightText}",
            Notes = "Use Vertical for lists/columns, Horizontal for rows. Nest them for grid layouts."
        },

        // === Display Widgets ===
        new()
        {
            Name = "text_block",
            Category = "Display",
            Description = "Displays text on screen. The most common widget. Supports dynamic updates via SetText().",
            Properties = new()
            {
                new("DefaultText", "message", "Initial text content — use StringToMessage() to convert strings"),
                new("DefaultTextColor", "color", "Text color as color{R:=, G:=, B:=, A:=}"),
                new("DefaultShadowColor", "color", "Drop shadow color"),
                new("DefaultShadowOffset", "vector2", "Shadow offset in pixels")
            },
            UsagePattern = @"ScoreText := text_block:
    DefaultText := StringToMessage(""Score: 0"")
    DefaultTextColor := color{R := 1.0, G := 1.0, B := 1.0, A := 1.0}

# Update later:
ScoreText.SetText(StringToMessage(""Score: {Points}""))",
            Notes = "Always use StringToMessage() to convert strings to message type. SetText() updates content at runtime."
        },
        new()
        {
            Name = "color_block",
            Category = "Display",
            Description = "A solid color rectangle. Used for backgrounds, health bars, progress indicators, and decorative elements.",
            Properties = new()
            {
                new("DefaultColor", "color", "Fill color as color{R:=, G:=, B:=, A:=}"),
                new("DefaultOpacity", "float", "Transparency 0.0 (invisible) to 1.0 (opaque)"),
                new("DefaultDesiredSize", "vector2", "Size in pixels when SizeToContent is false")
            },
            UsagePattern = @"HealthBar := color_block:
    DefaultColor := color{R := 0.2, G := 0.9, B := 0.3, A := 1.0}
    DefaultOpacity := 0.9

Background := color_block:
    DefaultColor := color{R := 0.0, G := 0.0, B := 0.0, A := 0.7}",
            Notes = "For health/progress bars: put two color_blocks in an overlay — background (dark) and fill (colored). Resize fill by rebuilding canvas with different anchors."
        },
        new()
        {
            Name = "texture_block",
            Category = "Display",
            Description = "Displays an image/texture. Reference textures from the project's Content directory.",
            Properties = new()
            {
                new("DefaultImage", "texture", "Texture asset reference"),
                new("DefaultDesiredSize", "vector2", "Display size in pixels")
            },
            UsagePattern = @"# Reference a texture from Content/
Icon := texture_block:
    DefaultImage := MyTexture",
            Notes = "Textures must be imported into the UEFN project first. Reference them by the asset name."
        },
        new()
        {
            Name = "button",
            Category = "Interactive",
            Description = "A clickable button widget. Subscribe to OnClick() for interaction handling.",
            Properties = new()
            {
                new("DefaultText", "message", "Button label text"),
                new("OnClick", "event", "Fires when player clicks the button")
            },
            UsagePattern = @"MyButton := button_loud:
    DefaultText := StringToMessage(""Click Me"")

# In OnBegin or setup:
MyButton.OnClick().Subscribe(HandleClick)

HandleClick():void =
    Print(""Button clicked!"")",
            Notes = "Button types: button_loud (prominent), button_quiet (subtle), button_regular. All have OnClick() event."
        },

        // === Player UI Integration ===
        new()
        {
            Name = "player_ui",
            Category = "System",
            Description = "The player's UI root. Get it via GetPlayerUI[Player], then AddWidget/RemoveWidget to show/hide canvases.",
            Properties = new()
            {
                new("AddWidget", "method(canvas)", "Adds a canvas to the player's screen"),
                new("RemoveWidget", "method(canvas)", "Removes a canvas from the player's screen")
            },
            UsagePattern = @"if (PlayerUI := GetPlayerUI[Player]):
    PlayerUI.AddWidget(MyCanvas)

# Later, to remove:
if (PlayerUI := GetPlayerUI[Player]):
    PlayerUI.RemoveWidget(MyCanvas)",
            Notes = "Only canvas widgets can be added to player_ui. Always check with if() since GetPlayerUI is failable."
        }
    };

    public static List<AnchoringPreset> GetAnchoringPresets() => new()
    {
        new("Top-Left", "vector2{X := 0.0, Y := 0.0}", "vector2{X := 0.0, Y := 0.0}"),
        new("Top-Center", "vector2{X := 0.5, Y := 0.03}", "vector2{X := 0.5, Y := 0.0}"),
        new("Top-Right", "vector2{X := 0.95, Y := 0.03}", "vector2{X := 1.0, Y := 0.0}"),
        new("Center", "vector2{X := 0.5, Y := 0.5}", "vector2{X := 0.5, Y := 0.5}"),
        new("Bottom-Left", "vector2{X := 0.05, Y := 0.95}", "vector2{X := 0.0, Y := 1.0}"),
        new("Bottom-Center", "vector2{X := 0.5, Y := 0.95}", "vector2{X := 0.5, Y := 1.0}"),
        new("Bottom-Right", "vector2{X := 0.95, Y := 0.95}", "vector2{X := 1.0, Y := 1.0}"),
        new("Full-Screen", "Minimum := vector2{X := 0.0, Y := 0.0}, Maximum := vector2{X := 1.0, Y := 1.0}", "N/A — stretches to fill"),
        new("Top-Bar", "Minimum := vector2{X := 0.0, Y := 0.0}, Maximum := vector2{X := 1.0, Y := 0.08}", "Full width, top 8%"),
        new("Bottom-Bar", "Minimum := vector2{X := 0.0, Y := 0.92}, Maximum := vector2{X := 1.0, Y := 1.0}", "Full width, bottom 8%"),
    };

    public static List<UIPattern> GetCommonPatterns() => new()
    {
        new()
        {
            Name = "HUD Text Element",
            Description = "Fixed-position text that updates dynamically",
            Code = @"var ScoreDisplay : ?text_block = false

SetupScoreHUD(Player : player) : void =
    if (PlayerUI := GetPlayerUI[Player]):
        Score := text_block{DefaultText := StringToMessage(""Score: 0"")}
        set ScoreDisplay = option{Score}
        HUD := canvas:
            Slots := array:
                canvas_slot:
                    Widget := Score
                    Anchors := anchors{Minimum := vector2{X := 0.95, Y := 0.05}, Maximum := vector2{X := 0.95, Y := 0.05}}
                    Alignment := vector2{X := 1.0, Y := 0.0}
        PlayerUI.AddWidget(HUD)

UpdateScore(Points : int) : void =
    if (Display := ScoreDisplay?):
        Display.SetText(StringToMessage(""Score: {Points}""))"
        },
        new()
        {
            Name = "Toggle Panel",
            Description = "UI panel that shows/hides on button press",
            Code = @"var PanelCanvas : ?canvas = false
var PanelVisible : logic = false

TogglePanel(Player : player) : void =
    if (PanelVisible?):
        HidePanel(Player)
    else:
        ShowPanel(Player)

ShowPanel(Player : player) : void =
    if (PlayerUI := GetPlayerUI[Player]):
        Panel := canvas:
            Slots := array:
                # Background
                canvas_slot:
                    Widget := color_block{DefaultColor := color{R := 0.0, G := 0.0, B := 0.0, A := 0.7}}
                    Anchors := anchors{Minimum := vector2{X := 0.2, Y := 0.2}, Maximum := vector2{X := 0.8, Y := 0.8}}
                # Content
                canvas_slot:
                    Widget := text_block{DefaultText := StringToMessage(""Panel Content"")}
                    Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.3}, Maximum := vector2{X := 0.5, Y := 0.3}}
                    Alignment := vector2{X := 0.5, Y := 0.0}
        PlayerUI.AddWidget(Panel)
        set PanelCanvas = option{Panel}
        set PanelVisible = true

HidePanel(Player : player) : void =
    if (PlayerUI := GetPlayerUI[Player], Panel := PanelCanvas?):
        PlayerUI.RemoveWidget(Panel)
        set PanelCanvas = false
        set PanelVisible = false"
        },
        new()
        {
            Name = "Timed Notification",
            Description = "Toast message that auto-dismisses after a duration",
            Code = @"ShowNotification(Player : player, Message : string, Duration : float)<suspends> : void =
    if (PlayerUI := GetPlayerUI[Player]):
        MsgText := text_block{DefaultText := StringToMessage(Message)}
        Toast := canvas:
            Slots := array:
                canvas_slot:
                    Widget := color_block{DefaultColor := color{R := 0.1, G := 0.1, B := 0.1, A := 0.85}}
                    Anchors := anchors{Minimum := vector2{X := 0.3, Y := 0.05}, Maximum := vector2{X := 0.7, Y := 0.1}}
                canvas_slot:
                    Widget := MsgText
                    Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.075}, Maximum := vector2{X := 0.5, Y := 0.075}}
                    Alignment := vector2{X := 0.5, Y := 0.5}
        PlayerUI.AddWidget(Toast)
        Sleep(Duration)
        PlayerUI.RemoveWidget(Toast)"
        },
        new()
        {
            Name = "Player List / Leaderboard",
            Description = "Vertical stack of player entries with scores",
            Code = @"BuildLeaderboard(Player : player) : void =
    if (PlayerUI := GetPlayerUI[Player]):
        AllPlayers := GetPlayspace().GetPlayers()
        var Slots : []canvas_slot = array{}

        # Header
        set Slots += array:
            canvas_slot:
                Widget := text_block{DefaultText := StringToMessage(""LEADERBOARD"")}
                Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.15}, Maximum := vector2{X := 0.5, Y := 0.15}}
                Alignment := vector2{X := 0.5, Y := 0.0}

        # Player entries
        var YPos : float = 0.22
        for (Index := 0..Min(AllPlayers.Length - 1, 9)):
            if (P := AllPlayers[Index]):
                EntryText := text_block{DefaultText := StringToMessage(""{Index + 1}. Player {Index + 1} — 0 pts"")}
                set Slots += array:
                    canvas_slot:
                        Widget := EntryText
                        Anchors := anchors{Minimum := vector2{X := 0.5, Y := YPos}, Maximum := vector2{X := 0.5, Y := YPos}}
                        Alignment := vector2{X := 0.5, Y := 0.0}
                set YPos += 0.04

        Board := canvas{Slots := Slots}
        PlayerUI.AddWidget(Board)"
        },
        new()
        {
            Name = "Health/Progress Bar",
            Description = "Visual bar that fills based on a 0-1 value",
            Code = @"# Two color_blocks in a canvas — background and fill.
# To update the fill width, rebuild the canvas with new anchors.

var BarCanvas : ?canvas = false
var CurrentPlayerUI : ?player_ui = false

SetupBar(Player : player) : void =
    if (PlayerUI := GetPlayerUI[Player]):
        set CurrentPlayerUI = option{PlayerUI}
        RebuildBar(1.0) # Start at full

RebuildBar(FillPercent : float) : void =
    # Remove old bar
    if (PUI := CurrentPlayerUI?, OldBar := BarCanvas?):
        PUI.RemoveWidget(OldBar)

    FillMax := 0.3 + (0.4 * Clamp(FillPercent, 0.0, 1.0))

    Bar := canvas:
        Slots := array:
            # Background (dark)
            canvas_slot:
                Widget := color_block{DefaultColor := color{R := 0.15, G := 0.15, B := 0.15, A := 0.8}}
                Anchors := anchors{Minimum := vector2{X := 0.3, Y := 0.92}, Maximum := vector2{X := 0.7, Y := 0.95}}
            # Fill (colored)
            canvas_slot:
                Widget := color_block{DefaultColor := color{R := 0.2, G := 0.9, B := 0.3, A := 1.0}}
                Anchors := anchors{Minimum := vector2{X := 0.3, Y := 0.92}, Maximum := vector2{X := FillMax, Y := 0.95}}

    if (PUI := CurrentPlayerUI?):
        PUI.AddWidget(Bar)
        set BarCanvas = option{Bar}"
        },
        new()
        {
            Name = "Required Using Statements",
            Description = "Standard imports needed for Verse UI code",
            Code = @"using { /Fortnite.com/Devices }
using { /Fortnite.com/UI }
using { /Verse.org/Simulation }
using { /UnrealEngine.com/Temporary/UI }
using { /UnrealEngine.com/Temporary/SpatialMath }

# For player access:
using { /Fortnite.com/Characters }
using { /Fortnite.com/Game }
using { /Fortnite.com/Playspaces }"
        }
    };

    /// <summary>
    /// Gets a complete reference for a specific widget type.
    /// </summary>
    public static WidgetTypeRef? GetWidget(string name)
        => GetAllWidgets().FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all patterns matching a keyword.
    /// </summary>
    public static List<UIPattern> FindPatterns(string keyword)
        => GetCommonPatterns().Where(p =>
            p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            p.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            p.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
}

// === Models ===

public class WidgetTypeRef
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public List<WidgetProperty> Properties { get; set; } = new();
    public string UsagePattern { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class WidgetProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public WidgetProperty() { }
    public WidgetProperty(string name, string type, string desc) { Name = name; Type = type; Description = desc; }
}

public class AnchoringPreset
{
    public string Name { get; set; } = "";
    public string Anchors { get; set; } = "";
    public string Alignment { get; set; } = "";
    public AnchoringPreset() { }
    public AnchoringPreset(string name, string anchors, string alignment) { Name = name; Anchors = anchors; Alignment = alignment; }
}

public class UIPattern
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Code { get; set; } = "";
}
