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
    private string? _sourcePath;
    private string? _cachePath;

    private static readonly HashSet<string> CacheEligibleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ifc", ".ifcxml", ".ifczip" };

    internal static bool IsCacheEligible(string filePath)
        => CacheEligibleExtensions.Contains(Path.GetExtension(filePath));

    internal static StorageType GetStorageType(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ifc" => StorageType.Ifc,
            ".ifcxml" => StorageType.IfcXml,
            ".ifczip" => StorageType.IfcZip,
            ".xbim" => StorageType.Xbim,
            _ => throw new InvalidOperationException(
                $"Unsupported file extension '{Path.GetExtension(filePath)}'. Use .ifc, .ifcxml, .ifczip, or .xbim.")
        };

    public bool HasActiveTransaction => _transaction is not null;

    public ModelMetadata LoadModel(string filePath)
    {
        try
        {
            _model?.Dispose();
            _model = null;
            _metadata = null;
            _sourcePath = null;
            _cachePath = null;

            logger.LogInformation("Loading IFC model from {FilePath}", filePath);

            string loadedFrom;

            if (IsCacheEligible(filePath))
            {
                var xbimPath = Path.ChangeExtension(filePath, ".xbim");

                if (File.Exists(xbimPath)
                    && File.GetLastWriteTimeUtc(xbimPath) >= File.GetLastWriteTimeUtc(filePath))
                {
                    logger.LogInformation("Loading cached .xbim for {FilePath}", filePath);
                    _model = IfcStore.Open(xbimPath);
                    loadedFrom = "cache";
                }
                else
                {
                    _model = IfcStore.Open(filePath);
                    loadedFrom = "parsed";

                    try
                    {
                        _model.SaveAs(xbimPath, StorageType.Xbim);
                        logger.LogInformation("Created .xbim cache at {XbimPath}", xbimPath);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to create .xbim cache at {XbimPath} — continuing without cache", xbimPath);
                        xbimPath = null;
                    }
                }

                _sourcePath = filePath;
                _cachePath = xbimPath;
            }
            else
            {
                _model = IfcStore.Open(filePath);
                _sourcePath = null;
                _cachePath = null;
                loadedFrom = "direct";
            }

            _metadata = BuildMetadata(
                _sourcePath ?? filePath,
                _cachePath,
                loadedFrom,
                _model);

            logger.LogInformation("Loaded model: schema={Schema}, project={Project}, loadedFrom={LoadedFrom}",
                _metadata.Schema, _metadata.ProjectName, _metadata.LoadedFrom);
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
        _sourcePath = null;
        _cachePath = null;
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

        _sourcePath = null;
        _cachePath = null;
        _metadata = BuildMetadata(string.Empty, null, "created", _model);
        logger.LogInformation("Created new IFC4 model: project={Project}", projectName);
        return _metadata;
    }

    public string SaveModel()
    {
        var model = GetModelOrThrow();

        if (_sourcePath is not null)
        {
            model.SaveAs(_sourcePath, GetStorageType(_sourcePath));
            logger.LogInformation("Model saved to {FilePath}", _sourcePath);

            if (_cachePath is not null)
            {
                try
                {
                    model.SaveAs(_cachePath, StorageType.Xbim);
                    logger.LogInformation("Cache updated at {CachePath}", _cachePath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to update .xbim cache at {CachePath}", _cachePath);
                }
            }

            return _sourcePath;
        }

        if (_cachePath is not null)
        {
            model.SaveAs(_cachePath, StorageType.Xbim);
            logger.LogInformation("Model saved to {CachePath}", _cachePath);
            return _cachePath;
        }

        if (_metadata is not null && !string.IsNullOrEmpty(_metadata.FilePath))
        {
            model.SaveAs(_metadata.FilePath);
            logger.LogInformation("Model saved to {FilePath}", _metadata.FilePath);
            return _metadata.FilePath;
        }

        throw new InvalidOperationException("No file path — use save_model_as to specify a path");
    }

    public string SaveModelAs(string filePath)
    {
        var model = GetModelOrThrow();
        var storageType = GetStorageType(filePath);

        model.SaveAs(filePath, storageType);
        logger.LogInformation("Model saved to {FilePath}", filePath);

        if (storageType == StorageType.Xbim)
        {
            _sourcePath = null;
            _cachePath = filePath;
        }
        else
        {
            _sourcePath = filePath;
            var xbimPath = Path.ChangeExtension(filePath, ".xbim");
            try
            {
                model.SaveAs(xbimPath, StorageType.Xbim);
                _cachePath = xbimPath;
                logger.LogInformation("Cache updated at {CachePath}", xbimPath);
            }
            catch (Exception ex)
            {
                _cachePath = null;
                logger.LogWarning(ex, "Failed to update .xbim cache at {CachePath}", xbimPath);
            }
        }

        _metadata = BuildMetadata(
            _sourcePath ?? filePath,
            _cachePath,
            _metadata?.LoadedFrom ?? "parsed",
            model);

        return filePath;
    }

    private static ModelMetadata BuildMetadata(string filePath, string? cachePath, string loadedFrom, IfcStore model)
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

        return new ModelMetadata(filePath, cachePath, loadedFrom, schema, projectName, siteName, buildingName, counts);
    }
}
