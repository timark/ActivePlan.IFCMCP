using IfcMcpServer.Models;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.IO;

namespace IfcMcpServer.Services;

public sealed class IfcService(ILogger<IfcService> logger)
{
    private IfcStore? _model;
    private ModelMetadata? _metadata;
    private ITransaction? _transaction;

    public bool HasActiveTransaction => _transaction is not null;

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

    public void BeginTransaction()
    {
        var model = GetModelOrThrow();
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already active — commit or rollback before starting a new one");
        _transaction = model.BeginTransaction("MCP Write");
        logger.LogInformation("Transaction started");
    }

    public void CommitTransaction()
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction — call begin_transaction first");
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
        logger.LogInformation("Transaction committed");
    }

    public void RollbackTransaction()
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction — call begin_transaction first");
        _transaction.Dispose();
        _transaction = null;
        logger.LogInformation("Transaction rolled back");
    }

    public IfcStore GetModelWithTransactionOrThrow()
    {
        var model = GetModelOrThrow();
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction — call begin_transaction first");
        return model;
    }

    public void CloseModel()
    {
        if (_transaction is not null)
        {
            _transaction.Dispose();
            _transaction = null;
            logger.LogInformation("Active transaction rolled back during close");
        }
        _model?.Dispose();
        _model = null;
        _metadata = null;
        logger.LogInformation("Model closed");
    }

    public ModelMetadata CreateModel(string projectName, string siteName, string buildingName)
    {
        _transaction?.Dispose();
        _transaction = null;
        _model?.Dispose();
        _model = null;
        _metadata = null;

        var editor = new XbimEditorCredentials
        {
            ApplicationDevelopersName = "IfcMcpServer",
            ApplicationFullName = "IfcMcpServer",
            ApplicationIdentifier = "IfcMcpServer",
            ApplicationVersion = "1.0",
            EditorsFamilyName = "MCP",
            EditorsGivenName = "Agent",
            EditorsOrganisationName = "IfcMcpServer"
        };

        _model = IfcStore.Create(editor, XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);

        using (var txn = _model.BeginTransaction("Create scaffold"))
        {
            var project = _model.Instances.New<IfcProject>(p =>
            {
                p.Name = projectName;
                p.UnitsInContext = _model.Instances.New<IfcUnitAssignment>(u =>
                {
                    u.Units.Add(_model.Instances.New<IfcSIUnit>(si =>
                    {
                        si.UnitType = Xbim.Ifc4.Interfaces.IfcUnitEnum.LENGTHUNIT;
                        si.Name = Xbim.Ifc4.Interfaces.IfcSIUnitName.METRE;
                    }));
                });
            });

            var site = _model.Instances.New<IfcSite>(s => s.Name = siteName);
            var building = _model.Instances.New<IfcBuilding>(b => b.Name = buildingName);

            _model.Instances.New<IfcRelAggregates>(r =>
            {
                r.RelatingObject = project;
                r.RelatedObjects.Add(site);
            });

            _model.Instances.New<IfcRelAggregates>(r =>
            {
                r.RelatingObject = site;
                r.RelatedObjects.Add(building);
            });

            txn.Commit();
        }

        _metadata = BuildMetadata(string.Empty, _model);
        logger.LogInformation("Created new IFC4 model: project={Project}", projectName);
        return _metadata;
    }

    public string SaveModel()
    {
        var model = GetModelOrThrow();
        if (_metadata is null || string.IsNullOrEmpty(_metadata.FilePath))
            throw new InvalidOperationException("No file path — use save_model_as to specify a path");
        model.SaveAs(_metadata.FilePath);
        logger.LogInformation("Model saved to {FilePath}", _metadata.FilePath);
        return _metadata.FilePath;
    }

    public string SaveModelAs(string filePath)
    {
        var model = GetModelOrThrow();
        model.SaveAs(filePath);
        _metadata = BuildMetadata(filePath, model);
        logger.LogInformation("Model saved to {FilePath}", filePath);
        return filePath;
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
