using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class ElementTools(IfcService ifc, ILogger<ElementTools> logger)
{
    [McpServerTool(Name = "get_elements_by_type")]
    [Description("Return all elements of a given IFC class name, e.g. 'IfcDoor', 'IfcPump', 'IfcWall'. Case-insensitive.")]
    public async Task<List<ElementSummary>> GetElementsByType(
        [Description("IFC class name, e.g. 'IfcDoor', 'IfcPump', 'IfcWall'")] string ifcType)
    {
        logger.LogDebug("get_elements_by_type: {IfcType}", ifcType);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var context = BuildSpatialContext(model);
            var typeName = NormalizeTypeName(ifcType);

            return model.Instances
                .Where(i => string.Equals(i.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                .OfType<IIfcElement>()
                .Select(e => ToSummary(e, context))
                .ToList();
        });
    }

    [McpServerTool(Name = "get_element_by_guid")]
    [Description("Return full details and all property sets for a single element by its GlobalId (GUID).")]
    public async Task<ElementDetail?> GetElementByGuid(
        [Description("GlobalId (GUID) of the element")] string guid)
    {
        logger.LogDebug("get_element_by_guid: {Guid}", guid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var element = model.Instances.OfType<IIfcElement>()
                .FirstOrDefault(e => e.GlobalId.ToString() == guid);
            if (element is null) return null;

            var psets = ReadPropertySets(model, element, logger);
            return new ElementDetail(
                element.GlobalId.ToString(),
                element.Name?.ToString() ?? string.Empty,
                element.GetType().Name,
                element.Description?.ToString(),
                psets);
        });
    }

    [McpServerTool(Name = "get_elements_in_space")]
    [Description("Return all elements spatially contained within a given space (by space GUID).")]
    public async Task<List<ElementSummary>> GetElementsInSpace(
        [Description("GlobalId of the IfcSpace")] string spaceGuid)
    {
        logger.LogDebug("get_elements_in_space: {SpaceGuid}", spaceGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var context = BuildSpatialContext(model);

            return model.Instances.OfType<IIfcRelContainedInSpatialStructure>()
                .Where(r => r.RelatingStructure.GlobalId.ToString() == spaceGuid)
                .SelectMany(r => r.RelatedElements)
                .OfType<IIfcElement>()
                .Select(e => ToSummary(e, context))
                .ToList();
        });
    }

    [McpServerTool(Name = "search_elements")]
    [Description("Case-insensitive text search across all element names. Returns matching elements with type and location info.")]
    public async Task<List<ElementSummary>> SearchElements(
        [Description("Substring to search for in element names (case-insensitive)")] string nameContains)
    {
        logger.LogDebug("search_elements: {Query}", nameContains);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var context = BuildSpatialContext(model);

            return model.Instances.OfType<IIfcElement>()
                .Where(e => e.Name != null &&
                            e.Name.ToString()!.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                .Select(e => ToSummary(e, context))
                .ToList();
        });
    }

    internal static Dictionary<string, Dictionary<string, string>> ReadPropertySets(
        IModel model, IIfcObjectDefinition element, ILogger logger)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        try
        {
            var psetRels = model.Instances.OfType<IIfcRelDefinesByProperties>()
                .Where(r => r.RelatedObjects.Contains(element));

            foreach (var rel in psetRels)
            {
                if (rel.RelatingPropertyDefinition is not IIfcPropertySet pset) continue;

                var psetName = pset.Name?.ToString() ?? "Unknown";
                var props = new Dictionary<string, string>();

                foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                {
                    try
                    {
                        var val = prop.NominalValue?.Value?.ToString() ?? string.Empty;
                        props[prop.Name.ToString()] = val;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not read property {Prop} in {Pset}", prop.Name, psetName);
                    }
                }

                result[psetName] = props;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error reading property sets for element {Guid}", element.GlobalId);
        }
        return result;
    }

    private static SpatialContext BuildSpatialContext(IModel model)
    {
        var elementToStorey = new Dictionary<string, string>();
        var elementToSpace = new Dictionary<string, string>();

        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            var structureName = rel.RelatingStructure.Name?.ToString() ?? string.Empty;
            var guid = rel.RelatingStructure.GlobalId.ToString();

            foreach (var obj in rel.RelatedElements)
            {
                var objGuid = obj.GlobalId.ToString();
                if (rel.RelatingStructure is IIfcBuildingStorey)
                    elementToStorey.TryAdd(objGuid, structureName);
                else if (rel.RelatingStructure is IIfcSpace)
                    elementToSpace.TryAdd(objGuid, structureName);
            }
        }

        return new SpatialContext(elementToStorey, elementToSpace);
    }

    private static ElementSummary ToSummary(IIfcElement element, SpatialContext context)
    {
        var guid = element.GlobalId.ToString();
        context.ElementToStorey.TryGetValue(guid, out var storey);
        context.ElementToSpace.TryGetValue(guid, out var space);
        return new ElementSummary(
            guid,
            element.Name?.ToString() ?? string.Empty,
            element.GetType().Name,
            storey,
            space);
    }

    private static string NormalizeTypeName(string ifcType)
    {
        // xBIM concrete types are prefixed with "Ifc" and may have implementation suffixes
        // We match on the simple class name
        return ifcType.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase) ? ifcType : "Ifc" + ifcType;
    }

    private record SpatialContext(
        Dictionary<string, string> ElementToStorey,
        Dictionary<string, string> ElementToSpace);
}
