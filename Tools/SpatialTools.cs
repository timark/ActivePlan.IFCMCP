using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class SpatialTools(IfcService ifc, ILogger<SpatialTools> logger)
{
    [McpServerTool(Name = "get_spatial_hierarchy")]
    [Description("Return the full spatial hierarchy: Site > Building > Storey > Space, with GUIDs and names for each level.")]
    public async Task<SpatialHierarchy> GetSpatialHierarchy()
    {
        logger.LogDebug("get_spatial_hierarchy");
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();

            var site = model.Instances.OfType<IIfcSite>().FirstOrDefault();
            var siteGuid = site?.GlobalId.ToString() ?? string.Empty;
            var siteName = site?.Name?.ToString() ?? string.Empty;

            var buildings = model.Instances.OfType<IIfcBuilding>().Select(b =>
            {
                var storeys = model.Instances.OfType<IIfcBuildingStorey>()
                    .Where(s => IsContainedIn(model, s, b))
                    .Select(s =>
                    {
                        var spaces = model.Instances.OfType<IIfcSpace>()
                            .Where(sp => IsContainedIn(model, sp, s))
                            .Select(sp => new SpaceNode(
                                sp.GlobalId.ToString(),
                                sp.Name?.ToString() ?? string.Empty,
                                sp.LongName?.ToString()))
                            .ToList();

                        return new StoreyNode(
                            s.GlobalId.ToString(),
                            s.Name?.ToString() ?? string.Empty,
                            TryGetElevation(s),
                            spaces);
                    }).ToList();

                return new BuildingNode(
                    b.GlobalId.ToString(),
                    b.Name?.ToString() ?? string.Empty,
                    storeys);
            }).ToList();

            return new SpatialHierarchy(siteGuid, siteName, buildings);
        });
    }

    [McpServerTool(Name = "get_storeys")]
    [Description("Return all building storeys with GUID, name, and elevation.")]
    public async Task<List<StoreyInfo>> GetStoreys()
    {
        logger.LogDebug("get_storeys");
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();
            return model.Instances.OfType<IIfcBuildingStorey>()
                .Select(s => new StoreyInfo(
                    s.GlobalId.ToString(),
                    s.Name?.ToString() ?? string.Empty,
                    TryGetElevation(s)))
                .ToList();
        });
    }

    [McpServerTool(Name = "get_spaces")]
    [Description("Return all spaces. Optionally filter by storey name (case-insensitive substring match).")]
    public async Task<List<SpaceInfo>> GetSpaces(
        [Description("Optional storey name to filter by (case-insensitive)")] string? storeyName = null)
    {
        logger.LogDebug("get_spaces: storeyName={StoreyName}", storeyName);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelOrThrow();

            var storeyLookup = BuildStoreyLookup(model);

            var spaces = model.Instances.OfType<IIfcSpace>().AsEnumerable();

            return spaces
                .Select(sp =>
                {
                    var containingStorey = FindContainingStorey(model, sp, storeyLookup);
                    return new SpaceInfo(
                        sp.GlobalId.ToString(),
                        sp.Name?.ToString() ?? string.Empty,
                        sp.LongName?.ToString(),
                        containingStorey);
                })
                .Where(si => storeyName is null ||
                             si.ContainingStorey.Contains(storeyName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        });
    }

    private static bool IsContainedIn(Xbim.Common.IModel model, IIfcSpatialElement child, IIfcSpatialElement parent)
    {
        return model.Instances.OfType<IIfcRelAggregates>()
            .Any(r => r.RelatingObject == parent && r.RelatedObjects.Contains(child));
    }

    private static double? TryGetElevation(IIfcBuildingStorey storey)
    {
        try { return (double?)storey.Elevation; }
        catch { return null; }
    }

    private static Dictionary<string, string> BuildStoreyLookup(Xbim.Common.IModel model)
    {
        var result = new Dictionary<string, string>();
        foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>())
        {
            if (rel.RelatingStructure is IIfcBuildingStorey storey)
            {
                var storeyName = storey.Name?.ToString() ?? string.Empty;
                foreach (var obj in rel.RelatedElements)
                    result.TryAdd(obj.GlobalId.ToString(), storeyName);
            }
        }
        return result;
    }

    private static string FindContainingStorey(
        Xbim.Common.IModel model,
        IIfcSpace space,
        Dictionary<string, string> storeyLookup)
    {
        // Check direct containment
        if (storeyLookup.TryGetValue(space.GlobalId.ToString(), out var name))
            return name;

        // Check via aggregation (space contained in storey via IfcRelAggregates)
        foreach (var rel in model.Instances.OfType<IIfcRelAggregates>())
        {
            if (rel.RelatingObject is IIfcBuildingStorey storey &&
                rel.RelatedObjects.Contains(space))
                return storey.Name?.ToString() ?? string.Empty;
        }

        return string.Empty;
    }
}
