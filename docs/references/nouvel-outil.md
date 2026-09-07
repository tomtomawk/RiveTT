# Nouvel outil

## Files

- `src/RiveTT.Tools/<Category>/<ToolName>Tool.cs`: `IRiveTTTool` implementation.
- `src/RiveTT.Server/Tools/<Category>Tools.cs`: typed MCP wrapper.
- `src/RiveTT.Tests`: unit, contract, or source tests.
- `src/resources/documentation/SKILL.md`: the standalone shipped guide. Update
  its relevant section for user-facing behavior; regenerate its embedded inventory
  with tools/audit-tool-surface.py. Do not recreate modular reference files.

## Requirements

- MCP name is `snake_case`; C# class is `PascalCaseTool`.
- Validate input before touching the document.
- Use `[ToolSafety]` with accurate read/write and destructive metadata.
- Execute Revit API work through the plugin dispatcher.
- Wrap writes in a transaction and surface failed commits as structured errors.
- Keep `dryRun` preview behavior when relevant.
- Return `RiveTTResult<object>.Ok(...)` or `.Fail(...)`; do not leak exceptions.
- Add the matching `[McpServerTool]` wrapper and tests.

## Verification

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

Supported targets are Revit 2026.5+ and 2027, both .NET 10 / x64.
