namespace FortniteForge.Core.Models;

/// <summary>
/// Declarative specification for a Widget Blueprint.
/// Describes the widget tree hierarchy, layout, and visual properties.
/// Used by WidgetBlueprintBuilder to generate .uasset files.
/// </summary>
public class WidgetSpec
{
    public string Name { get; set; } = "";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public WidgetNode Root { get; set; } = new() { Type = WidgetType.CanvasPanel, Name = "Root" };
    public List<WidgetVariable> Variables { get; set; } = new();
    public List<string> TextureImports { get; set; } = new();

    public WidgetSpec(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Validate the widget tree against UEFN containment rules.
    /// Returns a list of errors and warnings. An empty list means the spec is valid.
    /// </summary>
    public List<WidgetValidationError> Validate()
    {
        var errors = new List<WidgetValidationError>();

        // Root must be CanvasPanel — UEFN's AddWidget() only accepts canvas
        if (Root.Type != WidgetType.CanvasPanel)
        {
            errors.Add(new WidgetValidationError(
                Root.Name,
                WidgetValidationSeverity.Error,
                $"Root widget must be CanvasPanel, got {Root.Type}"));
        }

        // Validate the tree recursively
        ValidateNode(Root, Root.Name, errors);

        return errors;
    }

    private void ValidateNode(WidgetNode node, string path, List<WidgetValidationError> errors)
    {
        // Single-child container check
        if (WidgetRules.SingleChildContainers.Contains(node.Type) && node.Children.Count > 1)
        {
            errors.Add(new WidgetValidationError(
                path,
                WidgetValidationSeverity.Error,
                $"{node.Type} accepts only 1 child, has {node.Children.Count}"));
        }

        // Non-container with children
        if (!WidgetRules.IsContainer(node.Type) && node.Children.Count > 0)
        {
            errors.Add(new WidgetValidationError(
                path,
                WidgetValidationSeverity.Error,
                $"{node.Type} is not a container and cannot have children"));
        }

        // Check each child against parent's allowed types
        foreach (var child in node.Children)
        {
            var childPath = $"{path} > {child.Name}";

            if (WidgetRules.IsContainer(node.Type) && !WidgetRules.CanContain(node.Type, child.Type))
            {
                errors.Add(new WidgetValidationError(
                    childPath,
                    WidgetValidationSeverity.Error,
                    $"{child.Type} is not allowed in {WidgetRules.GetSlotType(node.Type)}"));
            }

            // Warn on missing recommended properties
            if (child.Type == WidgetType.TextBlock && child.Text == null)
            {
                errors.Add(new WidgetValidationError(
                    childPath,
                    WidgetValidationSeverity.Warning,
                    "TextBlock has no Text set"));
            }

            if (child.Type is WidgetType.ButtonLoud or WidgetType.ButtonQuiet or WidgetType.ButtonRegular
                && child.Text == null)
            {
                errors.Add(new WidgetValidationError(
                    childPath,
                    WidgetValidationSeverity.Warning,
                    "Button has no Text set"));
            }

            // Recurse
            ValidateNode(child, childPath, errors);
        }
    }

    /// <summary>
    /// Auto-generate variables from the widget tree.
    /// Creates a variable for every editable property (text, color, texture, etc.)
    /// </summary>
    public void GenerateVariables()
    {
        Variables.Clear();
        GenerateVariablesRecursive(Root);
    }

    private void GenerateVariablesRecursive(WidgetNode node)
    {
        // TextBlock → text variable
        if (node.Type == WidgetType.TextBlock && node.Text != null)
        {
            Variables.Add(new WidgetVariable
            {
                Id = $"var-{node.Name}-text",
                DisplayName = $"{node.Name} Text",
                Type = WidgetVariableType.Text,
                DefaultValue = node.Text,
                WidgetName = node.Name,
                WidgetProperty = "text"
            });
        }

        // Button → text variable
        if (node.Type is WidgetType.ButtonLoud or WidgetType.ButtonQuiet or WidgetType.ButtonRegular
            && node.Text != null)
        {
            Variables.Add(new WidgetVariable
            {
                Id = $"var-{node.Name}-text",
                DisplayName = $"{node.Name} Text",
                Type = WidgetVariableType.Text,
                DefaultValue = node.Text,
                WidgetName = node.Name,
                WidgetProperty = "text"
            });
        }

        // Image with tint → color variable
        if (node.Type == WidgetType.Image && node.TintColor != null)
        {
            Variables.Add(new WidgetVariable
            {
                Id = $"var-{node.Name}-color",
                DisplayName = $"{node.Name} Color",
                Type = WidgetVariableType.Color,
                DefaultValue = node.TintColor,
                WidgetName = node.Name,
                WidgetProperty = "tintColor"
            });
        }

        // Image with texture → image variable
        if (node.Type == WidgetType.Image && node.TexturePath != null)
        {
            Variables.Add(new WidgetVariable
            {
                Id = $"var-{node.Name}-image",
                DisplayName = $"{node.Name} Image",
                Type = WidgetVariableType.Image,
                DefaultValue = node.TexturePath,
                WidgetName = node.Name,
                WidgetProperty = "texturePath"
            });
        }

        // Image placeholder (no tint, no texture) → image variable for swapping
        if (node.Type == WidgetType.Image && node.TintColor == null && node.TexturePath == null)
        {
            Variables.Add(new WidgetVariable
            {
                Id = $"var-{node.Name}-image",
                DisplayName = $"{node.Name} Image",
                Type = WidgetVariableType.Image,
                DefaultValue = "",
                WidgetName = node.Name,
                WidgetProperty = "texturePath"
            });
        }

        foreach (var child in node.Children)
            GenerateVariablesRecursive(child);
    }
}

/// <summary>
/// A bindable variable in a WidgetSpec. Links a user-editable value
/// to a specific widget's property. Used by both the visual editor
/// (Component Creator) and the .uasset builder (FortniteForge).
/// </summary>
public class WidgetVariable
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public WidgetVariableType Type { get; set; }
    public string DefaultValue { get; set; } = "";
    /// <summary>The WidgetNode.Name this variable targets.</summary>
    public string WidgetName { get; set; } = "";
    /// <summary>The property on the widget: "text", "tintColor", "texturePath", "visibility".</summary>
    public string WidgetProperty { get; set; } = "";
}

public enum WidgetVariableType
{
    Text,
    Color,
    Image,
    Number
}

public class WidgetNode
{
    public WidgetType Type { get; set; }
    public string Name { get; set; } = "";
    public List<WidgetNode> Children { get; set; } = new();

    // Layout (for CanvasPanelSlot)
    public AnchorPreset Anchor { get; set; } = AnchorPreset.TopLeft;
    public float OffsetLeft { get; set; }
    public float OffsetTop { get; set; }
    public float OffsetRight { get; set; }
    public float OffsetBottom { get; set; }
    public float AlignmentX { get; set; }
    public float AlignmentY { get; set; }
    public bool AutoSize { get; set; }

    // Raw anchor values (0-1, for stretch vs point-anchor computation)
    public float AnchorMinX { get; set; }
    public float AnchorMinY { get; set; }
    public float AnchorMaxX { get; set; }
    public float AnchorMaxY { get; set; }

    // Slot alignment (for Overlay/StackBox slots)
    public string? SlotHAlign { get; set; }  // "Left", "Center", "Right", "Fill"
    public string? SlotVAlign { get; set; }  // "Top", "Center", "Bottom", "Fill"
    public float SlotPadLeft { get; set; }
    public float SlotPadTop { get; set; }
    public float SlotPadRight { get; set; }
    public float SlotPadBottom { get; set; }

    // Visual properties
    public string? TintColor { get; set; }         // hex color for Image brush
    public string? Text { get; set; }               // for TextBlock
    public string? TexturePath { get; set; }        // for Image brush ResourceObject
    public ButtonStyle ButtonStyle { get; set; } = ButtonStyle.Quiet;
    public int MinWidth { get; set; }
    public int MinHeight { get; set; }
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public float Padding { get; set; }
    public string? Visibility { get; set; }         // "Visible", "Collapsed", "Hidden"

    // Text visual properties
    public float FontSize { get; set; }             // from Font.Size (0 = default)
    public string? FontWeight { get; set; }         // from Font.TypefaceFontName ("Bold", etc.)
    public string? TextColor { get; set; }          // hex from ColorAndOpacity
    public string? Justification { get; set; }      // "Left", "Center", "Right"
    public int LetterSpacing { get; set; }
    public int OutlineSize { get; set; }
    public string? OutlineColor { get; set; }       // hex

    // Rendering
    public float RenderOpacity { get; set; } = 1f;
}

public enum WidgetType
{
    CanvasPanel,
    Image,
    TextBlock,          // UEFN_TextBlock_C
    ButtonLoud,         // UEFN_Button_Loud_C
    ButtonQuiet,        // UEFN_Button_Quiet_C
    ButtonRegular,      // UEFN_Button_Regular_C
    Overlay,
    StackBox,
    SizeBox,
    ScaleBox,
    GridPanel
}

public enum AnchorPreset
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    FullScreen,         // Anchors 0,0 → 1,1
    TopStretch,         // Anchors 0,0 → 1,0
    BottomStretch,      // Anchors 0,1 → 1,1
    LeftStretch,        // Anchors 0,0 → 0,1
    RightStretch        // Anchors 1,0 → 1,1
}

public enum ButtonStyle
{
    Loud,
    Quiet,
    Regular
}

public enum Orientation
{
    Horizontal,
    Vertical
}

/// <summary>
/// Fluent builder for constructing WidgetSpec trees.
/// </summary>
public class WidgetSpecBuilder
{
    private readonly WidgetSpec _spec;
    private readonly Stack<WidgetNode> _stack = new();
    private WidgetNode _current;

    public WidgetSpecBuilder(string name)
    {
        _spec = new WidgetSpec(name);
        _current = _spec.Root;
    }

    public WidgetSpec Build() => _spec;

    // Container widgets — push onto stack
    public WidgetSpecBuilder Canvas(string name, AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = false)
    {
        var node = new WidgetNode
        {
            Type = WidgetType.CanvasPanel, Name = name, Anchor = anchor,
            OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        };
        _current.Children.Add(node);
        _stack.Push(_current);
        _current = node;
        return this;
    }

    public WidgetSpecBuilder Overlay(string name, AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = false)
    {
        var node = new WidgetNode
        {
            Type = WidgetType.Overlay, Name = name, Anchor = anchor,
            OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        };
        _current.Children.Add(node);
        _stack.Push(_current);
        _current = node;
        return this;
    }

    public WidgetSpecBuilder Stack(string name, Orientation orientation = Orientation.Vertical,
        AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = false)
    {
        var node = new WidgetNode
        {
            Type = WidgetType.StackBox, Name = name, Orientation = orientation, Anchor = anchor,
            OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        };
        _current.Children.Add(node);
        _stack.Push(_current);
        _current = node;
        return this;
    }

    public WidgetSpecBuilder SizeBox(string name, int width = 0, int height = 0,
        AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        var node = new WidgetNode
        {
            Type = WidgetType.SizeBox, Name = name, MinWidth = width, MinHeight = height,
            Anchor = anchor, OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom
        };
        _current.Children.Add(node);
        _stack.Push(_current);
        _current = node;
        return this;
    }

    // Pop back to parent
    public WidgetSpecBuilder End()
    {
        if (_stack.Count > 0)
            _current = _stack.Pop();
        return this;
    }

    // Leaf widgets
    public WidgetSpecBuilder Image(string name, string? tint = null, string? texture = null,
        AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = false)
    {
        var node = new WidgetNode
        {
            Type = WidgetType.Image, Name = name, TintColor = tint, TexturePath = texture,
            Anchor = anchor, OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        };
        if (texture != null && !_spec.TextureImports.Contains(texture))
            _spec.TextureImports.Add(texture);
        _current.Children.Add(node);
        return this;
    }

    public WidgetSpecBuilder Text(string name, string text = "",
        AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = true)
    {
        _current.Children.Add(new WidgetNode
        {
            Type = WidgetType.TextBlock, Name = name, Text = text,
            Anchor = anchor, OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        });
        return this;
    }

    public WidgetSpecBuilder Button(string name, ButtonStyle style = ButtonStyle.Quiet, string text = "",
        int minWidth = 0, int minHeight = 0,
        AnchorPreset anchor = AnchorPreset.TopLeft,
        float left = 0, float top = 0, float right = 0, float bottom = 0, bool autoSize = true)
    {
        var type = style switch
        {
            ButtonStyle.Loud => WidgetType.ButtonLoud,
            ButtonStyle.Regular => WidgetType.ButtonRegular,
            _ => WidgetType.ButtonQuiet
        };
        _current.Children.Add(new WidgetNode
        {
            Type = type, Name = name, Text = text, ButtonStyle = style,
            MinWidth = minWidth, MinHeight = minHeight,
            Anchor = anchor, OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom,
            AutoSize = autoSize
        });
        return this;
    }

    /// <summary>
    /// Count all widgets in the tree (for sizing estimation).
    /// </summary>
    public int CountWidgets()
    {
        return CountNodes(_spec.Root);
    }

    private static int CountNodes(WidgetNode node)
    {
        int count = 1;
        foreach (var child in node.Children)
            count += CountNodes(child);
        return count;
    }
}
