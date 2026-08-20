# MCPRVTT27 — contributor guide

MCPRVTT27 supports Autodesk Revit 2027 only. The runtime is .NET 10 on
Windows x64; do not reintroduce R23–R26 configurations or `net48`/`net8`
compatibility branches.

## Architecture

    MCP client
      -> MCPRVTT27.Server (stdio)
      -> Windows named pipe (current user only)
      -> MCPRVTT27.Plugin (Revit ExternalEvent)
      -> CortexRouter
      -> ICortexTool implementations

The C# server is the only server implementation. Generated distribution
outputs live under `distribution/plugin` and `distribution/server` and are
never committed.

## Development rules

- Prefer a dedicated `ICortexTool`; keep `send_code_to_revit` as a fallback.
- Revit API calls must execute through `ExternalEvent`.
- Writes use a `Transaction`, return a structured `CortexResult`, and must
  not leak exceptions across the router.
- Keep `dryRun` preview behavior where a tool supports it. MCPRVTT27 does not
  display confirmation or licensing dialogs.
- Keep the Roslyn sandbox and local audit log intact.
- Use language-independent IDs and `BuiltInCategory` values when possible.
- Add or update xUnit coverage for every behavior change.

## Verification

    dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c Release
    dotnet build .\RevitCortex.sln -c Release
    .\build.ps1

The last command prepares the ignored distribution binaries and the generated
addin manifest. Installation is per-user through `distribution/install.ps1`.
