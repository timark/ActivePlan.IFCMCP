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
    [Description("Open an IFC file and load it into memory. Returns schema, project name, site name, building name, and element counts. Pass the full path to the .ifc file.")]
    public async Task<ModelMetadata> LoadModel(
        [Description("Full path to the IFC file to load")] string filePath)
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
