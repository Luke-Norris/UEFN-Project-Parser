using WellVersed.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace WellVersed.MCP.Tools;

/// <summary>
/// MCP tools for the Device Property Encyclopedia — searchable reference
/// for all UEFN Creative Devices with schemas, usage stats, and common configs.
/// </summary>
[McpServerToolType]
public class EncyclopediaTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool, Description(
        "Searches the device encyclopedia for devices and properties matching a query. " +
        "Fuzzy-matches across device names, property names, event names, and common configuration descriptions. " +
        "Returns ranked results with match context, property/event counts, and usage frequency.")]
    public string search_device_encyclopedia(
        DeviceEncyclopedia encyclopedia,
        [Description("Search query — device name, property name, or description keyword (e.g., 'trigger', 'damage zone', 'team spawner')")] string query)
    {
        var results = encyclopedia.SearchDevices(query);

        if (results.Count == 0)
            return $"No devices or properties matching '{query}'. Try broader terms like 'trigger', 'spawner', 'barrier', or 'zone'.";

        return JsonSerializer.Serialize(new
        {
            query,
            resultCount = results.Count,
            results = results.Select(r => new
            {
                r.DeviceName,
                r.DisplayName,
                r.ParentClass,
                r.PropertyCount,
                r.EventCount,
                r.FunctionCount,
                r.HasCommonConfigs,
                r.UsageCount,
                matchedProperties = r.MatchedProperties.Count > 0 ? r.MatchedProperties : null,
                matchedEvents = r.MatchedEvents.Count > 0 ? r.MatchedEvents : null,
                matchContext = r.MatchContext
            })
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets the full reference documentation for a device type. " +
        "Includes all properties with types, defaults, descriptions, and usage statistics from real maps. " +
        "Also includes events, functions, and pre-built common configurations.")]
    public string get_device_reference(
        DeviceEncyclopedia encyclopedia,
        [Description("Device class name (e.g., 'trigger_device', 'mutator_zone_device', 'barrier_device')")] string deviceClass)
    {
        var reference = encyclopedia.GetDeviceReference(deviceClass);

        if (reference == null)
        {
            // Try to find similar devices
            var suggestions = encyclopedia.SearchDevices(deviceClass);
            if (suggestions.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"No reference found for '{deviceClass}'.",
                    suggestions = suggestions.Take(5).Select(s => new { s.DeviceName, s.DisplayName })
                }, JsonOpts);
            }
            return $"No reference found for '{deviceClass}'. Use search_device_encyclopedia to find the correct name.";
        }

        return JsonSerializer.Serialize(new
        {
            reference.DeviceName,
            reference.DisplayName,
            reference.ParentClass,
            reference.Description,
            reference.SourceFile,
            reference.TotalUsageCount,
            reference.ProjectsUsedIn,
            properties = reference.Properties.Select(p => new
            {
                p.Name,
                p.Type,
                p.DefaultValue,
                p.IsEditable,
                p.Description,
                usagePercent = p.UsagePercent > 0 ? p.UsagePercent : (double?)null,
                commonValues = p.CommonValues.Count > 0 ? p.CommonValues : null,
                relatedProperties = p.RelatedProperties.Count > 0 ? p.RelatedProperties : null
            }),
            reference.Events,
            reference.Functions,
            commonConfigurations = reference.CommonConfigurations.Count > 0
                ? reference.CommonConfigurations.Select(c => new
                {
                    c.Name,
                    c.Description,
                    c.Properties,
                    c.Tags
                })
                : null
        }, JsonOpts);
    }

    [McpServerTool, Description(
        "Gets pre-built common configurations for a device type. " +
        "Returns ready-to-use property presets like 'one-shot trigger', 'team-only spawner', " +
        "'timed barrier with 10s delay', etc. Each config includes property names and values.")]
    public string get_common_device_configs(
        DeviceEncyclopedia encyclopedia,
        [Description("Device class name (e.g., 'trigger', 'barrier', 'mutator_zone', 'item_granter')")] string deviceClass)
    {
        var configs = encyclopedia.GetCommonConfigurations(deviceClass);

        if (configs.Count == 0)
        {
            // List device types that have configs
            var allDevices = encyclopedia.ListAllDevices();
            var withConfigs = allDevices.Where(d => d.HasCommonConfigs).Select(d => d.DisplayName).ToList();

            return JsonSerializer.Serialize(new
            {
                error = $"No common configurations found for '{deviceClass}'.",
                devicesWithConfigs = withConfigs
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            deviceClass,
            configCount = configs.Count,
            configurations = configs.Select(c => new
            {
                c.Name,
                c.Description,
                c.Properties,
                c.Tags
            })
        }, JsonOpts);
    }
}
