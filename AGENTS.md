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

## The two-sided contract

The MCP surface (`src/RevitCortex.Server/Tools`) and the runtime tools
(`src/RevitCortex.Tools`) are separate assemblies that agree only by JSON key
name. Every parameter published by a wrapper MUST be read by the tool it is
forwarded to.

A published parameter nobody reads is worse than a missing one: the caller
believes it took effect. `create_sheet` published `titleBlockId` while the
runtime read `titleBlockTypeId`, so every sheet came out as a bare 210×297 mm
sheet with no frame, silently, and no presentation sheet could be produced at
all. `ServerRuntimeParameterContractTests` now fails the build on that class of
mismatch — do not add a waiver to it without a reason in the table.

When you add or rename a parameter:

- add it on both sides in the same change, or accept the old name as an alias
  in the runtime tool;
- state the unit and the convention in the `[Description]` (mm, degrees,
  absolute vs relative elevation);
- make the response report what was actually applied, not what was requested.

## The write lock

Every Revit session starts read-only. `CortexRouter.Route` refuses any tool whose
`toolReadOnly` classification is false with `PermissionDenied`, before the cache
and before the open-document check, until a human presses *Écriture* in the
**MCPRVTT27** ribbon panel (Add-Ins tab).

Consequences for anything you add here:

- `[ToolSafety(readOnly, destructive)]` is now a permission boundary, not just
  metadata. A write tool marked `readOnly: true` would slip through the lock;
  the registration already traces a mismatch against the name-prefix heuristic,
  so do not silence that trace.
- Never read the lock from a tool, and never write to it: `WriteAccessPolicy.Set`
  belongs to the ribbon. `WriteAccessGateTests` scans the whole of
  `RevitCortex.Tools` and fails if a tool calls it.
- `dryRun` is not an exemption. A preview is a tool's own promise; the lock
  cannot depend on 250 implementations keeping it.
- The lock is session state, not document state: `CortexSession.Reinitialize`
  must leave it alone.

## Development rules

- Prefer a dedicated `ICortexTool`; keep `send_code_to_revit` as a fallback.
- Revit API calls must execute through `ExternalEvent`. That context is a valid
  API context and is less restricted than an API *event* handler: switching the
  active document (`OpenAndActivateDocument`) and opening API edit scopes
  (`StairsEditScope`) are supported here, and both were wrongly documented as
  impossible. Before declaring something impossible, check whether the
  restriction applies to API event handlers (Idling, DocumentChanged) or to a
  modal editor — those are different constraints.
- An API edit scope must be started with no transaction open, committed with a
  failure preprocessor (an unhandled warning opens a modal dialog and freezes
  the pipe), and cancelled in a `finally` so Revit never stays in edit mode.
- Writes use a `Transaction`, return a structured `CortexResult`, and must
  not leak exceptions across the router.
- Keep `dryRun` preview behavior where a tool supports it. MCPRVTT27 does not
  display confirmation or licensing dialogs.
- Keep the Roslyn sandbox and local audit log intact.
- Use language-independent IDs and `BuiltInCategory` values when possible.
- Resolve free-text parameter names through `ParameterNameResolver` and
  category names through `CategoryResolver`. Never compare a caller's string to
  a localized `Definition.Name` alone: on a French document that silently
  matches nothing.
- Never return an empty value where a name failed to resolve. Report it
  (`unresolvedParameterNames`, `notFoundIds`, `skippedFields[].reason`) with
  suggestions. A silent empty column reads as real data.
- Numeric outputs carry their unit. Revit stores feet, ft² and ft³ whatever the
  project units are; use `ParameterValueFormatter` so a caller cannot mistake
  one for the other.
- Report the localized category name AND `categoryBic` (the `OST_*` code).
  French Revit names the viewport category "Fenêtres " — same label as windows,
  with a trailing space.
- `ToolResponseShaper` must never shape a failure payload, never drop a list
  item, and never recompute a counter from a trimmed list.
- Add or update xUnit coverage for every behavior change.

## Verification

    dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c Release
    dotnet build .\RevitCortex.sln -c Release
    .\build.ps1

The last command prepares the ignored distribution binaries and the generated
addin manifest. Installation is per-user through `distribution/install.ps1`.

Behavior that only a live Revit session can prove (geometry, transactions,
Revit error messages) must still be re-tested manually against a real 2027
model, and the outcome logged in `docs/MCP_AGENT_IMPROVEMENTS.md`.
