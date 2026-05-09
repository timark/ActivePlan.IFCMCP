# IfcMcpServer

An MCP (Model Context Protocol) server that lets AI assistants read and write IFC building models. Built with .NET 10 and [xBIM](https://docs.xbim.net/), it exposes IFC file contents over stdio so tools like Claude can explore spatial hierarchies, look up elements, read property sets, create and modify elements, and save changes back to disk.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Quick Start

```bash
# Build
dotnet build

# Run (optionally pre-load a model)
dotnet run -- "path/to/model.ifc"
```

## Registering with Claude Code

```bash
# Development (builds on the fly)
claude mcp add ifc-mcp --scope user -- dotnet run --project /path/to/IfcMcpServer

# Published binary
dotnet publish -c Release -r win-x64
claude mcp add ifc-mcp --scope user -- /path/to/publish/IfcMcpServer.exe
```

Once registered, Claude can call any of the tools below during a conversation.

## Available Tools

### Model Management

| Tool | Description |
|------|-------------|
| `load_model` | Open an IFC file and return schema, project/site/building names, and element counts |
| `get_model_info` | Return metadata for the currently loaded model |
| `close_model` | Dispose the model and free memory |

### Spatial Queries

| Tool | Description |
|------|-------------|
| `get_spatial_hierarchy` | Full Site > Building > Storey > Space tree with GUIDs |
| `get_storeys` | All building storeys with GUID, name, and elevation |
| `get_spaces` | All spaces, optionally filtered by storey name |

### Element Queries

| Tool | Description |
|------|-------------|
| `get_elements_by_type` | All elements of a given IFC class (e.g. `IfcDoor`, `IfcWall`) |
| `get_element_by_guid` | Full details and property sets for a single element |
| `get_elements_in_space` | All elements contained within a space |
| `search_elements` | Case-insensitive name search across all elements |

### Properties

| Tool | Description |
|------|-------------|
| `get_property_sets` | All property sets for an element |
| `get_property` | A single property value by pset name and property name |
| `get_cobie_data` | Only COBie-related property sets for an element |
| `find_elements_by_property` | Find elements where a property matches a value (full model scan) |

### Type Objects

| Tool | Description |
|------|-------------|
| `get_types` | All `IfcTypeProduct` instances in the model |
| `get_type_components` | All element instances of a given type object |
| `get_type_properties` | Property sets defined on a type object |

### Transactions

| Tool | Description |
|------|-------------|
| `begin_transaction` | Start a new transaction (required before any write operation) |
| `commit_transaction` | Commit the active transaction (changes stay in memory) |
| `rollback_transaction` | Discard all changes since begin_transaction |

### Write Operations

| Tool | Description |
|------|-------------|
| `create_model` | Create a new empty IFC4 model with Project/Site/Building scaffolding |
| `create_element` | Add an element by IFC class name and assign to a storey |
| `delete_element` | Remove an element and its relationships by GUID |
| `set_property` | Set a property value (creates the pset if needed) |
| `delete_property` | Remove a property from a pset |
| `create_storey` | Add a building storey with name and elevation |
| `create_space` | Add a space within a storey |

### Persistence

| Tool | Description |
|------|-------------|
| `save_model` | Save to the file path the model was loaded from |
| `save_model_as` | Save to a new file path |

## Project Structure

```
IfcMcpServer/
  Program.cs              # Host setup, DI, MCP stdio wiring
  Services/
    IfcService.cs         # Singleton wrapping IfcStore (load/query/close/create/save + transactions)
  Tools/
    ModelTools.cs         # load_model, get_model_info, close_model
    SpatialTools.cs       # get_spatial_hierarchy, get_storeys, get_spaces
    ElementTools.cs       # get_elements_by_type, get_element_by_guid, search_elements
    PropertyTools.cs      # get_property_sets, get_property, get_cobie_data, find_elements_by_property
    TypeTools.cs          # get_types, get_type_components, get_type_properties
    TransactionTools.cs   # begin_transaction, commit_transaction, rollback_transaction
    WriteTools.cs         # create_model, create/delete element, set/delete property, create storey/space, save
  Models/
    IfcModels.cs          # C# record return types (no xBIM types exposed to clients)
```

## Adding a New Tool

1. Create `Tools/YourTool.cs` with the `[McpServerToolType]` attribute on the class
2. Inject `IfcService` and `ILogger<YourTool>` via the primary constructor
3. Add methods with `[McpServerTool]` and `[Description("...")]`
4. Define any new return types in `Models/IfcModels.cs`

Tools are discovered automatically via `WithToolsFromAssembly()` -- no manual registration needed.

## Dependencies

| Package | Purpose |
|---------|---------|
| [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) | MCP server SDK (stdio transport, tool discovery) |
| [Xbim.Essentials](https://www.nuget.org/packages/Xbim.Essentials) | IFC file parsing and querying (IFC2x3, IFC4, IFC4x3) |
| Microsoft.Extensions.Hosting | Generic host, DI, logging |
