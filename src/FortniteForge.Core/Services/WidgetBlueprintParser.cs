using FortniteForge.Core.Models;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Parses Widget Blueprint .uasset files into WidgetSpec objects.
/// Reverse of WidgetBlueprintBuilder — reads the dual-tree structure
/// and extracts widget hierarchy, properties, and layout data.
/// Only parses the editor tree (first WidgetTree export).
/// </summary>
public class WidgetBlueprintParser
{
    /// <summary>
    /// Parse a Widget Blueprint .uasset file into a WidgetSpec.
    /// Uses SafeFileAccess for copy-on-read safety.
    /// </summary>
    public WidgetSpec? Parse(string assetPath, SafeFileAccess fileAccess)
    {
        var asset = fileAccess.OpenForRead(assetPath);
        return ParseFromAsset(asset, Path.GetFileNameWithoutExtension(assetPath));
    }

    /// <summary>
    /// Parse a Widget Blueprint from an already-loaded UAsset.
    /// </summary>
    /// <summary>
    /// Diagnostic parse — returns error string if parsing fails, null on success.
    /// </summary>
    public string? DiagnoseParseFailure(UAsset asset)
    {
        if (!IsWidgetBlueprint(asset))
            return "Not a widget blueprint (no WidgetTree export)";

        NormalExport? widgetTreeExport = null;
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].GetExportClassType()?.ToString() == "WidgetTree"
                && asset.Exports[i] is NormalExport ne)
            { widgetTreeExport = ne; break; }
        }
        if (widgetTreeExport == null)
            return "WidgetTree export found by IsWidgetBlueprint but not as NormalExport";

        var rootWidgetProp = FindProperty<ObjectPropertyData>(widgetTreeExport.Data, "RootWidget");
        if (rootWidgetProp == null)
        {
            // Check if this is a Verse-generated stub
            bool isVerseStub = asset.Exports.Any(e =>
                e.GetExportClassType()?.ToString() == "VerseTypeEditorClassSettings");
            if (isVerseStub)
                return "Verse-generated widget class — no visual tree defined. Create the widget layout in UEFN's Widget Designer first.";

            var propNames = string.Join(", ", widgetTreeExport.Data.Select(p => p.Name?.ToString() ?? "?").Take(10));
            return $"WidgetTree has no RootWidget property. Properties: [{propNames}]";
        }

        var rootExportIdx = rootWidgetProp.Value.Index - 1;
        if (rootExportIdx < 0 || rootExportIdx >= asset.Exports.Count)
            return $"RootWidget index {rootExportIdx} out of range (0..{asset.Exports.Count - 1})";

        if (asset.Exports[rootExportIdx] is not NormalExport)
            return $"RootWidget export [{rootExportIdx}] is {asset.Exports[rootExportIdx].GetType().Name}, not NormalExport";

        return null; // No diagnosable issue — ParseWidgetExport likely failed on widget structure
    }

    public WidgetSpec? ParseFromAsset(UAsset asset, string? name = null)
    {
        if (!IsWidgetBlueprint(asset))
            return null;

        // Find the first WidgetTree export (editor tree)
        NormalExport? widgetTreeExport = null;
        for (int i = 0; i < asset.Exports.Count; i++)
        {
            if (asset.Exports[i].GetExportClassType()?.ToString() == "WidgetTree"
                && asset.Exports[i] is NormalExport ne)
            {
                widgetTreeExport = ne;
                break; // First WidgetTree = editor tree
            }
        }

        if (widgetTreeExport == null)
            return null;

        // Get root widget from WidgetTree's RootWidget property
        var rootWidgetProp = FindProperty<ObjectPropertyData>(widgetTreeExport.Data, "RootWidget");
        if (rootWidgetProp == null)
            return null;

        var rootExportIdx = rootWidgetProp.Value.Index - 1; // FPackageIndex is 1-based for exports
        if (rootExportIdx < 0 || rootExportIdx >= asset.Exports.Count)
            return null;

        if (asset.Exports[rootExportIdx] is not NormalExport rootExport)
            return null;

        // Determine blueprint name
        var blueprintName = name ?? "ParsedWidget";
        foreach (var export in asset.Exports)
        {
            if (export.GetExportClassType()?.ToString() == "WidgetBlueprint")
            {
                blueprintName = export.ObjectName?.ToString() ?? blueprintName;
                break;
            }
        }

        // Build the AllWidgets index to know which exports belong to the editor tree
        var editorWidgetIndices = new HashSet<int>();
        var allWidgetsProp = FindProperty<ArrayPropertyData>(widgetTreeExport.Data, "AllWidgets");
        if (allWidgetsProp?.Value != null)
        {
            foreach (var item in allWidgetsProp.Value)
            {
                if (item is ObjectPropertyData objProp)
                {
                    var idx = objProp.Value.Index - 1;
                    if (idx >= 0) editorWidgetIndices.Add(idx);
                }
            }
        }

        // Parse the root widget recursively
        var rootNode = ParseWidgetExport(rootExport, rootExportIdx, asset, editorWidgetIndices, isRoot: true);
        if (rootNode == null)
            return null;

        var spec = new WidgetSpec(blueprintName)
        {
            Root = rootNode
        };

        // Auto-generate variables
        spec.GenerateVariables();

        return spec;
    }

    /// <summary>
    /// Check if a UAsset is a Widget Blueprint (has at least one WidgetTree export).
    /// </summary>
    public static bool IsWidgetBlueprint(UAsset asset)
    {
        return asset.Exports.Any(e => e.GetExportClassType()?.ToString() == "WidgetTree");
    }

    /// <summary>
    /// Get a quick summary of a Widget Blueprint without full parsing.
    /// Returns (name, widgetCount) or null if not a widget blueprint.
    /// </summary>
    public static (string name, int widgetCount)? GetSummary(UAsset asset)
    {
        if (!IsWidgetBlueprint(asset))
            return null;

        string name = "Unknown";
        int widgetCount = 0;
        bool hasRootWidget = false;

        // Infrastructure export types to skip when counting widgets
        var infraTypes = new HashSet<string> {
            "WidgetTree", "WidgetBlueprint", "WidgetBlueprintGeneratedClass",
            "EdGraph", "MVVMBlueprintView", "MVVMBlueprintViewSettings",
            "MVVMWidgetBlueprintExtension_View", "VerseTypeEditorClassSettings",
            "VerseTypeEditorWidgetExtension"
        };

        foreach (var export in asset.Exports)
        {
            var cls = export.GetExportClassType()?.ToString() ?? "";

            if (cls == "WidgetBlueprint")
                name = export.ObjectName?.ToString() ?? name;

            // Check if WidgetTree has a RootWidget (non-empty tree)
            if (cls == "WidgetTree" && export is NormalExport ne)
            {
                if (ne.Data.Any(p => p.Name?.ToString() == "RootWidget"))
                    hasRootWidget = true;
            }

            // Count actual widget exports
            if (!infraTypes.Contains(cls) && !cls.All(char.IsDigit) && !cls.EndsWith("Slot")
                && !cls.StartsWith("Default__"))
            {
                widgetCount++;
            }
        }

        // Skip empty Verse class stubs that have no visual tree
        if (!hasRootWidget)
            return null;

        // Divide by 2 because of dual-tree (editor + runtime copies)
        widgetCount = Math.Max(1, widgetCount / 2);

        return (name, widgetCount);
    }

    private WidgetNode? ParseWidgetExport(NormalExport export, int exportIdx, UAsset asset, HashSet<int> editorWidgetIndices, bool isRoot = false)
    {
        var className = export.GetExportClassType()?.ToString() ?? "";
        var widgetType = ClassNameToWidgetType(className);

        // For unknown types, try to infer from the import chain or context
        if (widgetType == null)
        {
            widgetType = InferWidgetType(export, asset, isRoot);
            if (widgetType == null)
                return null; // Truly unknown — skip
        }

        var node = new WidgetNode
        {
            Type = widgetType.Value,
            Name = export.ObjectName?.ToString() ?? $"Widget_{exportIdx}"
        };

        // Extract type-specific properties
        ExtractWidgetProperties(export, node, asset);

        // Find children via Slots array
        var slotsProp = FindProperty<ArrayPropertyData>(export.Data, "Slots");
        if (slotsProp?.Value != null)
        {
            foreach (var slotItem in slotsProp.Value)
            {
                if (slotItem is not ObjectPropertyData slotRef)
                    continue;

                var slotExportIdx = slotRef.Value.Index - 1;
                if (slotExportIdx < 0 || slotExportIdx >= asset.Exports.Count)
                    continue;

                if (asset.Exports[slotExportIdx] is not NormalExport slotExport)
                    continue;

                // Get child widget from slot's Content property
                var contentProp = FindProperty<ObjectPropertyData>(slotExport.Data, "Content");
                if (contentProp == null)
                    continue;

                var childExportIdx = contentProp.Value.Index - 1;
                if (childExportIdx < 0 || childExportIdx >= asset.Exports.Count)
                    continue;

                // Only parse children that belong to the editor tree
                if (editorWidgetIndices.Count > 0 && !editorWidgetIndices.Contains(childExportIdx))
                    continue;

                if (asset.Exports[childExportIdx] is not NormalExport childExport)
                    continue;

                var childNode = ParseWidgetExport(childExport, childExportIdx, asset, editorWidgetIndices);
                if (childNode != null)
                {
                    // Extract slot layout data onto the child node
                    ExtractSlotLayout(slotExport, childNode, node.Type);
                    node.Children.Add(childNode);
                }
            }
        }

        return node;
    }

    private void ExtractWidgetProperties(NormalExport export, WidgetNode node, UAsset? asset = null)
    {
        switch (node.Type)
        {
            case WidgetType.TextBlock:
                var textProp = FindProperty<TextPropertyData>(export.Data, "Text");
                if (textProp != null)
                    node.Text = textProp.CultureInvariantString?.ToString()
                                ?? textProp.Value?.ToString()
                                ?? "";
                break;

            case WidgetType.Image:
                ExtractBrushProperties(export, node, asset);
                break;

            case WidgetType.ButtonLoud:
            case WidgetType.ButtonQuiet:
            case WidgetType.ButtonRegular:
                var minW = FindProperty<IntPropertyData>(export.Data, "MinWidth");
                if (minW != null) node.MinWidth = minW.Value;
                var minH = FindProperty<IntPropertyData>(export.Data, "MinHeight");
                if (minH != null) node.MinHeight = minH.Value;
                // Buttons may also have Text
                var btnText = FindProperty<TextPropertyData>(export.Data, "Text");
                if (btnText != null)
                    node.Text = btnText.CultureInvariantString?.ToString()
                                ?? btnText.Value?.ToString()
                                ?? "";
                break;

            case WidgetType.StackBox:
                var orientProp = FindProperty<BytePropertyData>(export.Data, "Orientation");
                if (orientProp != null)
                {
                    var enumVal = orientProp.EnumValue?.ToString() ?? "";
                    node.Orientation = enumVal.Contains("Horizontal")
                        ? Orientation.Horizontal
                        : Orientation.Vertical;
                }
                break;

            case WidgetType.SizeBox:
                var widthOverride = FindProperty<FloatPropertyData>(export.Data, "WidthOverride");
                if (widthOverride != null) node.MinWidth = (int)widthOverride.Value;
                var heightOverride = FindProperty<FloatPropertyData>(export.Data, "HeightOverride");
                if (heightOverride != null) node.MinHeight = (int)heightOverride.Value;
                break;
        }

        // ── Common properties (all types) ──

        // Visibility
        var visProp = FindProperty<EnumPropertyData>(export.Data, "Visibility");
        if (visProp != null)
        {
            var visVal = visProp.Value?.ToString() ?? "";
            var colonIdx = visVal.LastIndexOf("::", StringComparison.Ordinal);
            node.Visibility = colonIdx >= 0 ? visVal[(colonIdx + 2)..] : visVal;
        }

        // RenderOpacity
        var opacityProp = FindProperty<FloatPropertyData>(export.Data, "RenderOpacity");
        if (opacityProp != null)
            node.RenderOpacity = opacityProp.Value;

        // ── Text visual properties (TextBlock + Buttons) ──
        if (node.Type == WidgetType.TextBlock || node.Type == WidgetType.ButtonLoud
            || node.Type == WidgetType.ButtonQuiet || node.Type == WidgetType.ButtonRegular)
        {
            ExtractTextVisualProperties(export, node);
        }
    }

    private void ExtractTextVisualProperties(NormalExport export, WidgetNode node)
    {
        // ColorAndOpacity → SpecifiedColor (text foreground color)
        var colorProp = FindProperty<StructPropertyData>(export.Data, "ColorAndOpacity");
        if (colorProp?.Value != null)
        {
            var specColor = FindProperty<LinearColorPropertyData>(colorProp.Value, "SpecifiedColor");
            if (specColor != null)
                node.TextColor = LinearColorToHex(specColor.Value);
        }

        // Font struct → Size, TypefaceFontName, LetterSpacing, OutlineSettings
        var fontProp = FindProperty<StructPropertyData>(export.Data, "Font");
        if (fontProp?.Value != null)
        {
            var sizeProp = FindProperty<FloatPropertyData>(fontProp.Value, "Size");
            if (sizeProp != null) node.FontSize = sizeProp.Value;

            var faceProp = FindProperty<NamePropertyData>(fontProp.Value, "TypefaceFontName");
            if (faceProp != null) node.FontWeight = faceProp.Value?.ToString();

            var letterProp = FindProperty<IntPropertyData>(fontProp.Value, "LetterSpacing");
            if (letterProp != null) node.LetterSpacing = letterProp.Value;

            var outlineProp = FindProperty<StructPropertyData>(fontProp.Value, "OutlineSettings");
            if (outlineProp?.Value != null)
            {
                var outSizeProp = FindProperty<IntPropertyData>(outlineProp.Value, "OutlineSize");
                if (outSizeProp != null) node.OutlineSize = outSizeProp.Value;

                var outColorProp = FindProperty<LinearColorPropertyData>(outlineProp.Value, "OutlineColor");
                if (outColorProp != null) node.OutlineColor = LinearColorToHex(outColorProp.Value);
            }
        }

        // Justification (text alignment)
        var justProp = FindProperty<BytePropertyData>(export.Data, "Justification");
        if (justProp != null)
        {
            var justVal = justProp.EnumValue?.ToString() ?? justProp.Value.ToString();
            if (justVal.Contains("Center")) node.Justification = "Center";
            else if (justVal.Contains("Right")) node.Justification = "Right";
            else node.Justification = "Left";
        }
    }

    private void ExtractBrushProperties(NormalExport export, WidgetNode node, UAsset? asset = null)
    {
        var brushProp = FindProperty<StructPropertyData>(export.Data, "Brush");
        if (brushProp?.Value == null) return;

        // Brush -> TintColor -> SpecifiedColor (LinearColor)
        var tintColor = FindProperty<StructPropertyData>(brushProp.Value, "TintColor");
        if (tintColor?.Value != null)
        {
            var specifiedColor = FindProperty<LinearColorPropertyData>(tintColor.Value, "SpecifiedColor");
            if (specifiedColor != null)
            {
                node.TintColor = LinearColorToHex(specifiedColor.Value);
            }
        }

        // Brush -> ResourceObject (texture path — resolve import references)
        var resourceObj = FindProperty<ObjectPropertyData>(brushProp.Value, "ResourceObject");
        if (resourceObj != null && resourceObj.Value.Index != 0)
        {
            // Resolve import index to actual object path
            var idx = resourceObj.Value.Index;
            if (idx < 0)
            {
                // Negative = import reference, resolve via asset imports
                var importIdx = -idx - 1;
                if (importIdx < asset.Imports.Count)
                {
                    var import = asset.Imports[importIdx];
                    var objName = import.ObjectName?.ToString() ?? "";
                    // Walk up the import chain to build the full path
                    var outerIdx = import.OuterIndex.Index;
                    if (outerIdx < 0)
                    {
                        var outerImportIdx = -outerIdx - 1;
                        if (outerImportIdx < asset.Imports.Count)
                        {
                            var outerName = asset.Imports[outerImportIdx].ObjectName?.ToString() ?? "";
                            node.TexturePath = $"{outerName}.{objName}";
                        }
                        else
                            node.TexturePath = objName;
                    }
                    else
                        node.TexturePath = objName;
                }
            }
            else
            {
                var texPath = resourceObj.Value.ToString();
                if (!string.IsNullOrEmpty(texPath))
                    node.TexturePath = texPath;
            }
        }

        // Also check for SoftObjectProperty for texture reference
        var softObj = FindProperty<SoftObjectPropertyData>(brushProp.Value, "ResourceObject");
        if (softObj != null)
        {
            var path = softObj.Value.AssetPath.PackageName?.ToString();
            if (!string.IsNullOrEmpty(path))
                node.TexturePath = path;
        }
    }

    private void ExtractSlotLayout(NormalExport slotExport, WidgetNode childNode, WidgetType parentType)
    {
        if (parentType == WidgetType.CanvasPanel)
        {
            ExtractCanvasPanelSlotLayout(slotExport, childNode);
        }
        else if (parentType == WidgetType.StackBox)
        {
            ExtractStackBoxSlotLayout(slotExport, childNode);
        }
        else if (parentType == WidgetType.Overlay)
        {
            ExtractOverlaySlotLayout(slotExport, childNode);
        }
        // SizeBoxSlot, ScaleBoxSlot, GridSlot — extract alignment if available
        else
        {
            ExtractSlotAlignment(slotExport, childNode);
        }
    }

    private void ExtractCanvasPanelSlotLayout(NormalExport slotExport, WidgetNode childNode)
    {
        var layoutData = FindProperty<StructPropertyData>(slotExport.Data, "LayoutData");
        if (layoutData?.Value == null) return;

        // Offsets (Left, Top, Right, Bottom)
        var offsets = FindProperty<StructPropertyData>(layoutData.Value, "Offsets");
        if (offsets?.Value != null)
        {
            var left = FindProperty<FloatPropertyData>(offsets.Value, "Left");
            if (left != null) childNode.OffsetLeft = left.Value;

            var top = FindProperty<FloatPropertyData>(offsets.Value, "Top");
            if (top != null) childNode.OffsetTop = top.Value;

            var right = FindProperty<FloatPropertyData>(offsets.Value, "Right");
            if (right != null) childNode.OffsetRight = right.Value;

            var bottom = FindProperty<FloatPropertyData>(offsets.Value, "Bottom");
            if (bottom != null) childNode.OffsetBottom = bottom.Value;
        }

        // Anchors (Minimum, Maximum — each is a Vector2D)
        var anchors = FindProperty<StructPropertyData>(layoutData.Value, "Anchors");
        if (anchors?.Value != null)
        {
            float minX = 0, minY = 0, maxX = 0, maxY = 0;

            var minimum = FindProperty<StructPropertyData>(anchors.Value, "Minimum");
            if (minimum?.Value != null)
            {
                var x = FindFloatOrDouble(minimum.Value, "X");
                var y = FindFloatOrDouble(minimum.Value, "Y");
                if (x.HasValue) minX = x.Value;
                if (y.HasValue) minY = y.Value;
            }

            var maximum = FindProperty<StructPropertyData>(anchors.Value, "Maximum");
            if (maximum?.Value != null)
            {
                var x = FindFloatOrDouble(maximum.Value, "X");
                var y = FindFloatOrDouble(maximum.Value, "Y");
                if (x.HasValue) maxX = x.Value;
                if (y.HasValue) maxY = y.Value;
            }

            childNode.Anchor = DetectAnchorPreset(minX, minY, maxX, maxY);
            childNode.AnchorMinX = minX;
            childNode.AnchorMinY = minY;
            childNode.AnchorMaxX = maxX;
            childNode.AnchorMaxY = maxY;
        }

        // Alignment
        var alignment = FindProperty<StructPropertyData>(layoutData.Value, "Alignment");
        if (alignment?.Value != null)
        {
            var ax = FindFloatOrDouble(alignment.Value, "X");
            var ay = FindFloatOrDouble(alignment.Value, "Y");
            if (ax.HasValue) childNode.AlignmentX = ax.Value;
            if (ay.HasValue) childNode.AlignmentY = ay.Value;
        }

        // bAutoSize
        var autoSize = FindProperty<BoolPropertyData>(slotExport.Data, "bAutoSize");
        if (autoSize != null) childNode.AutoSize = autoSize.Value;
    }

    private void ExtractStackBoxSlotLayout(NormalExport slotExport, WidgetNode childNode)
    {
        ExtractSlotPadding(slotExport, childNode);
        ExtractSlotAlignment(slotExport, childNode);
    }

    private void ExtractOverlaySlotLayout(NormalExport slotExport, WidgetNode childNode)
    {
        ExtractSlotPadding(slotExport, childNode);
        ExtractSlotAlignment(slotExport, childNode);
    }

    private void ExtractSlotPadding(NormalExport slotExport, WidgetNode childNode)
    {
        var padding = FindProperty<StructPropertyData>(slotExport.Data, "Padding");
        if (padding?.Value != null)
        {
            var left = FindProperty<FloatPropertyData>(padding.Value, "Left");
            var top = FindProperty<FloatPropertyData>(padding.Value, "Top");
            var right = FindProperty<FloatPropertyData>(padding.Value, "Right");
            var bottom = FindProperty<FloatPropertyData>(padding.Value, "Bottom");
            if (left != null) { childNode.Padding = left.Value; childNode.SlotPadLeft = left.Value; }
            if (top != null) childNode.SlotPadTop = top.Value;
            if (right != null) childNode.SlotPadRight = right.Value;
            if (bottom != null) childNode.SlotPadBottom = bottom.Value;
        }
    }

    private static void ExtractSlotAlignment(NormalExport slotExport, WidgetNode childNode)
    {
        var hAlign = FindProperty<BytePropertyData>(slotExport.Data, "HorizontalAlignment");
        if (hAlign != null)
        {
            var val = hAlign.EnumValue?.ToString() ?? hAlign.Value.ToString();
            if (val.Contains("Fill")) childNode.SlotHAlign = "Fill";
            else if (val.Contains("Center")) childNode.SlotHAlign = "Center";
            else if (val.Contains("Right")) childNode.SlotHAlign = "Right";
            else childNode.SlotHAlign = "Left";
        }

        var vAlign = FindProperty<BytePropertyData>(slotExport.Data, "VerticalAlignment");
        if (vAlign != null)
        {
            var val = vAlign.EnumValue?.ToString() ?? vAlign.Value.ToString();
            if (val.Contains("Fill")) childNode.SlotVAlign = "Fill";
            else if (val.Contains("Center")) childNode.SlotVAlign = "Center";
            else if (val.Contains("Bottom")) childNode.SlotVAlign = "Bottom";
            else childNode.SlotVAlign = "Top";
        }
    }

    // === Helpers ===

    /// <summary>
    /// Infer the widget type for unknown class names by checking:
    /// 1. Import chain — does the class inherit from a known widget type?
    /// 2. Properties — does it have Slots (container) or specific widget properties?
    /// 3. Root context — root widgets are always CanvasPanel-like
    /// </summary>
    private static WidgetType? InferWidgetType(NormalExport export, UAsset asset, bool isRoot)
    {
        // Check import chain for known base classes
        var classIndex = export.ClassIndex;
        if (classIndex.Index < 0) // It's an import reference
        {
            var importIdx = -classIndex.Index - 1;
            if (importIdx >= 0 && importIdx < asset.Imports.Count)
            {
                var importName = asset.Imports[importIdx].ObjectName?.ToString() ?? "";
                var knownType = ClassNameToWidgetType(importName);
                if (knownType != null) return knownType;

                // Check parent (OuterIndex) of the import for package hints
                var outerIdx = asset.Imports[importIdx].OuterIndex;
                if (outerIdx.Index < 0)
                {
                    var parentImportIdx = -outerIdx.Index - 1;
                    if (parentImportIdx >= 0 && parentImportIdx < asset.Imports.Count)
                    {
                        var parentName = asset.Imports[parentImportIdx].ObjectName?.ToString() ?? "";
                        // Classes from /Game/Valkyrie/UMG/ are UEFN widget wrappers
                        if (parentName.Contains("UMG") || parentName.Contains("Valkyrie"))
                        {
                            if (importName.Contains("TextBlock")) return WidgetType.TextBlock;
                            if (importName.Contains("Button_Loud")) return WidgetType.ButtonLoud;
                            if (importName.Contains("Button_Quiet")) return WidgetType.ButtonQuiet;
                            if (importName.Contains("Button_Regular")) return WidgetType.ButtonRegular;
                        }
                    }
                }
            }
        }

        // Heuristic: if it has Slots property, it's a container
        var hasSlots = export.Data.Any(p => p.Name?.ToString() == "Slots");

        if (isRoot || hasSlots)
        {
            // Root widgets in UEFN are always CanvasPanel-based
            return WidgetType.CanvasPanel;
        }

        // Check for widget-like properties
        var hasText = export.Data.Any(p => p.Name?.ToString() == "Text" && p is TextPropertyData);
        if (hasText) return WidgetType.TextBlock;

        var hasBrush = export.Data.Any(p => p.Name?.ToString() == "Brush");
        if (hasBrush) return WidgetType.Image;

        // Last resort: if it has a Slot property, it's a child widget — treat as unknown container
        var hasSlot = export.Data.Any(p => p.Name?.ToString() == "Slot");
        if (hasSlot) return WidgetType.CanvasPanel;

        return null;
    }

    private static WidgetType? ClassNameToWidgetType(string className) => className switch
    {
        "CanvasPanel" => WidgetType.CanvasPanel,
        "Image" => WidgetType.Image,
        "UEFN_TextBlock_C" => WidgetType.TextBlock,
        "UEFN_Button_Loud_C" => WidgetType.ButtonLoud,
        "UEFN_Button_Quiet_C" => WidgetType.ButtonQuiet,
        "UEFN_Button_Regular_C" => WidgetType.ButtonRegular,
        "Overlay" => WidgetType.Overlay,
        "StackBox" => WidgetType.StackBox,
        "SizeBox" => WidgetType.SizeBox,
        "ScaleBox" => WidgetType.ScaleBox,
        "GridPanel" => WidgetType.GridPanel,
        _ => null // Unknown widget type — skip
    };

    private static AnchorPreset DetectAnchorPreset(float minX, float minY, float maxX, float maxY)
    {
        // Match against known presets (reverse of WidgetBlueprintBuilder.GetAnchorValues)
        return (minX, minY, maxX, maxY) switch
        {
            (0, 0, 0, 0) => AnchorPreset.TopLeft,
            (0.5f, 0, 0.5f, 0) => AnchorPreset.TopCenter,
            (1, 0, 1, 0) => AnchorPreset.TopRight,
            (0, 0.5f, 0, 0.5f) => AnchorPreset.CenterLeft,
            (0.5f, 0.5f, 0.5f, 0.5f) => AnchorPreset.Center,
            (1, 0.5f, 1, 0.5f) => AnchorPreset.CenterRight,
            (0, 1, 0, 1) => AnchorPreset.BottomLeft,
            (0.5f, 1, 0.5f, 1) => AnchorPreset.BottomCenter,
            (1, 1, 1, 1) => AnchorPreset.BottomRight,
            (0, 0, 1, 1) => AnchorPreset.FullScreen,
            (0, 0, 1, 0) => AnchorPreset.TopStretch,
            (0, 1, 1, 1) => AnchorPreset.BottomStretch,
            (0, 0, 0, 1) => AnchorPreset.LeftStretch,
            (1, 0, 1, 1) => AnchorPreset.RightStretch,
            _ => AnchorPreset.TopLeft // Default for custom anchors
        };
    }

    private static string LinearColorToHex(FLinearColor color)
    {
        var r = (int)Math.Clamp(color.R * 255, 0, 255);
        var g = (int)Math.Clamp(color.G * 255, 0, 255);
        var b = (int)Math.Clamp(color.B * 255, 0, 255);
        var a = (int)Math.Clamp(color.A * 255, 0, 255);

        return a == 255
            ? $"#{r:X2}{g:X2}{b:X2}"
            : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    /// <summary>
    /// Find a property by name in a list of PropertyData, cast to the expected type.
    /// </summary>
    private static T? FindProperty<T>(IEnumerable<PropertyData> props, string name) where T : PropertyData
    {
        foreach (var prop in props)
        {
            if (prop.Name?.ToString() == name && prop is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Find a float value from either FloatPropertyData or DoublePropertyData.
    /// UE5.4 may use either depending on context.
    /// </summary>
    private static float? FindFloatOrDouble(IEnumerable<PropertyData> props, string name)
    {
        foreach (var prop in props)
        {
            if (prop.Name?.ToString() != name) continue;

            if (prop is FloatPropertyData fp) return fp.Value;
            if (prop is DoublePropertyData dp) return (float)dp.Value;
        }
        return null;
    }
}
