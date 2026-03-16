using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace FortniteForge.Core.Services;

/// <summary>
/// Sets property values on UAsset properties by parsing string values.
/// Used by both the MCP tools and the Web dashboard for device configuration.
/// </summary>
public static class PropertyValueSetter
{
    public static void SetPropertyValue(UAsset asset, PropertyData prop, string newValue)
    {
        switch (prop)
        {
            case BoolPropertyData b:
                b.Value = bool.Parse(newValue); break;
            case IntPropertyData i:
                i.Value = int.Parse(newValue); break;
            case FloatPropertyData f:
                f.Value = float.Parse(newValue); break;
            case DoublePropertyData d:
                d.Value = double.Parse(newValue); break;
            case StrPropertyData s:
                s.Value = FString.FromString(newValue); break;
            case NamePropertyData n:
                n.Value = FName.FromString(asset, newValue); break;
            case EnumPropertyData e:
                e.Value = FName.FromString(asset, newValue); break;
            case BytePropertyData bp:
                if (byte.TryParse(newValue, out var bv)) bp.Value = bv;
                else bp.EnumValue = FName.FromString(asset, newValue);
                break;
            default:
                throw new NotSupportedException(
                    $"Cannot set value on {prop.GetType().Name}. " +
                    "Supported types: Bool, Int, Float, Double, String, Name, Enum, Byte.");
        }
    }
}
