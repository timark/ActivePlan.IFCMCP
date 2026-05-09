using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.SharedBldgElements;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class WriteTools(IfcService ifc, ILogger<WriteTools> logger)
{
    [McpServerTool(Name = "create_model")]
    [Description("Create a new empty IFC4 model with Project > Site > Building scaffolding. Disposes any previously loaded model. Does not require an active transaction.")]
    public async Task<ModelMetadata> CreateModel(
        [Description("Name for the IfcProject")] string projectName,
        [Description("Name for the IfcSite")] string siteName = "Default Site",
        [Description("Name for the IfcBuilding")] string buildingName = "Default Building")
    {
        logger.LogDebug("create_model: {Project}", projectName);
        return await Task.Run(() => ifc.CreateModel(projectName, siteName, buildingName));
    }

    [McpServerTool(Name = "save_model")]
    [Description("Save the model to the file path it was loaded from. Fails on newly created models — use save_model_as instead.")]
    public async Task<SaveResult> SaveModel()
    {
        logger.LogDebug("save_model");
        return await Task.Run(() => new SaveResult(ifc.SaveModel()));
    }

    [McpServerTool(Name = "save_model_as")]
    [Description("Save the model to a new file path. Updates the stored path so subsequent save_model calls use it.")]
    public async Task<SaveResult> SaveModelAs(
        [Description("Full path for the output .ifc file")] string filePath)
    {
        logger.LogDebug("save_model_as: {FilePath}", filePath);
        return await Task.Run(() => new SaveResult(ifc.SaveModelAs(filePath)));
    }

    [McpServerTool(Name = "create_element")]
    [Description("Create a new IFC element by class name (e.g. 'IfcWall', 'IfcDoor') and place it in a storey. Requires an active transaction.")]
    public async Task<CreatedElement> CreateElement(
        [Description("IFC class name, e.g. 'IfcWall', 'IfcDoor', 'IfcWindow'")] string ifcType,
        [Description("Name for the new element")] string name,
        [Description("GlobalId of the IfcBuildingStorey to place the element in")] string storeyGuid)
    {
        logger.LogDebug("create_element: {IfcType} {Name} in {Storey}", ifcType, name, storeyGuid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelWithTransactionOrThrow();

            var storey = model.Instances.OfType<IIfcBuildingStorey>()
                             .FirstOrDefault(s => s.GlobalId.ToString() == storeyGuid)
                         ?? throw new InvalidOperationException($"Storey '{storeyGuid}' not found");

            var normalizedType = ifcType.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase) ? ifcType : "Ifc" + ifcType;

            var ifcAssembly = typeof(IfcWall).Assembly;
            var entityType = ifcAssembly.GetTypes()
                .FirstOrDefault(t => string.Equals(t.Name, normalizedType, StringComparison.OrdinalIgnoreCase)
                                     && typeof(IIfcElement).IsAssignableFrom(t)
                                     && !t.IsAbstract)
                ?? throw new InvalidOperationException(
                    $"Unknown or abstract IFC element type '{ifcType}'. Use concrete types like IfcWall, IfcDoor, IfcWindow, IfcSlab, etc.");

            var element = (IIfcElement)model.Instances.New(entityType);
            element.Name = name;
            var concreteElement = (IfcProduct)element;

            var rel = model.Instances.OfType<IIfcRelContainedInSpatialStructure>()
                          .FirstOrDefault(r => r.RelatingStructure == storey);
            if (rel is not null)
            {
                rel.RelatedElements.Add(concreteElement);
            }
            else
            {
                model.Instances.New<IfcRelContainedInSpatialStructure>(r =>
                {
                    r.RelatingStructure = (IfcBuildingStorey)storey;
                    r.RelatedElements.Add(concreteElement);
                });
            }

            return new CreatedElement(
                element.GlobalId.ToString(),
                element.Name?.ToString() ?? string.Empty,
                element.GetType().Name);
        });
    }

    [McpServerTool(Name = "delete_element")]
    [Description("Delete an element by GlobalId. Removes the element and cleans up spatial containment relationships. Requires an active transaction.")]
    public async Task<string> DeleteElement(
        [Description("GlobalId of the element to delete")] string guid)
    {
        logger.LogDebug("delete_element: {Guid}", guid);
        return await Task.Run(() =>
        {
            var model = ifc.GetModelWithTransactionOrThrow();

            var element = model.Instances.OfType<IIfcElement>()
                              .FirstOrDefault(e => e.GlobalId.ToString() == guid)
                          ?? throw new InvalidOperationException($"Element '{guid}' not found");

            foreach (var rel in model.Instances.OfType<IIfcRelContainedInSpatialStructure>().ToList())
                rel.RelatedElements.Remove(element);

            foreach (var rel in model.Instances.OfType<IIfcRelDefinesByProperties>().ToList())
                rel.RelatedObjects.Remove(element);

            foreach (var rel in model.Instances.OfType<IIfcRelDefinesByType>().ToList())
                rel.RelatedObjects.Remove(element);

            model.Delete(element);
            return $"Element '{guid}' deleted.";
        });
    }
}
