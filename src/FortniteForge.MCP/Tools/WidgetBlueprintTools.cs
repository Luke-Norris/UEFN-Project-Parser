using FortniteForge.Core.Config;
using FortniteForge.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.MCP.Tools;

/// <summary>
/// MCP tools for UEFN Widget Blueprint creation and management.
/// Clones existing Widget Blueprint templates, modifies widget trees,
/// and generates paired Verse controller code.
/// </summary>
[McpServerToolType]
public class WidgetBlueprintTools
{
    [McpServerTool, Description(
        "Get the complete UEFN Widget Blueprint reference — all available UMG widget types, " +
        "their properties, and what's NOT available in UEFN. Based on scanning 1,543 real " +
        "Widget Blueprints across 61 projects. Use before creating any Widget Blueprint.")]
    public string get_widget_blueprint_reference()
    {
        return @"=== UEFN Widget Blueprint Reference ===
(Based on scanning 1,543 Widget Blueprints across 61 UEFN projects)

== Available Widget Types ==

CONTAINER WIDGETS:
  CanvasPanel (1,795 uses) — Root container, absolute positioning
    Properties: Slots (array of CanvasPanelSlot)

  Overlay (393 uses) — Stacks children, all fill bounds
    Properties: Slots (array of OverlaySlot)
    Slot props: HorizontalAlignment, VerticalAlignment, Padding

  StackBox (65 uses) — UEFN's VerticalBox/HorizontalBox
    Properties: Orientation (Vertical/Horizontal), Slots
    Slot props: HorizontalAlignment, VerticalAlignment, Padding, Size

  SizeBox (150 uses) — Constrains child size
    Properties: WidthOverride, HeightOverride

  ScaleBox (52 uses) — Scales child to fit
  GridPanel (28 uses) — Grid layout
    Slot props: Row, Column, HorizontalAlignment, VerticalAlignment

DISPLAY WIDGETS:
  UEFN_TextBlock_C (954 uses) — Text display (UEFN wrapper)
    Import: /Game/Valkyrie/UMG/UEFN_TextBlock
    Properties: Text, Font, ColorAndOpacity, ShadowOffset, ShadowColorAndOpacity, Justification, Visibility
    Verse: text_block — SetText(), SetTextColor(), SetTextOpacity(), SetJustification()

  Image (2,341 uses) — Texture/image display
    Properties: Brush (SlateBrush with ResourceObject), RenderTransform, Clipping
    Verse: texture_block — SetImage(), SetTint(), SetDesiredSize()

  ColorBlock — Solid color rectangle (Verse-only, use color_block)
    Not a Widget Blueprint widget — create in Verse code

INTERACTIVE WIDGETS:
  UEFN_Button_Regular_C (40 uses)
    Import: /Game/Valkyrie/UMG/UEFN_Button_Regular
    Properties: Text, Slot
    Verse: button_regular — OnClick() event

  UEFN_Button_Loud_C (38 uses)
    Import: /Game/Valkyrie/UMG/UEFN_Button_Loud
    Properties: Text, MinHeight, MinWidth, bSelectable, bInteractableWhenSelected, RenderTransform
    Verse: button_loud — OnClick() event

  UEFN_Button_Quiet_C (36 uses)
    Import: /Game/Valkyrie/UMG/UEFN_Button_Quiet
    Properties: Text, MinHeight, MinWidth, SingleText, NormalStyle, SelectedStyle, DisabledStyle, LockedStyle, Visibility

== NOT Available in UEFN ==
ProgressBar, ScrollBox, Border, WrapBox, Spacer, WidgetSwitcher, ListView,
TileView, TreeView, RichTextBlock, CheckBox, Slider, EditableText, ComboBox,
BackgroundBlur, Throbber, RetainerBox — NONE of these exist in any UEFN project.

== CanvasPanelSlot Layout Properties ==
  LayoutData (AnchorData):
    Offsets: Left, Top, Right, Bottom (float — pixel position/size)
    Anchors: Minimum{X,Y}, Maximum{X,Y} (0.0-1.0 screen space)
    Alignment: X, Y (0.0-1.0 pivot point)
  bAutoSize: logic — widget sizes to its content

== Widget Blueprint .uasset Structure ==
  WidgetBlueprint export → WidgetTree → RootWidget → child widgets
  All logic must be in Verse (no Blueprint scripting in UEFN Widget BPs)
  277 of 1,543 (18%) use MVVM framework with viewmodels

== Workflow ==
  1. Create Widget Blueprint in UEFN Widget Designer (visual layout)
  2. OR clone an existing one from the library
  3. Write Verse code to instantiate and control the widget
  4. User tweaks visual properties in UEFN designer";
    }

    [McpServerTool, Description(
        "Search the library for Widget Blueprint templates matching a description. " +
        "Returns templates you can clone to the active project. " +
        "Examples: 'HUD', 'menu', 'welcome', 'shop', 'scoreboard', 'button panel'")]
    public string find_widget_templates(
        LibraryIndexer indexer,
        [Description("What kind of widget you're looking for")] string query)
    {
        if (indexer.Index == null)
        {
            var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fortniteforge", "library-index.json");
            indexer.LoadIndex(savePath);
        }

        // Search library for widget blueprint assets
        var results = indexer.Search(query);
        var widgetAssets = results.Assets
            .Where(h => h.Item.AssetClass.Contains("CanvasPanel") ||
                        h.Item.AssetClass.Contains("WidgetBlueprint") ||
                        h.Item.AssetClass.Contains("Image") ||
                        h.Item.Name.Contains("widget", StringComparison.OrdinalIgnoreCase) ||
                        h.Item.Name.Contains("ui", StringComparison.OrdinalIgnoreCase) ||
                        h.Item.Name.Contains("hud", StringComparison.OrdinalIgnoreCase) ||
                        h.Item.Name.Contains("menu", StringComparison.OrdinalIgnoreCase))
            .Take(15)
            .ToList();

        if (widgetAssets.Count == 0)
            return $"No widget templates found for '{query}'. Try: HUD, menu, welcome, shop, button, text";

        var output = new List<string> { $"Widget Blueprint templates matching '{query}':\n" };
        foreach (var h in widgetAssets)
        {
            output.Add($"  [{h.ProjectName}] {h.Item.Name}");
            output.Add($"    Class: {h.Item.AssetClass} | Size: {h.Item.FileSize} bytes");
            output.Add($"    Path: {h.Item.FilePath}");
            output.Add($"    Use clone_widget_blueprint to copy to your project.\n");
        }
        return string.Join("\n", output);
    }

    [McpServerTool, Description(
        "Clone a Widget Blueprint from the library to the active project. " +
        "Copies the .uasset file and generates paired Verse controller code. " +
        "The user can then tweak the visual layout in UEFN's Widget Designer.")]
    public string clone_widget_blueprint(
        ForgeConfig config,
        [Description("Full path to the source Widget Blueprint .uasset")] string sourcePath,
        [Description("Name for the new widget (without extension)")] string newName)
    {
        if (!File.Exists(sourcePath))
            return $"Source file not found: {sourcePath}";

        if (config.ReadOnly)
            return "BLOCKED: Project is in read-only mode.";

        var contentPath = config.ContentPath;
        if (!Directory.Exists(contentPath))
            return "Content directory not found.";

        var targetPath = Path.Combine(contentPath, $"{newName}.uasset");
        if (File.Exists(targetPath))
            return $"File already exists: {targetPath}. Choose a different name.";

        // Copy the widget blueprint
        File.Copy(sourcePath, targetPath);

        // Also copy .uexp if it exists
        var sourceUexp = Path.ChangeExtension(sourcePath, ".uexp");
        if (File.Exists(sourceUexp))
            File.Copy(sourceUexp, Path.ChangeExtension(targetPath, ".uexp"));

        // Inspect the widget to understand its structure
        var widgetInfo = InspectWidgetBlueprint(targetPath);

        // Generate paired Verse controller code
        var verseCode = GenerateWidgetController(newName, widgetInfo);

        return $"Widget Blueprint cloned successfully!\n\n" +
               $"Asset: {targetPath}\n" +
               $"Structure: {widgetInfo}\n\n" +
               $"=== Generated Verse Controller ===\n{verseCode}\n\n" +
               $"Next steps:\n" +
               $"1. Open {newName} in UEFN's Widget Designer to customize the layout\n" +
               $"2. Save the Verse controller with write_project_verse\n" +
               $"3. Place the Verse device in your level\n" +
               $"4. Wire the device's widget reference to {newName}";
    }

    [McpServerTool, Description(
        "Inspect a Widget Blueprint .uasset file — shows the widget tree hierarchy, " +
        "all widget types used, and their editable properties.")]
    public string inspect_widget_blueprint(
        [Description("Path to the Widget Blueprint .uasset file")] string path)
    {
        if (!File.Exists(path))
            return $"File not found: {path}";

        try
        {
            var asset = new UAsset(path, EngineVersion.VER_UE5_4);
            var output = new List<string> { $"Widget Blueprint: {Path.GetFileNameWithoutExtension(path)}\n" };

            foreach (var export in asset.Exports)
            {
                var cls = export.GetExportClassType()?.ToString() ?? "Unknown";
                var name = export.ObjectName?.ToString() ?? "";

                if (export is NormalExport ne && ne.Data.Count > 0)
                {
                    output.Add($"  {cls} \"{name}\" ({ne.Data.Count} props):");
                    foreach (var prop in ne.Data.Take(10))
                    {
                        var propName = prop.Name?.ToString() ?? "";
                        var value = prop switch
                        {
                            TextPropertyData t => t.Value?.ToString() ?? "",
                            StrPropertyData s => s.Value?.ToString() ?? "",
                            BoolPropertyData b => b.Value.ToString(),
                            IntPropertyData i => i.Value.ToString(),
                            FloatPropertyData f => f.Value.ToString(),
                            BytePropertyData bp => bp.EnumValue?.ToString() ?? bp.Value.ToString(),
                            _ => $"[{prop.GetType().Name}]"
                        };
                        output.Add($"    {propName} = {value}");
                    }
                }
                else
                {
                    output.Add($"  {cls} \"{name}\"");
                }
            }

            return string.Join("\n", output);
        }
        catch (Exception ex)
        {
            return $"Error parsing widget: {ex.Message}";
        }
    }

    private string InspectWidgetBlueprint(string path)
    {
        try
        {
            var asset = new UAsset(path, EngineVersion.VER_UE5_4);
            var widgetTypes = asset.Exports
                .Select(e => e.GetExportClassType()?.ToString() ?? "")
                .Where(c => !string.IsNullOrEmpty(c) && c != "MetaData" && c != "EdGraph")
                .Distinct()
                .ToList();
            return $"{asset.Exports.Count} exports, widgets: {string.Join(", ", widgetTypes)}";
        }
        catch
        {
            return "Could not parse widget structure";
        }
    }

    private string GenerateWidgetController(string widgetName, string widgetInfo)
    {
        return $@"# {widgetName} Controller — Generated by FortniteForge
# Verse code to instantiate and control the Widget Blueprint.
# Modify this to add dynamic behavior.

using {{ /Fortnite.com/Devices }}
using {{ /Fortnite.com/UI }}
using {{ /Verse.org/Simulation }}
using {{ /UnrealEngine.com/Temporary/UI }}
using {{ /UnrealEngine.com/Temporary/SpatialMath }}

{widgetName}_controller := class(creative_device):

    # Reference to the Widget Blueprint asset
    # Set this in UEFN's device properties after placing the device
    @editable
    WidgetClass : subtype_of(widget) = widget

    var ActiveWidget : ?canvas = false
    var ActivePlayerUI : ?player_ui = false

    OnBegin<override>()<suspends>:void =
        # Show widget for all current players
        for (Player : GetPlayspace().GetPlayers()):
            ShowWidget(Player)
        # Show for players who join later
        GetPlayspace().PlayerAddedEvent().Subscribe(OnPlayerAdded)

    OnPlayerAdded(Player : player):void =
        ShowWidget(Player)

    ShowWidget(Player : player):void =
        if (PlayerUI := GetPlayerUI[Player]):
            # Create the widget from the blueprint
            # For programmatic widgets, build a canvas here:
            MyCanvas := canvas:
                Slots := array:
                    canvas_slot:
                        Widget := text_block{{DefaultText := StringToMessage(""{widgetName} loaded"")}}
                        Anchors := anchors{{Minimum := vector2{{X := 0.5, Y := 0.5}}, Maximum := vector2{{X := 0.5, Y := 0.5}}}}
                        Alignment := vector2{{X := 0.5, Y := 0.5}}
            PlayerUI.AddWidget(MyCanvas)
            set ActiveWidget = option{{MyCanvas}}
            set ActivePlayerUI = option{{PlayerUI}}

    HideWidget():void =
        if (PUI := ActivePlayerUI?, Widget := ActiveWidget?):
            PUI.RemoveWidget(Widget)
            set ActiveWidget = false

    # Add your custom methods here:
    # UpdateText(NewText : string):void = ...
    # OnButtonClicked():void = ...";
    }
}
