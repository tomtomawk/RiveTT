# Checklist de build et de release

## Pre-commit

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

Confirm that the complete test tree is compiled; do not disable default
`Compile` items to obtain a green build.

## Package

    .\builder\build.ps1

Builds every supported Revit target and compiles the installer. Two generated
trees, both ignored, and the split is the rule to remember:

    builder/staging/2026/plugin/   add-in built against Revit 2026.5
    builder/staging/2027/plugin/   add-in built against Revit 2027
    builder/staging/server/        RiveTT.Server.exe, self-contained, shared
    builder/staging/RiveTT.addin   manifest, identical for both targets
    builder/staging/documentation/ src/resources/documentation, shipped

    dist/RiveTT-Setup-<version>.exe

ISCC reads `builder/staging/` and writes `dist/`. Everything in `dist/` is
publishable as it stands — nothing else belongs there.

The server has no Revit API reference, so it is built once and shared. It is
self-contained on purpose: framework-dependent it would need the .NET 10 runtime
under Program Files, and installing that is the one thing that would demand admin.

`-SkipInstaller` fills `builder/staging/` without Inno Setup and does not create
`dist/` at all; `-RevitVersion 2027` builds a single target (the resulting
installer then serves that target only).

A failing test STOPS the build. It used to warn and package anyway, from a time when
13 tests could not pass off a Revit workstation; they report a clean Skip now, so a
red run is a real failure and the installer it would have produced is not shippable.
`-AllowTestFailures` overrides that, and says so again next to the installer path.

## Install

    .\dist\RiveTT-Setup-<version>.exe

Per-user, no elevation, no UAC prompt. Revit does NOT have to be closed for an
update — locked files are parked as `.old-<stamp>` and replaced. It must be closed
to uninstall.

## Release checks

- Revit 2026.5+ and 2027 / .NET 10 / x64 only.
- Generated plugin/server binaries and addin manifest are not committed;
  `builder/staging/` and `dist/` are both ignored in full. The versioned sources
  are `builder/build.ps1` and `builder/installer/RiveTT.iss`.
- The installer manifest is `asInvoker`. Anything that makes it request elevation
  is a regression, not a detail.
- `builder/build.ps1` stays UTF-8 WITH BOM. Without it PowerShell 5.1 reads the
  file as Windows-1252 and multi-byte characters decode into curly quotes, which it
  honours as string delimiters — that silently stripped `$LASTEXITCODE` out of a
  guard. `BuildScriptEncodingTests` fails the suite if the BOM goes missing.
- `src/resources/documentation/USER_GUIDE.md` reflects changed user-facing tools,
  and `python tools/audit-tool-surface.py` has been re-run so
  `src/resources/documentation/references/inventaire-des-outils.md` matches the
  surface being shipped. Both are installed on the workstation.
- `send_code_to_revit` sandbox tests pass.
- No references to the removed TCP, TypeScript, licensing, telemetry, updater,
  Power BI, or multi-version deployment stacks remain in active code.
