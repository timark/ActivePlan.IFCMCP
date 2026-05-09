using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class PropertyTools(IfcService ifc, ILogger<PropertyTools> logger)
{
    private static readonly HashSet<string> CobiePsetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pset_ManufacturerTypeInformation",
        "Pset_Warranty",
        "Pset_MaintenanceStrategy",
        "COBie_Component",
        "COBie_Type",
        "COBie_Space",
        "COBie_Zone",
        "COBie_System",
        "COBie_Spare",
        "COBie_Job",
        "COBie_Resource",
        "COBie_Contact",
        "COBie_Document",
        "COBie_Attribute",
        "COBie_Coordinate",
    };

    [McpServerTool(Name = "get_property_sets")]
    [Description("Return all property sets for an element as { PsetName: { PropertyName: Value } }. Pass the element's GlobalId.")]
    public async Task<Dictionary<string, Dictionary<string, string>>> GetPropertySets(
        [Description("GlobalId of the element")] string elementGuid)
    {
        logger.LogDebug("get_property_sets: {Guid}", elementGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var element = model.Instances.OfType<IIfcObjectDefinition>()
                              .FirstOrDefault(e => e.GlobalId.ToString() == elementGuid)
                          ?? throw new InvalidOperationException($"Element '{elementGuid}' not found");
            return ElementTools.ReadPropertySets(model, element, logger);
        });
    }

    [McpServerTool(Name = "get_property")]
    [Description("Return the value of a single property on an element. Pass the element GlobalId, property set name, and property name.")]
    public async Task<string?> GetProperty(
        [Description("GlobalId of the element")] string elementGuid,
        [Description("Property set name, e.g. 'Pset_DoorCommon'")] string psetName,
        [Description("Property name, e.g. 'FireRating'")] string propertyName)
    {
        logger.LogDebug("get_property: {Guid} / {Pset} / {Prop}", elementGuid, psetName, propertyName);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var element = model.Instances.OfType<IIfcObjectDefinition>()
                              .FirstOrDefault(e => e.GlobalId.ToString() == elementGuid)
                          ?? throw new InvalidOperationException($"Element '{elementGuid}' not found");
            var psets = ElementTools.ReadPropertySets(model, element, logger);
            if (psets.TryGetValue(psetName, out var props) &&
                props.TryGetValue(propertyName, out var value))
                return value;
            return null;
        });
    }

    [McpServerTool(Name = "get_cobie_data")]
    [Description("Return only COBie-related property sets for an element (Pset_ManufacturerTypeInformation, COBie_Component, COBie_Type, etc.).")]
    public async Task<Dictionary<string, Dictionary<string, string>>> GetCobieData(
        [Description("GlobalId of the element")] string elementGuid)
    {
        logger.LogDebug("get_cobie_data: {Guid}", elementGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var element = model.Instances.OfType<IIfcObjectDefinition>()
                              .FirstOrDefault(e => e.GlobalId.ToString() == elementGuid)
                          ?? throw new InvalidOperationException($"Element '{elementGuid}' not found");
            var all = ElementTools.ReadPropertySets(model, element, logger);
            return all
                .Where(kvp => CobiePsetNames.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        });
    }

    [McpServerTool(Name = "find_elements_by_property")]
    [Description("Find all elements where a specific property matches a value. WARNING: This scans every element in the model and is slow on large files.")]
    public async Task<List<ElementSummary>> FindElementsByProperty(
        [Description("Property set name to look in, e.g. 'Pset_DoorCommon'")] string psetName,
        [Description("Property name to match, e.g. 'FireRating'")] string propertyName,
        [Description("Value to match (exact, case-insensitive)")] string value)
    {
        logger.LogDebug("find_elements_by_property: {Pset}/{Prop}={Value}", psetName, propertyName, value);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var results = new List<ElementSummary>();

            foreach (var rel in model.Instances.OfType<IIfcRelDefinesByProperties>())
            {
                if (rel.RelatingPropertyDefinition is not IIfcPropertySet pset) continue;
                if (!string.Equals(pset.Name?.ToString(), psetName, StringComparison.OrdinalIgnoreCase)) continue;

                var match = pset.HasProperties
                    .OfType<IIfcPropertySingleValue>()
                    .Any(p => string.Equals(p.Name.ToString(), propertyName, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(p.NominalValue?.Value?.ToString(), value, StringComparison.OrdinalIgnoreCase));

                if (!match) continue;

                foreach (var obj in rel.RelatedObjects.OfType<IIfcElement>())
                {
                    results.Add(new ElementSummary(
                        obj.GlobalId.ToString(),
                        obj.Name?.ToString() ?? string.Empty,
                        obj.GetType().Name,
                        null,
                        null));
                }
            }

            return results;
        });
    }
}
