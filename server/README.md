# `server/` — non-canonical (legacy TypeScript runtime)

> **Status: NON-CANONICAL.** The supported MCP server for RevitCortex is the
> **C# server** at [`src/RevitCortex.Server/`](../src/RevitCortex.Server/)
> (stdio transport, `RevitCortex.Server.exe`). All documentation, the installer,
> and the support report point to that server. See the architecture section in
> [`CLAUDE.md`](../CLAUDE.md) and the MCP configuration in
> [`README.md`](../README.md).

## What this folder is

This directory holds the **original TypeScript MCP server runtime**
(`src/**/*.ts`, ~188 files: connection, database, journal layers). It predates
the C# rewrite and is **not the shipped server**. It is not published as an npm
package (`package.json` has no `main`/`bin`, only `build` scripts) and is not
referenced by `deploy.ps1` / `deploy-dev.ps1` / `release.ps1`. Do **not** tell
users to run `node server/…` — the canonical entry point is the C# executable.

## What is STILL active here (do not remove)

- **`generate-tool-schemas-csharp.mjs`** — the current tool-schema generator.
  The release workflow regenerates [`tool-schemas.txt`](../tool-schemas.txt)
  with `node server/generate-tool-schemas-csharp.mjs` (see `README.md` and
  `CLAUDE.md`). This script reads the **C#** tool definitions; the `-csharp`
  suffix is deliberate. It is load-bearing and must be kept working even though
  the TypeScript *runtime* around it is legacy.

## Guidance

- New MCP tool work goes in the C# server (`src/RevitCortex.Server/Tools/`).
- The TS runtime is kept compiling only for historical reference; it is not a
  supported deliverable and should not be represented as one in any user-facing
  or commercial material.
- If/when the schema generator is ported off this folder, the TypeScript
  runtime can be archived or removed outright.
