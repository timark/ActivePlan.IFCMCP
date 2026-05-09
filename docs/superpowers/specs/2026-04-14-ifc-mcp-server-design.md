# IfcMcpServer — Design Spec
_Date: 2026-04-14_

## Purpose

A .NET 10 MCP server that exposes IFC model files to Claude (or any MCP client) via the xBIM toolkit. Clients can load an IFC file, navigate its spatial hierarchy, query elements by type or GUID, read property sets, extract COBie data, and inspect type objects — all through MCP tools over stdio.

---

## Project structure

```
IfcMcpServer/
├── IfcMcpServer.sln
├── IfcMcpServer.csproj         net10.0, Exe
├── CLAUDE.md
├── Program.cs                  host setup + optional CLI file-load
├── Services/
│   └── IfcService.cs           singleton wrapping IfcStore?
├── Tools/
│   ├── ModelTools.cs           load_model, get_model_info, close_model
│   ├── SpatialTools.cs         spatial hierarchy + storeys + spaces
│   ├── ElementTools.cs         element queries by type/guid/space/name
│   ├── PropertyTools.cs        property set read + COBie + find-by-value
│   └── TypeTools.cs            IfcTypeProduct listing + components + psets
└── Models/
    └── IfcModels.cs            C# records for all tool return types
```

---

## Target framework and packages

| Package | Purpose |
|---------|---------|
| `net10.0` | Matches all existing MCP servers in this repo |
| `ModelContextProtocol 1.0.0` | Official C# MCP SDK |
| `Xbim.Essentials` | IFC file parsing, LINQ queries, property sets |
| `Xbim.Geometry` | Native geometry kernel (Open CASCADE) — included for future geometry tools |
| `Microsoft.Extensions.Hosting` | IHost, DI container |
| `Microsoft.Extensions.Logging` | ILogger injection |

---

## Architecture

### Program.cs

Bootstraps the host using `Host.CreateApplicationBuilder(args)`. Redirects all logging to stderr (`LogToStandardErrorThreshold = LogLevel.Trace`). Registers `IfcService` as a singleton. Wires MCP: `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.

After `builder.Build()`, before `host.RunAsync()`: if `args[0]` is present, resolves `IfcService` from the container and calls `LoadModel(args[0])` to pre-load the file before the MCP handshake.

### IfcService (Services/IfcService.cs)

Singleton. Holds `IfcStore? _model`. Public surface:

```csharp
public sealed class IfcService(ILogger<IfcService> logger)
{
    public ModelMetadata LoadModel(string filePath)   // opens/replaces _model, returns metadata
    public IfcStore GetModelOrThrow()                  // returns _model or throws InvalidOperationException
    public ModelMetadata? GetModelInfo()               // returns metadata if loaded, null if not
    public void CloseModel()                           // disposes _model, sets null
}
```

`LoadModel` disposes any existing model before opening the new one. Catches all xBIM exceptions; logs at `Error` and rethrows as `InvalidOperationException` with the file path and original message included.

`GetModelOrThrow` is called at the top of every tool method. Returns the store or throws `InvalidOperationException("No model loaded — call load_model first")`.

### Tool classes (Tools/)

All five tool classes follow the PIMMcpServer pattern:

```csharp
[McpServerToolType]
public sealed class XxxTools(IfcService ifc, ILogger<XxxTools> logger)
```

All tool methods are `async Task<T>`, wrapping synchronous xBIM calls in `Task.Run` to avoid blocking the host thread.

### Models (Models/IfcModels.cs)

All return types are C# `record` types — flat, JSON-serialisable, no xBIM types exposed.

```csharp
record ModelMetadata(string FilePath, string Schema, string ProjectName,
                     string SiteName, string BuildingName, Dictionary<string, int> ElementCounts);
record StoreyInfo(string Guid, string Name, double? Elevation);
record SpaceInfo(string Guid, string Name, string? LongName, string ContainingStorey);
record ElementSummary(string Guid, string Name, string IfcType,
                      string? ContainingStorey, string? ContainingSpace);
record ElementDetail(string Guid, string Name, string IfcType, string? Description,
                     Dictionary<string, Dictionary<string, string>> PropertySets);
record TypeInfo(string Guid, string Name, string? Description, string IfcType);
record SpatialHierarchy(string SiteGuid, string SiteName, List<BuildingNode> Buildings);
record BuildingNode(string Guid, string Name, List<StoreyNode> Storeys);
record StoreyNode(string Guid, string Name, double? Elevation, List<SpaceNode> Spaces);
record SpaceNode(string Guid, string Name, string? LongName);
```

---

## Tools

### ModelTools.cs

| Tool | Signature | Description |
|------|-----------|-------------|
| `load_model` | `(string filePath)` → `ModelMetadata` | Opens IFC file, caches in IfcService, returns schema/project/element counts |
| `get_model_info` | `()` → `ModelMetadata?` | Returns metadata for currently loaded model |
| `close_model` | `()` → `string` | Disposes current IfcStore, returns confirmation message |

### SpatialTools.cs

| Tool | Signature | Description |
|------|-----------|-------------|
| `get_spatial_hierarchy` | `()` → `SpatialHierarchy` | Full tree: Site > Building > Storey > Space with GUIDs and names |
| `get_storeys` | `()` → `List<StoreyInfo>` | All IfcBuildingStorey elements with GUID, name, elevation |
| `get_spaces` | `(string? storeyName = null)` → `List<SpaceInfo>` | All IfcSpace elements, optionally filtered by storey name |

### ElementTools.cs

| Tool | Signature | Description |
|------|-----------|-------------|
| `get_elements_by_type` | `(string ifcType)` → `List<ElementSummary>` | Query by IFC class name e.g. "IfcDoor", "IfcPump" — case-insensitive |
| `get_element_by_guid` | `(string guid)` → `ElementDetail?` | Full details + all property sets for one element |
| `get_elements_in_space` | `(string spaceGuid)` → `List<ElementSummary>` | All elements contained within a given space |
| `search_elements` | `(string nameContains)` → `List<ElementSummary>` | Case-insensitive text search across element names |

### PropertyTools.cs

| Tool | Signature | Description |
|------|-----------|-------------|
| `get_property_sets` | `(string elementGuid)` → `Dictionary<string, Dictionary<string, string>>` | All Psets as Pset name → property name → string value |
| `get_property` | `(string elementGuid, string psetName, string propertyName)` → `string?` | Single property value |
| `get_cobie_data` | `(string elementGuid)` → `Dictionary<string, Dictionary<string, string>>` | COBie Psets only (Pset_ManufacturerTypeInformation, COBie_Component, COBie_Type, etc.) |
| `find_elements_by_property` | `(string psetName, string propertyName, string value)` → `List<ElementSummary>` | Find elements where a property matches a value. **Slow on large models** — tool description warns the user. |

### TypeTools.cs

| Tool | Signature | Description |
|------|-----------|-------------|
| `get_types` | `()` → `List<TypeInfo>` | All IfcTypeProduct instances with GUID, name, description |
| `get_type_components` | `(string typeGuid)` → `List<ElementSummary>` | All IfcElement instances of a given type (via IfcRelDefinesByType) |
| `get_type_properties` | `(string typeGuid)` → `Dictionary<string, Dictionary<string, string>>` | Property sets on the type object |

---

## Error handling

| Scenario | Behaviour |
|----------|-----------|
| Tool called before `load_model` | `GetModelOrThrow()` throws `InvalidOperationException("No model loaded — call load_model first")` |
| `load_model` on a bad/missing file | Logged at `Error`, rethrown as `InvalidOperationException` with file path + original message |
| GUID not found | Return `null` (for single-item lookups) or empty list |
| Unexpected null on element property | Log at `Warning`, skip element / return empty string |
| xBIM geometry exception | Log at `Warning`, skip — never aborts a property or hierarchy query |
| xBIM query on malformed IFC data | Caught per-tool, logged at `Warning`, returns partial results |

---

## xBIM query patterns reference

```csharp
// Open a file
using var model = IfcStore.Open(filePath);

// Query all instances of a type
var spaces = model.Instances.OfType<IfcSpace>();

// Look up by GlobalId
var element = model.Instances.OfGlobalId(guid);

// Spatial containment
var containment = model.Instances.OfType<IfcRelContainedInSpatialStructure>()
    .Where(r => r.RelatingStructure.GlobalId == storeyGuid);

// Property sets on an element
var psetRels = model.Instances.OfType<IfcRelDefinesByProperties>()
    .Where(r => r.RelatedObjects.Contains(element));

// Type → components
var typeRels = model.Instances.OfType<IfcRelDefinesByType>()
    .Where(r => r.RelatingType.GlobalId == typeGuid);
```

---

## Common IFC type names

**Architectural:** IfcWall, IfcSlab, IfcRoof, IfcDoor, IfcWindow, IfcStair, IfcRailing, IfcColumn, IfcBeam, IfcCurtainWall, IfcCovering, IfcFurniture

**Spatial:** IfcSite, IfcBuilding, IfcBuildingStorey, IfcSpace

**MEP — Mechanical:** IfcAirTerminal, IfcAirTerminalBox, IfcUnitaryEquipment, IfcBoiler, IfcChiller, IfcCoil, IfcCompressor, IfcCondenser, IfcCooledBeam, IfcCoolingTower, IfcEvaporator, IfcFan, IfcHeatExchanger, IfcHumidifier, IfcPump, IfcTank, IfcValve, IfcPipeFitting, IfcPipeSegment, IfcDuctFitting, IfcDuctSegment, IfcFilter

**MEP — Electrical:** IfcElectricAppliance, IfcElectricDistributionBoard, IfcElectricFlowStorageDevice, IfcElectricGenerator, IfcElectricMotor, IfcLamp, IfcLightFixture, IfcProtectiveDevice, IfcSwitchingDevice, IfcTransformer, IfcCableCarrierFitting, IfcCableCarrierSegment, IfcCableSegment

**MEP — Plumbing/Fire:** IfcFireSuppressionTerminal, IfcSanitaryTerminal, IfcWasteTerminal, IfcInterceptor

**General:** IfcBuildingElementProxy, IfcDiscreteAccessory, IfcMechanicalFastener

---

## COBie property set names

```
Pset_ManufacturerTypeInformation
Pset_Warranty
Pset_MaintenanceStrategy
COBie_Component
COBie_Type
COBie_Space
COBie_Zone
COBie_System
COBie_Spare
COBie_Job
COBie_Resource
COBie_Contact
COBie_Document
COBie_Attribute
COBie_Coordinate
```

---

## Adding new tool groups

1. Create `Tools/NewTool.cs` with `[McpServerToolType]` on the class
2. Inject `IfcService ifc` and `ILogger<NewTool> logger` via constructor
3. Add methods decorated with `[McpServerTool]` and `[Description("...")]`
4. Add any new return types to `Models/IfcModels.cs`
5. No registration needed — `WithToolsFromAssembly()` picks it up automatically

---

## Registration commands

```bash
# Development (dotnet run)
claude mcp add ifc-mcp --scope user -- dotnet run --project D:/Users/TimAikin/Documents/GitHub/IfcMcpServer

# Production (published exe)
claude mcp add ifc-mcp --scope user -- D:/Users/TimAikin/Documents/GitHub/IfcMcpServer/publish/IfcMcpServer.exe

# With a default file pre-loaded at startup
claude mcp add ifc-mcp --scope user -- D:/Users/TimAikin/Documents/GitHub/IfcMcpServer/publish/IfcMcpServer.exe "C:/path/to/model.ifc"
```

---

## Sample IFC files for testing

Available at `D:/Users/TimAikin/Documents/GitHub/Sample-Test-Files/`:

```
IFC 2x3/Duplex Apartment/Duplex_A_20110907.ifc          (architectural)
IFC 2x3/Duplex Apartment/Duplex_MEP_20110907.ifc         (MEP)
IFC 2x3/Medical-Dental Clinic/Clinic_Architectural.ifc
IFC 2x3/Medical-Dental Clinic/Clinic_HVAC.ifc
```
