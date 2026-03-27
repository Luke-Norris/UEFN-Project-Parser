using WellVersed.Core.Models;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace WellVersed.Core.Services;

/// <summary>
/// Builds Widget Blueprint .uasset files from a WidgetSpec.
/// Clone-and-extend approach: starts from a minimal template that has valid
/// WidgetBlueprint/WidgetBlueprintGeneratedClass/WidgetTree infrastructure,
/// then adds widget exports according to the spec.
///
/// Widget blueprints have a dual-tree structure: editor tree + runtime tree.
/// Each widget appears as two exports. The two WidgetTree exports each
/// reference their respective copy.
/// </summary>
public class WidgetBlueprintBuilder
{
    // Import indices (populated from template or created fresh)
    private readonly Dictionary<string, int> _importIndices = new();
    private UAsset _asset = null!;

    // Tracks the export indices for each widget (editor copy, runtime copy)
    private readonly List<(int editorIdx, int runtimeIdx, WidgetNode node)> _widgetExports = new();

    // Tracks slot exports (editor, runtime) linked to their parent + child
    private readonly List<SlotInfo> _slotExports = new();

    private record SlotInfo(int EditorIdx, int RuntimeIdx, int ParentEditorIdx, int ParentRuntimeIdx,
        int ChildEditorIdx, int ChildRuntimeIdx, WidgetNode ChildNode, WidgetType ParentType);

    /// <summary>
    /// Build a Widget Blueprint .uasset from a spec, using a template for the base structure.
    /// </summary>
    /// <param name="spec">The widget tree specification.</param>
    /// <param name="templatePath">Path to a minimal Widget Blueprint template.</param>
    /// <param name="outputPath">Where to write the result.</param>
    public void Build(WidgetSpec spec, string templatePath, string outputPath)
    {
        // Validate the spec before building — fail fast on invalid trees
        var validationErrors = spec.Validate();
        var errors = validationErrors.Where(e => e.Severity == WidgetValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var messages = string.Join("; ", errors.Select(e => $"[{e.Path}] {e.Message}"));
            throw new InvalidOperationException($"WidgetSpec validation failed: {messages}");
        }

        // Load template (minimal: CDO + WidgetBlueprint + GeneratedClass + 2 WidgetTrees)
        _asset = new UAsset(templatePath, EngineVersion.VER_UE5_4);

        // Index existing imports
        IndexImports();

        // Ensure all needed widget class imports exist
        EnsureWidgetImports(spec);

        // Remove any existing widget content exports (keep infrastructure)
        RemoveWidgetContentExports();

        // Build widget tree from spec — creates paired (editor + runtime) exports
        BuildWidgetTree(spec.Root, parentEditorIdx: -1, parentRuntimeIdx: -1, parentType: WidgetType.CanvasPanel);

        // Wire up the WidgetTree exports
        WireWidgetTrees();

        // Rename the blueprint to match spec
        RenameBlueprintExports(spec.Name);

        // Pre-register all name map entries needed for serialization
        PreRegisterNames();

        // Copy .uexp if template has one
        var templateUexp = Path.ChangeExtension(templatePath, ".uexp");
        if (File.Exists(templateUexp))
        {
            var outputUexp = Path.ChangeExtension(outputPath, ".uexp");
            File.Copy(templateUexp, outputUexp, overwrite: true);
        }

        // Write
        _asset.Write(outputPath);
    }

    private void IndexImports()
    {
        for (int i = 0; i < _asset.Imports.Count; i++)
        {
            var name = _asset.Imports[i].ObjectName?.ToString() ?? "";
            _importIndices[name] = i;
        }
    }

    private void EnsureWidgetImports(WidgetSpec spec)
    {
        var needed = CollectNeededImports(spec.Root);

        foreach (var (className, classPath, packagePath) in needed)
        {
            if (!_importIndices.ContainsKey(className))
            {
                // Add the package import first if needed
                int packageImportIdx = -1;
                if (packagePath != null && !_importIndices.ContainsKey(packagePath))
                {
                    var pkgImport = new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), packagePath, false, _asset);
                    _asset.Imports.Add(pkgImport);
                    packageImportIdx = _asset.Imports.Count - 1;
                    _importIndices[packagePath] = packageImportIdx;
                }

                // Add the class import
                var outerIdx = packageImportIdx >= 0
                    ? FPackageIndex.FromImport(packageImportIdx)
                    : new FPackageIndex(0);

                if (classPath != null && !_importIndices.ContainsKey(classPath))
                {
                    var classImport = new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), classPath, false, _asset);
                    _asset.Imports.Add(classImport);
                    _importIndices[classPath] = _asset.Imports.Count - 1;
                }

                var import = new Import("/Script/Engine", "Class",
                    classPath != null ? FPackageIndex.FromImport(_importIndices[classPath]) : new FPackageIndex(0),
                    className, false, _asset);
                _asset.Imports.Add(import);
                _importIndices[className] = _asset.Imports.Count - 1;
            }
        }

        // Ensure slot class imports exist
        EnsureSlotImport("CanvasPanelSlot");
        EnsureSlotImport("OverlaySlot");
        EnsureSlotImport("StackBoxSlot");
        EnsureSlotImport("SizeBoxSlot");
        EnsureSlotImport("ScaleBoxSlot");
        EnsureSlotImport("GridSlot");
    }

    private void EnsureSlotImport(string slotName)
    {
        if (!_importIndices.ContainsKey(slotName))
        {
            var import = new Import("/Script/UMG", "Class", new FPackageIndex(0), slotName, false, _asset);
            _asset.Imports.Add(import);
            _importIndices[slotName] = _asset.Imports.Count - 1;
        }
    }

    private static List<(string className, string? classPath, string? packagePath)> CollectNeededImports(WidgetNode node)
    {
        var result = new List<(string, string?, string?)>();
        CollectImportsRecursive(node, result);
        return result.DistinctBy(x => x.Item1).ToList();
    }

    private static void CollectImportsRecursive(WidgetNode node, List<(string, string?, string?)> result)
    {
        var (className, classPath, packagePath) = node.Type switch
        {
            WidgetType.CanvasPanel => ("CanvasPanel", (string?)null, (string?)null),
            WidgetType.Image => ("Image", null, null),
            WidgetType.TextBlock => ("UEFN_TextBlock_C", "/Game/Valkyrie/UMG/UEFN_TextBlock", "/Game/Valkyrie/UMG"),
            WidgetType.ButtonLoud => ("UEFN_Button_Loud_C", "/Game/Valkyrie/UMG/UEFN_Button_Loud", "/Game/Valkyrie/UMG"),
            WidgetType.ButtonQuiet => ("UEFN_Button_Quiet_C", "/Game/Valkyrie/UMG/UEFN_Button_Quiet", "/Game/Valkyrie/UMG"),
            WidgetType.ButtonRegular => ("UEFN_Button_Regular_C", "/Game/Valkyrie/UMG/UEFN_Button_Regular", "/Game/Valkyrie/UMG"),
            WidgetType.Overlay => ("Overlay", null, null),
            WidgetType.StackBox => ("StackBox", null, null),
            WidgetType.SizeBox => ("SizeBox", null, null),
            WidgetType.ScaleBox => ("ScaleBox", null, null),
            WidgetType.GridPanel => ("GridPanel", null, null),
            _ => ("CanvasPanel", null, null)
        };
        result.Add((className, classPath, packagePath));

        foreach (var child in node.Children)
            CollectImportsRecursive(child, result);
    }

    /// <summary>
    /// Remove existing widget content exports, keeping only infrastructure
    /// (CDO, WidgetBlueprint, WidgetBlueprintGeneratedClass, WidgetTree).
    /// </summary>
    private void RemoveWidgetContentExports()
    {
        var toKeep = new List<Export>();
        foreach (var export in _asset.Exports)
        {
            var cls = export.GetExportClassType()?.ToString() ?? "";
            if (cls == "WidgetBlueprint" || cls == "WidgetBlueprintGeneratedClass" ||
                cls == "WidgetTree" || cls.All(char.IsDigit))
            {
                toKeep.Add(export);
            }
        }
        _asset.Exports.Clear();
        _asset.Exports.AddRange(toKeep);
    }

    /// <summary>
    /// Recursively build the widget tree, creating paired editor+runtime exports.
    /// </summary>
    private void BuildWidgetTree(WidgetNode node, int parentEditorIdx, int parentRuntimeIdx, WidgetType parentType)
    {
        var className = GetClassName(node.Type);
        if (!_importIndices.TryGetValue(className, out var classImportIdx))
            throw new InvalidOperationException($"Missing import for {className}");

        // Create editor export
        var editorExport = CreateWidgetExport(node, classImportIdx);
        _asset.Exports.Add(editorExport);
        var editorIdx = _asset.Exports.Count - 1;

        // Create runtime export
        var runtimeExport = CreateWidgetExport(node, classImportIdx);
        _asset.Exports.Add(runtimeExport);
        var runtimeIdx = _asset.Exports.Count - 1;

        _widgetExports.Add((editorIdx, runtimeIdx, node));

        // If this widget has a parent, create slot exports to connect them
        if (parentEditorIdx >= 0)
        {
            var slotClassName = GetSlotClassName(parentType);
            if (_importIndices.TryGetValue(slotClassName, out var slotImportIdx))
            {
                var editorSlot = CreateSlotExport(node, slotImportIdx, parentEditorIdx, editorIdx, parentType);
                _asset.Exports.Add(editorSlot);
                var editorSlotIdx = _asset.Exports.Count - 1;

                var runtimeSlot = CreateSlotExport(node, slotImportIdx, parentRuntimeIdx, runtimeIdx, parentType);
                _asset.Exports.Add(runtimeSlot);
                var runtimeSlotIdx = _asset.Exports.Count - 1;

                _slotExports.Add(new SlotInfo(editorSlotIdx, runtimeSlotIdx,
                    parentEditorIdx, parentRuntimeIdx, editorIdx, runtimeIdx, node, parentType));

                // Set Slot property on the child widget pointing back to its slot
                if (editorExport is NormalExport edNe)
                    edNe.Data.Add(new ObjectPropertyData(new FName(_asset, "Slot")) { Value = FPackageIndex.FromExport(editorSlotIdx) });
                if (runtimeExport is NormalExport rtNe)
                    rtNe.Data.Add(new ObjectPropertyData(new FName(_asset, "Slot")) { Value = FPackageIndex.FromExport(runtimeSlotIdx) });
            }
        }

        // Recurse into children
        foreach (var child in node.Children)
        {
            BuildWidgetTree(child, editorIdx, runtimeIdx, node.Type);
        }

        // After all children are added, set the Slots array on container widgets
        if (node.Children.Count > 0 && IsContainer(node.Type))
        {
            SetSlotsArray(editorIdx, runtimeIdx);
        }
    }

    private NormalExport CreateWidgetExport(WidgetNode node, int classImportIdx)
    {
        var export = new NormalExport(_asset, Array.Empty<byte>())
        {
            ObjectName = new FName(_asset, node.Name),
            ClassIndex = FPackageIndex.FromImport(classImportIdx),
            OuterIndex = new FPackageIndex(0),
            SuperIndex = new FPackageIndex(0),
            TemplateIndex = new FPackageIndex(0),
            ObjectFlags = EObjectFlags.RF_Public | EObjectFlags.RF_DefaultSubObject,
            Data = new List<PropertyData>()
        };

        // Set type-specific properties
        switch (node.Type)
        {
            case WidgetType.Image:
                if (node.TintColor != null)
                    export.Data.Add(CreateBrushProperty(node.TintColor));
                break;

            case WidgetType.TextBlock:
                if (node.Text != null)
                {
                    export.Data.Add(new TextPropertyData(new FName(_asset, "Text"))
                    {
                        Value = FString.FromString(node.Text),
                        HistoryType = TextHistoryType.Base,
                        CultureInvariantString = FString.FromString(node.Text)
                    });
                }
                break;

            case WidgetType.ButtonLoud:
            case WidgetType.ButtonQuiet:
            case WidgetType.ButtonRegular:
                if (node.MinWidth > 0)
                    export.Data.Add(new IntPropertyData(new FName(_asset, "MinWidth")) { Value = node.MinWidth });
                if (node.MinHeight > 0)
                    export.Data.Add(new IntPropertyData(new FName(_asset, "MinHeight")) { Value = node.MinHeight });
                break;

            case WidgetType.StackBox:
                if (node.Orientation == Orientation.Horizontal)
                {
                    export.Data.Add(new BytePropertyData(new FName(_asset, "Orientation"))
                    {
                        ByteType = BytePropertyType.FName,
                        EnumType = new FName(_asset, "EOrientation"),
                        EnumValue = new FName(_asset, "Orient_Horizontal")
                    });
                }
                break;

            case WidgetType.SizeBox:
                if (node.MinWidth > 0)
                    export.Data.Add(new FloatPropertyData(new FName(_asset, "WidthOverride")) { Value = node.MinWidth });
                if (node.MinHeight > 0)
                    export.Data.Add(new FloatPropertyData(new FName(_asset, "HeightOverride")) { Value = node.MinHeight });
                break;
        }

        // Expanded in designer (helps UEFN display the tree)
        if (node.Children.Count > 0)
        {
            export.Data.Add(new BoolPropertyData(new FName(_asset, "bExpandedInDesigner")) { Value = true });
        }

        // Visibility
        if (node.Visibility != null)
        {
            export.Data.Add(new EnumPropertyData(new FName(_asset, "Visibility"))
            {
                EnumType = new FName(_asset, "ESlateVisibility"),
                Value = new FName(_asset, $"ESlateVisibility::{node.Visibility}")
            });
        }

        return export;
    }

    private NormalExport CreateSlotExport(WidgetNode childNode, int slotClassImportIdx,
        int parentExportIdx, int childExportIdx, WidgetType parentType)
    {
        var export = new NormalExport(_asset, Array.Empty<byte>())
        {
            ObjectName = new FName(_asset, GetSlotClassName(parentType) + "_0"),
            ClassIndex = FPackageIndex.FromImport(slotClassImportIdx),
            OuterIndex = new FPackageIndex(0),
            SuperIndex = new FPackageIndex(0),
            TemplateIndex = new FPackageIndex(0),
            ObjectFlags = EObjectFlags.RF_Public | EObjectFlags.RF_DefaultSubObject,
            Data = new List<PropertyData>()
        };

        // Parent/Content links
        export.Data.Add(new ObjectPropertyData(new FName(_asset, "Parent"))
        {
            Value = FPackageIndex.FromExport(parentExportIdx)
        });
        export.Data.Add(new ObjectPropertyData(new FName(_asset, "Content"))
        {
            Value = FPackageIndex.FromExport(childExportIdx)
        });

        // Layout properties depend on parent type
        if (parentType == WidgetType.CanvasPanel)
        {
            export.Data.Insert(0, CreateLayoutDataProperty(childNode));

            if (childNode.AutoSize)
            {
                export.Data.Add(new BoolPropertyData(new FName(_asset, "bAutoSize")) { Value = true });
            }
        }
        else if (parentType == WidgetType.StackBox)
        {
            if (childNode.Padding > 0)
            {
                export.Data.Insert(0, CreatePaddingProperty(childNode.Padding));
            }
        }

        return export;
    }

    private StructPropertyData CreateLayoutDataProperty(WidgetNode node)
    {
        // Only emit Offsets for positioning. Anchors default to TopLeft (0,0→0,0).
        // Users can adjust anchors in UEFN's Widget Designer after generation.
        // Margin struct uses NTPL (named tagged property list) serialization — just floats.
        var offsets = new StructPropertyData(new FName(_asset, "Offsets"))
        {
            StructType = new FName(_asset, "Margin"),
            Value = new List<PropertyData>
            {
                new FloatPropertyData(new FName(_asset, "Left")) { Value = node.OffsetLeft },
                new FloatPropertyData(new FName(_asset, "Top")) { Value = node.OffsetTop },
                new FloatPropertyData(new FName(_asset, "Right")) { Value = node.OffsetRight },
                new FloatPropertyData(new FName(_asset, "Bottom")) { Value = node.OffsetBottom }
            }
        };

        return new StructPropertyData(new FName(_asset, "LayoutData"))
        {
            StructType = new FName(_asset, "AnchorData"),
            Value = new List<PropertyData> { offsets }
        };
    }

    private StructPropertyData CreatePaddingProperty(float padding)
    {
        return new StructPropertyData(new FName(_asset, "Padding"))
        {
            StructType = new FName(_asset, "Margin"),
            Value = new List<PropertyData>
            {
                new FloatPropertyData(new FName(_asset, "Left")) { Value = padding },
                new FloatPropertyData(new FName(_asset, "Top")) { Value = padding },
                new FloatPropertyData(new FName(_asset, "Right")) { Value = padding },
                new FloatPropertyData(new FName(_asset, "Bottom")) { Value = padding }
            }
        };
    }

    private StructPropertyData CreateBrushProperty(string hexColor)
    {
        var (r, g, b, a) = ParseHexColor(hexColor);

        // Use LinearColorPropertyData directly — it has custom serialization
        var specifiedColor = new LinearColorPropertyData(new FName(_asset, "SpecifiedColor"))
        {
            Value = new FLinearColor(r, g, b, a)
        };

        var tintColor = new StructPropertyData(new FName(_asset, "TintColor"))
        {
            StructType = new FName(_asset, "SlateColor"),
            Value = new List<PropertyData> { specifiedColor }
        };

        return new StructPropertyData(new FName(_asset, "Brush"))
        {
            StructType = new FName(_asset, "SlateBrush"),
            Value = new List<PropertyData>
            {
                new BytePropertyData(new FName(_asset, "ImageType"))
                {
                    ByteType = BytePropertyType.FName,
                    EnumType = new FName(_asset, "ESlateBrushImageType"),
                    EnumValue = new FName(_asset, "ESlateBrushImageType::FullColor")
                },
                tintColor
            }
        };
    }

    /// <summary>
    /// Set the Slots array property on container widget exports.
    /// Called after all children have been added so we know the slot indices.
    /// </summary>
    private void SetSlotsArray(int editorIdx, int runtimeIdx)
    {
        var editorSlots = _slotExports
            .Where(s => s.ParentEditorIdx == editorIdx)
            .Select((s, i) => (PropertyData)new ObjectPropertyData(new FName(_asset, i.ToString()))
                { Value = FPackageIndex.FromExport(s.EditorIdx) })
            .ToArray();

        var runtimeSlots = _slotExports
            .Where(s => s.ParentRuntimeIdx == runtimeIdx)
            .Select((s, i) => (PropertyData)new ObjectPropertyData(new FName(_asset, i.ToString()))
                { Value = FPackageIndex.FromExport(s.RuntimeIdx) })
            .ToArray();

        if (editorSlots.Length > 0 && _asset.Exports[editorIdx] is NormalExport edExport)
        {
            // Remove existing Slots if any
            edExport.Data.RemoveAll(p => p.Name?.ToString() == "Slots");
            edExport.Data.Insert(0, new ArrayPropertyData(new FName(_asset, "Slots"))
            {
                ArrayType = new FName(_asset, "ObjectProperty"),
                Value = editorSlots
            });
        }

        if (runtimeSlots.Length > 0 && _asset.Exports[runtimeIdx] is NormalExport rtExport)
        {
            rtExport.Data.RemoveAll(p => p.Name?.ToString() == "Slots");
            rtExport.Data.Insert(0, new ArrayPropertyData(new FName(_asset, "Slots"))
            {
                ArrayType = new FName(_asset, "ObjectProperty"),
                Value = runtimeSlots
            });
        }
    }

    /// <summary>
    /// Wire up the two WidgetTree exports (editor + runtime) to point at the root widgets
    /// and list all widgets in AllWidgets.
    /// </summary>
    private void WireWidgetTrees()
    {
        var widgetTreeIndices = new List<int>();
        for (int i = 0; i < _asset.Exports.Count; i++)
        {
            if (_asset.Exports[i].GetExportClassType()?.ToString() == "WidgetTree")
                widgetTreeIndices.Add(i);
        }

        if (widgetTreeIndices.Count < 2 || _widgetExports.Count == 0) return;

        var rootEditor = _widgetExports[0].editorIdx;
        var rootRuntime = _widgetExports[0].runtimeIdx;

        // Editor tree
        SetWidgetTreeData(widgetTreeIndices[0], rootEditor,
            _widgetExports.Select(w => w.editorIdx).ToArray());

        // Runtime tree
        SetWidgetTreeData(widgetTreeIndices[1], rootRuntime,
            _widgetExports.Select(w => w.runtimeIdx).ToArray());
    }

    private void SetWidgetTreeData(int treeExportIdx, int rootWidgetIdx, int[] allWidgetIndices)
    {
        if (_asset.Exports[treeExportIdx] is not NormalExport treeExport) return;

        treeExport.Data.Clear();

        treeExport.Data.Add(new ObjectPropertyData(new FName(_asset, "RootWidget"))
        {
            Value = FPackageIndex.FromExport(rootWidgetIdx)
        });

        treeExport.Data.Add(new ArrayPropertyData(new FName(_asset, "AllWidgets"))
        {
            ArrayType = new FName(_asset, "ObjectProperty"),
            Value = allWidgetIndices
                .Select((idx, i) => (PropertyData)new ObjectPropertyData(new FName(_asset, i.ToString()))
                    { Value = FPackageIndex.FromExport(idx) })
                .ToArray()
        });
    }

    private void RenameBlueprintExports(string name)
    {
        foreach (var export in _asset.Exports)
        {
            var cls = export.GetExportClassType()?.ToString() ?? "";
            var objName = export.ObjectName?.ToString() ?? "";

            if (cls == "WidgetBlueprint")
                export.ObjectName = new FName(_asset, name);
            else if (cls == "WidgetBlueprintGeneratedClass")
                export.ObjectName = new FName(_asset, name + "_C");
            else if (cls.All(char.IsDigit) && objName.StartsWith("Default__"))
                export.ObjectName = new FName(_asset, $"Default__{name}_C");
        }
    }

    /// <summary>
    /// Pre-register all FName entries in the name map before serialization.
    /// UAssetAPI freezes the name map during Write(), so all names must exist beforehand.
    /// </summary>
    private void PreRegisterNames()
    {
        var names = new[]
        {
            // Property type names
            "BoolProperty", "ByteProperty", "IntProperty", "FloatProperty", "DoubleProperty",
            "StrProperty", "TextProperty", "NameProperty", "ObjectProperty", "StructProperty",
            "ArrayProperty", "EnumProperty", "SoftObjectProperty",
            // Struct type names
            "Margin", "AnchorData", "SlateBrush", "SlateColor", "LinearColor", "Vector2D",
            // Property names used
            "None", "Slots", "Slot", "Parent", "Content", "LayoutData", "Offsets",
            "Left", "Top", "Right", "Bottom", "bAutoSize", "bExpandedInDesigner",
            "Brush", "ImageType", "TintColor", "SpecifiedColor",
            "Text", "Visibility", "Orientation", "MinWidth", "MinHeight",
            "WidthOverride", "HeightOverride", "Padding", "RootWidget", "AllWidgets",
            "WidgetTree", "GeneratedClass", "DisplayLabel",
            // Enum type names
            "ESlateBrushImageType", "ESlateBrushImageType::FullColor",
            "EOrientation", "Orient_Horizontal", "Orient_Vertical",
            "ESlateVisibility", "ESlateVisibility::Visible",
            "ESlateVisibility::Collapsed", "ESlateVisibility::Hidden",
            // Slot class names
            "CanvasPanelSlot", "OverlaySlot", "StackBoxSlot", "SizeBoxSlot",
            "ScaleBoxSlot", "GridSlot",
            // Widget class names
            "CanvasPanel", "Image", "Overlay", "StackBox", "SizeBox", "ScaleBox", "GridPanel",
            "UEFN_TextBlock_C", "UEFN_Button_Loud_C", "UEFN_Button_Quiet_C", "UEFN_Button_Regular_C",
        };

        foreach (var name in names)
        {
            if (!_asset.ContainsNameReference(new FString(name)))
                _asset.AddNameReference(new FString(name));
        }

        // Also register names from all exports and their properties
        foreach (var export in _asset.Exports)
        {
            var objName = export.ObjectName?.ToString();
            if (objName != null && !_asset.ContainsNameReference(new FString(objName)))
                _asset.AddNameReference(new FString(objName));

            if (export is NormalExport ne)
            {
                RegisterPropertyNames(ne.Data);
            }
        }
    }

    private void RegisterPropertyNames(IEnumerable<PropertyData> props)
    {
        foreach (var prop in props)
        {
            var name = prop.Name?.ToString();
            if (name != null && !_asset.ContainsNameReference(new FString(name)))
                _asset.AddNameReference(new FString(name));

            var propType = prop.PropertyType?.ToString();
            if (propType != null && !_asset.ContainsNameReference(new FString(propType)))
                _asset.AddNameReference(new FString(propType));

            if (prop is StructPropertyData sp)
            {
                var structType = sp.StructType?.ToString();
                if (structType != null && !_asset.ContainsNameReference(new FString(structType)))
                    _asset.AddNameReference(new FString(structType));
                RegisterPropertyNames(sp.Value);
            }
            else if (prop is ArrayPropertyData ap)
            {
                var arrayType = ap.ArrayType?.ToString();
                if (arrayType != null && !_asset.ContainsNameReference(new FString(arrayType)))
                    _asset.AddNameReference(new FString(arrayType));
                RegisterPropertyNames(ap.Value);
            }
            else if (prop is BytePropertyData bp)
            {
                var enumType = bp.EnumType?.ToString();
                if (enumType != null && !_asset.ContainsNameReference(new FString(enumType)))
                    _asset.AddNameReference(new FString(enumType));
                var enumVal = bp.EnumValue?.ToString();
                if (enumVal != null && !_asset.ContainsNameReference(new FString(enumVal)))
                    _asset.AddNameReference(new FString(enumVal));
            }
            else if (prop is EnumPropertyData ep)
            {
                var enumType = ep.EnumType?.ToString();
                if (enumType != null && !_asset.ContainsNameReference(new FString(enumType)))
                    _asset.AddNameReference(new FString(enumType));
                var enumVal = ep.Value?.ToString();
                if (enumVal != null && !_asset.ContainsNameReference(new FString(enumVal)))
                    _asset.AddNameReference(new FString(enumVal));
            }
        }
    }

    // === Helpers ===

    private static string GetClassName(WidgetType type) => type switch
    {
        WidgetType.CanvasPanel => "CanvasPanel",
        WidgetType.Image => "Image",
        WidgetType.TextBlock => "UEFN_TextBlock_C",
        WidgetType.ButtonLoud => "UEFN_Button_Loud_C",
        WidgetType.ButtonQuiet => "UEFN_Button_Quiet_C",
        WidgetType.ButtonRegular => "UEFN_Button_Regular_C",
        WidgetType.Overlay => "Overlay",
        WidgetType.StackBox => "StackBox",
        WidgetType.SizeBox => "SizeBox",
        WidgetType.ScaleBox => "ScaleBox",
        WidgetType.GridPanel => "GridPanel",
        _ => "CanvasPanel"
    };

    private static string GetSlotClassName(WidgetType parentType) => WidgetRules.GetSlotType(parentType);

    private static bool IsContainer(WidgetType type) => WidgetRules.IsContainer(type);

    private static (float minX, float minY, float maxX, float maxY) GetAnchorValues(AnchorPreset preset) => preset switch
    {
        AnchorPreset.TopLeft => (0, 0, 0, 0),
        AnchorPreset.TopCenter => (0.5f, 0, 0.5f, 0),
        AnchorPreset.TopRight => (1, 0, 1, 0),
        AnchorPreset.CenterLeft => (0, 0.5f, 0, 0.5f),
        AnchorPreset.Center => (0.5f, 0.5f, 0.5f, 0.5f),
        AnchorPreset.CenterRight => (1, 0.5f, 1, 0.5f),
        AnchorPreset.BottomLeft => (0, 1, 0, 1),
        AnchorPreset.BottomCenter => (0.5f, 1, 0.5f, 1),
        AnchorPreset.BottomRight => (1, 1, 1, 1),
        AnchorPreset.FullScreen => (0, 0, 1, 1),
        AnchorPreset.TopStretch => (0, 0, 1, 0),
        AnchorPreset.BottomStretch => (0, 1, 1, 1),
        AnchorPreset.LeftStretch => (0, 0, 0, 1),
        AnchorPreset.RightStretch => (1, 0, 1, 1),
        _ => (0, 0, 0, 0)
    };

    private static (float r, float g, float b, float a) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return (
                int.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber) / 255f,
                1f
            );
        }
        if (hex.Length == 8)
        {
            return (
                int.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber) / 255f,
                int.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber) / 255f
            );
        }
        return (1, 1, 1, 1);
    }
}
