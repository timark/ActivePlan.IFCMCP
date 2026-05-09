namespace IfcMcpServer.Models;

public record ModelMetadata(
    string FilePath,
    string Schema,
    string ProjectName,
    string SiteName,
    string BuildingName,
    Dictionary<string, int> ElementCounts);

public record StoreyInfo(
    string Guid,
    string Name,
    double? Elevation);

public record SpaceInfo(
    string Guid,
    string Name,
    string? LongName,
    string ContainingStorey);

public record ElementSummary(
    string Guid,
    string Name,
    string IfcType,
    string? ContainingStorey,
    string? ContainingSpace);

public record ElementDetail(
    string Guid,
    string Name,
    string IfcType,
    string? Description,
    Dictionary<string, Dictionary<string, string>> PropertySets);

public record TypeInfo(
    string Guid,
    string Name,
    string? Description,
    string IfcType);

public record SpatialHierarchy(
    string SiteGuid,
    string SiteName,
    List<BuildingNode> Buildings);

public record BuildingNode(
    string Guid,
    string Name,
    List<StoreyNode> Storeys);

public record StoreyNode(
    string Guid,
    string Name,
    double? Elevation,
    List<SpaceNode> Spaces);

public record SpaceNode(
    string Guid,
    string Name,
    string? LongName);

public record CreatedElement(string Guid, string Name, string IfcType);

public record CreatedSpatialElement(string Guid, string Name);

public record TransactionResult(string Status);

public record SaveResult(string FilePath);
