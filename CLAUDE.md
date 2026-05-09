# IfcMcpServer

A .NET 10 MCP server that exposes IFC model files to Claude via the xBIM toolkit over stdio.

## Build & Run

```bash
dotnet build
dotnet run -- "path/to/model.ifc"   # optional: pre-load a file at startup
```

## Project Structure

- `Program.cs` — host setup, DI, MCP stdio wiring, optional CLI file pre-load
- `Services/IfcService.cs` — singleton wrapping IfcStore (load/query/close)
- `Tools/` — MCP tool classes (ModelTools, SpatialTools, ElementTools, PropertyTools, TypeTools)
- `Models/IfcModels.cs` — C# record return types (no xBIM types exposed)

## MCP Registration

```bash
# Dev
claude mcp add ifc-mcp --scope user -- dotnet run --project D:/Users/TimAikin/Documents/GitHub/Activeplan.IfcMcp

# Production
claude mcp add ifc-mcp --scope user -- D:/Users/TimAikin/Documents/GitHub/Activeplan.IfcMcp/publish/IfcMcpServer.exe
```

## Adding New Tools

1. Create `Tools/NewTool.cs` with `[McpServerToolType]` on the class
2. Inject `IfcService ifc` and `ILogger<NewTool> logger` via constructor
3. Decorate methods with `[McpServerTool]` and `[Description("...")]`
4. Add return types to `Models/IfcModels.cs` if needed
5. `WithToolsFromAssembly()` picks them up automatically — no registration needed

## Sample Test Files

```
D:/Users/TimAikin/Documents/GitHub/Sample-Test-Files/IFC 2x3/Duplex Apartment/Duplex_A_20110907.ifc
D:/Users/TimAikin/Documents/GitHub/Sample-Test-Files/IFC 2x3/Duplex Apartment/Duplex_MEP_20110907.ifc
D:/Users/TimAikin/Documents/GitHub/Sample-Test-Files/IFC 2x3/Medical-Dental Clinic/Clinic_Architectural.ifc
D:/Users/TimAikin/Documents/GitHub/Sample-Test-Files/IFC 2x3/Medical-Dental Clinic/Clinic_HVAC.ifc
```
