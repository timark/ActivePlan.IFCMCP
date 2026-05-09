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
}
