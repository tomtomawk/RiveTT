# Nouvel outil

## Files

- `src/RiveTT.Tools/<Category>/<ToolName>Tool.cs`: `ICortexTool` implementation.
- `src/RiveTT.Server/Tools/<Category>Tools.cs`: typed MCP wrapper.
- `src/RiveTT.Tests`: unit, contract, or source tests.
- `src/resources/documentation/README.md` (the shipped guide) and the relevant
  reference under `src/resources/documentation/references/` when the behavior is
  user-facing. Both ship with the product, so a stale sentence there reaches a
  workstation.

## Requirements

- MCP name is `snake_case`; C# class is `PascalCaseTool`.
- Validate input before touching the document.
- Use `[ToolSafety]` with accurate read/write and destructive metadata.
- Execute Revit API work through the plugin dispatcher.
- Wrap writes in a transaction and surface failed commits as structured errors.
- Keep `dryRun` preview behavior when relevant.
- Return `CortexResult<object>.Ok(...)` or `.Fail(...)`; do not leak exceptions.
- Add the matching `[McpServerTool]` wrapper and tests.

## Verification

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

Supported targets are Revit 2026.5+ and 2027, both .NET 10 / x64.
