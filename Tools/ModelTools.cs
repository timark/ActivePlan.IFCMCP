using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class ModelTools(IfcService ifc, ILogger<ModelTools> logger)
{
    [McpServerTool(Name = "load_model")]
    [Description("Open an IFC model file and load it into memory. Accepts .ifc, .ifcxml, .ifczip, and .xbim files. Automatically creates and uses a .xbim cache for faster subsequent loads. Returns schema, project name, site name, building name, element counts, and cache status.")]
    public async Task<ModelMetadata> LoadModel(
        [Description("Full path to the IFC model file (.ifc, .ifcxml, .ifczip, or .xbim)")] string filePath)
    {
        logger.LogDebug("load_model: {FilePath}", filePath);
        return await Task.Run(() => ifc.LoadModel(filePath));
    }

    [McpServerTool(Name = "get_model_info")]
    [Description("Return metadata for the currently loaded IFC model (schema, project name, site, building, element counts). Returns null if no model is loaded.")]
    public Task<ModelMetadata?> GetModelInfo()
    {
        return Task.FromResult(ifc.GetModelInfo());
    }

    [McpServerTool(Name = "close_model")]
    [Description("Dispose the currently loaded IFC model and free memory.")]
    public Task<string> CloseModel()
    {
        ifc.CloseModel();
        return Task.FromResult("Model closed successfully.");
    }
}
