namespace FortniteForge.Core.Models;

/// <summary>
/// A validation finding from WidgetSpec.Validate().
/// Path shows the widget tree location, e.g. "Root > ItemGrid > Row1 > Item_0".
/// </summary>
public record WidgetValidationError(string Path, WidgetValidationSeverity Severity, string Message);

public enum WidgetValidationSeverity
{
    Error,
    Warning
}
