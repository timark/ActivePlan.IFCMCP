using IfcMcpServer.Models;
using Microsoft.Extensions.Logging;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public sealed class IfcService(ILogger<IfcService> logger)
{
    private IfcStore? _model;
    private ModelMetadata? _metadata;

    public ModelMetadata LoadModel(string filePath)
    {
        try
        {
            _model?.Dispose();
            _model = null;
            _metadata = null;

            logger.LogInformation("Loading IFC model from {FilePath}", filePath);
            _model = IfcStore.Open(filePath);

            _metadata = BuildMetadata(filePath, _model);
            logger.LogInformation("Loaded model: schema={Schema}, project={Project}", _metadata.Schema, _metadata.ProjectName);
            return _metadata;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Failed to load IFC model from {FilePath}", filePath);
            throw new InvalidOperationException($"Failed to load IFC model from '{filePath}': {ex.Message}", ex);
        }
    }

    public IfcStore GetModelOrThrow()
    {
        if (_model is null)
            throw new InvalidOperationException("No model loaded — call load_model first");
        return _model;
    }

    public ModelMetadata? GetModelInfo() => _metadata;

    public void CloseModel()
    {
        _model?.Dispose();
        _model = null;
        _metadata = null;
        logger.LogInformation("Model closed");
    }

    private static ModelMetadata BuildMetadata(string filePath, IfcStore model)
    {
        var schema = model.SchemaVersion.ToString();

        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        var projectName = project?.Name?.ToString() ?? string.Empty;

        var site = model.Instances.OfType<IIfcSite>().FirstOrDefault();
        var siteName = site?.Name?.ToString() ?? string.Empty;

        var building = model.Instances.OfType<IIfcBuilding>().FirstOrDefault();
        var buildingName = building?.Name?.ToString() ?? string.Empty;

        var counts = new Dictionary<string, int>();
        foreach (var instance in model.Instances)
        {
            var typeName = instance.GetType().Name;
            counts.TryGetValue(typeName, out var count);
            counts[typeName] = count + 1;
        }

        return new ModelMetadata(filePath, schema, projectName, siteName, buildingName, counts);
    }
}
