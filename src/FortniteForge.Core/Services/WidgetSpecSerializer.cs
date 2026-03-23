using System.Text.Json;
using System.Text.Json.Serialization;
using FortniteForge.Core.Models;

namespace FortniteForge.Core.Services;

/// <summary>
/// Serializes/deserializes WidgetSpec to/from the shared JSON format
/// used by both FortniteForge (→ .uasset) and the UEFN UI Component Creator (→ visual preview).
///
/// The JSON format is the bridge between the two projects. Variable bindings
/// map widget properties (text, color, texture) to editable values that
/// propagate through both the visual canvas and the binary asset.
/// </summary>
public static class WidgetSpecSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Serialize a WidgetSpec to JSON. Auto-generates variables if none exist.
    /// </summary>
    public static string ToJson(WidgetSpec spec)
    {
        if (spec.Variables.Count == 0)
            spec.GenerateVariables();

        var dto = new WidgetSpecDto
        {
            Schema = "widget-spec-v1",
            Name = spec.Name,
            Width = spec.Width,
            Height = spec.Height,
            Variables = spec.Variables.Select(v => new VariableDto
            {
                Id = v.Id,
                Name = v.DisplayName,
                Type = v.Type.ToString().ToLowerInvariant(),
                DefaultValue = v.DefaultValue,
                WidgetName = v.WidgetName,
                WidgetProperty = v.WidgetProperty
            }).ToList(),
            Root = SerializeNode(spec.Root)
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>
    /// Deserialize a WidgetSpec from JSON. Restores variable bindings.
    /// </summary>
    public static WidgetSpec FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<WidgetSpecDto>(json, JsonOptions)
            ?? throw new JsonException("Failed to deserialize WidgetSpec JSON");

        var spec = new WidgetSpec(dto.Name ?? "Unnamed")
        {
            Width = dto.Width > 0 ? dto.Width : 1920,
            Height = dto.Height > 0 ? dto.Height : 1080
        };

        if (dto.Root != null)
            spec.Root = DeserializeNode(dto.Root);

        if (dto.Variables != null)
        {
            spec.Variables = dto.Variables.Select(v => new WidgetVariable
            {
                Id = v.Id ?? "",
                DisplayName = v.Name ?? "",
                Type = Enum.TryParse<WidgetVariableType>(v.Type, true, out var t) ? t : WidgetVariableType.Text,
                DefaultValue = v.DefaultValue ?? "",
                WidgetName = v.WidgetName ?? "",
                WidgetProperty = v.WidgetProperty ?? ""
            }).ToList();
        }

        return spec;
    }

    /// <summary>
    /// Apply variable values to the widget tree. Used before building the .uasset
    /// to inject user-edited values from the visual editor.
    /// </summary>
    public static void ApplyVariables(WidgetSpec spec, Dictionary<string, string> variableValues)
    {
        foreach (var variable in spec.Variables)
        {
            if (!variableValues.TryGetValue(variable.Id, out var value)) continue;

            var node = FindNode(spec.Root, variable.WidgetName);
            if (node == null) continue;

            switch (variable.WidgetProperty)
            {
                case "text":
                    node.Text = value;
                    break;
                case "tintColor":
                    node.TintColor = value;
                    break;
                case "texturePath":
                    node.TexturePath = value;
                    break;
                case "visibility":
                    node.Visibility = value;
                    break;
            }
        }
    }

    private static WidgetNode? FindNode(WidgetNode root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.Children)
        {
            var found = FindNode(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static NodeDto SerializeNode(WidgetNode node)
    {
        return new NodeDto
        {
            Type = node.Type.ToString(),
            Name = node.Name,
            Anchor = node.Anchor != AnchorPreset.TopLeft ? node.Anchor.ToString() : null,
            OffsetLeft = node.OffsetLeft != 0 ? node.OffsetLeft : null,
            OffsetTop = node.OffsetTop != 0 ? node.OffsetTop : null,
            OffsetRight = node.OffsetRight != 0 ? node.OffsetRight : null,
            OffsetBottom = node.OffsetBottom != 0 ? node.OffsetBottom : null,
            AutoSize = node.AutoSize ? true : null,
            TintColor = node.TintColor,
            Text = node.Text,
            TexturePath = node.TexturePath,
            ButtonStyle = node.Type is WidgetType.ButtonLoud or WidgetType.ButtonQuiet or WidgetType.ButtonRegular
                ? node.ButtonStyle.ToString() : null,
            MinWidth = node.MinWidth > 0 ? node.MinWidth : null,
            MinHeight = node.MinHeight > 0 ? node.MinHeight : null,
            Orientation = node.Type == WidgetType.StackBox ? node.Orientation.ToString() : null,
            Padding = node.Padding > 0 ? node.Padding : null,
            Visibility = node.Visibility,

            // Raw anchors (only if non-default)
            AnchorMinX = node.AnchorMinX != 0 ? node.AnchorMinX : null,
            AnchorMinY = node.AnchorMinY != 0 ? node.AnchorMinY : null,
            AnchorMaxX = node.AnchorMaxX != 0 ? node.AnchorMaxX : null,
            AnchorMaxY = node.AnchorMaxY != 0 ? node.AnchorMaxY : null,

            // Slot alignment
            SlotHAlign = node.SlotHAlign,
            SlotVAlign = node.SlotVAlign,
            SlotPadLeft = node.SlotPadLeft != 0 ? node.SlotPadLeft : null,
            SlotPadTop = node.SlotPadTop != 0 ? node.SlotPadTop : null,
            SlotPadRight = node.SlotPadRight != 0 ? node.SlotPadRight : null,
            SlotPadBottom = node.SlotPadBottom != 0 ? node.SlotPadBottom : null,

            // Text visuals
            FontSize = node.FontSize > 0 ? node.FontSize : null,
            FontWeight = node.FontWeight,
            TextColor = node.TextColor,
            Justification = node.Justification,
            LetterSpacing = node.LetterSpacing != 0 ? node.LetterSpacing : null,
            OutlineSize = node.OutlineSize > 0 ? node.OutlineSize : null,
            OutlineColor = node.OutlineColor,

            // Rendering
            RenderOpacity = node.RenderOpacity < 1f ? node.RenderOpacity : null,

            Children = node.Children.Count > 0
                ? node.Children.Select(SerializeNode).ToList()
                : null
        };
    }

    private static WidgetNode DeserializeNode(NodeDto dto)
    {
        var node = new WidgetNode
        {
            Type = Enum.TryParse<WidgetType>(dto.Type, true, out var wt) ? wt : WidgetType.CanvasPanel,
            Name = dto.Name ?? "",
            Anchor = Enum.TryParse<AnchorPreset>(dto.Anchor, true, out var a) ? a : AnchorPreset.TopLeft,
            OffsetLeft = dto.OffsetLeft ?? 0,
            OffsetTop = dto.OffsetTop ?? 0,
            OffsetRight = dto.OffsetRight ?? 0,
            OffsetBottom = dto.OffsetBottom ?? 0,
            AutoSize = dto.AutoSize ?? false,
            TintColor = dto.TintColor,
            Text = dto.Text,
            TexturePath = dto.TexturePath,
            ButtonStyle = Enum.TryParse<ButtonStyle>(dto.ButtonStyle, true, out var bs) ? bs : ButtonStyle.Quiet,
            MinWidth = dto.MinWidth ?? 0,
            MinHeight = dto.MinHeight ?? 0,
            Orientation = Enum.TryParse<Orientation>(dto.Orientation, true, out var o) ? o : Orientation.Vertical,
            Padding = dto.Padding ?? 0,
            Visibility = dto.Visibility
        };

        if (dto.Children != null)
            node.Children = dto.Children.Select(DeserializeNode).ToList();

        return node;
    }

    // DTOs for clean JSON shape
    private class WidgetSpecDto
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }
        public string? Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<VariableDto>? Variables { get; set; }
        public NodeDto? Root { get; set; }
    }

    private class VariableDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? DefaultValue { get; set; }
        public string? WidgetName { get; set; }
        public string? WidgetProperty { get; set; }
    }

    private class NodeDto
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Anchor { get; set; }
        public float? OffsetLeft { get; set; }
        public float? OffsetTop { get; set; }
        public float? OffsetRight { get; set; }
        public float? OffsetBottom { get; set; }
        public bool? AutoSize { get; set; }
        public string? TintColor { get; set; }
        public string? Text { get; set; }
        public string? TexturePath { get; set; }
        public string? ButtonStyle { get; set; }
        public int? MinWidth { get; set; }
        public int? MinHeight { get; set; }
        public string? Orientation { get; set; }
        public float? Padding { get; set; }
        public string? Visibility { get; set; }

        // Raw anchor values
        public float? AnchorMinX { get; set; }
        public float? AnchorMinY { get; set; }
        public float? AnchorMaxX { get; set; }
        public float? AnchorMaxY { get; set; }

        // Slot alignment
        public string? SlotHAlign { get; set; }
        public string? SlotVAlign { get; set; }
        public float? SlotPadLeft { get; set; }
        public float? SlotPadTop { get; set; }
        public float? SlotPadRight { get; set; }
        public float? SlotPadBottom { get; set; }

        // Text visuals
        public float? FontSize { get; set; }
        public string? FontWeight { get; set; }
        public string? TextColor { get; set; }
        public string? Justification { get; set; }
        public int? LetterSpacing { get; set; }
        public int? OutlineSize { get; set; }
        public string? OutlineColor { get; set; }

        // Rendering
        public float? RenderOpacity { get; set; }

        public List<NodeDto>? Children { get; set; }
    }
}
