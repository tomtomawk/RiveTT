# Build, test, and package checklist

## Pre-commit

    dotnet test src/RiveTT.Tests/RiveTT.Tests.csproj -c Release
    dotnet build RiveTT.sln -c Release

Confirm that the complete test tree is compiled; do not disable default
`Compile` items to obtain a green build.

## Package

    .\build.ps1

This rebuilds and publishes the framework-dependent .NET 10 server into the
ignored `distribution/server` directory and copies plugin dependencies into
`distribution/plugin`.

## Install

    .\distribution\install.ps1

Close Revit first. The install scope is the current user and Revit 2027 only.

## Release checks

- Revit 2027 / .NET 10 / x64 only.
- Generated plugin/server binaries and addin manifest are not committed.
- `docs/USER_GUIDE.md` reflects changed user-facing tools.
- `send_code_to_revit` sandbox tests pass.
- No references to the removed TCP, TypeScript, licensing, telemetry, updater,
  Power BI, or multi-version deployment stacks remain in active code.
