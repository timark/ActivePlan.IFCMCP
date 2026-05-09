using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class TypeTools(IfcService ifc, ILogger<TypeTools> logger)
{
    [McpServerTool(Name = "get_types")]
    [Description("Return all IfcTypeProduct instances (type objects) in the model with GUID, name, description, and IFC type.")]
    public async Task<List<TypeInfo>> GetTypes()
    {
        logger.LogDebug("get_types");
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            return model.Instances.OfType<IIfcTypeProduct>()
                .Select(t => new TypeInfo(
                    t.GlobalId.ToString(),
                    t.Name?.ToString() ?? string.Empty,
                    t.Description?.ToString(),
                    t.GetType().Name))
                .ToList();
        });
    }

    [McpServerTool(Name = "get_type_components")]
    [Description("Return all element instances of a given type object (via IfcRelDefinesByType). Pass the type object's GlobalId.")]
    public async Task<List<ElementSummary>> GetTypeComponents(
        [Description("GlobalId of the IfcTypeProduct")] string typeGuid)
    {
        logger.LogDebug("get_type_components: {TypeGuid}", typeGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            return model.Instances.OfType<IIfcRelDefinesByType>()
                .Where(r => r.RelatingType.GlobalId.ToString() == typeGuid)
                .SelectMany(r => r.RelatedObjects)
                .OfType<IIfcElement>()
                .Select(e => new ElementSummary(
                    e.GlobalId.ToString(),
                    e.Name?.ToString() ?? string.Empty,
                    e.GetType().Name,
                    null,
                    null))
                .ToList();
        });
    }

    [McpServerTool(Name = "get_type_properties")]
    [Description("Return all property sets on a type object as { PsetName: { PropertyName: Value } }. Pass the type's GlobalId.")]
    public async Task<Dictionary<string, Dictionary<string, string>>> GetTypeProperties(
        [Description("GlobalId of the IfcTypeProduct")] string typeGuid)
    {
        logger.LogDebug("get_type_properties: {TypeGuid}", typeGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            var typeObj = model.Instances.OfType<IIfcTypeObject>()
                              .FirstOrDefault(t => t.GlobalId.ToString() == typeGuid)
                          ?? throw new InvalidOperationException($"Type object '{typeGuid}' not found");

            var result = new Dictionary<string, Dictionary<string, string>>();
            try
            {
                if (typeObj.HasPropertySets is null) return result;

                foreach (var pset in typeObj.HasPropertySets.OfType<IIfcPropertySet>())
                {
                    var psetName = pset.Name?.ToString() ?? "Unknown";
                    var props = new Dictionary<string, string>();

                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        try
                        {
                            props[prop.Name.ToString()] = prop.NominalValue?.Value?.ToString() ?? string.Empty;
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
                logger.LogWarning(ex, "Error reading property sets for type {Guid}", typeGuid);
            }

            return result;
        });
    }
}
