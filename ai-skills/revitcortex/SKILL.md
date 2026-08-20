---
name: revitcortex
description: Use when working with MCPRVTT27/RevitCortex operations, Revit 2027 model automation, MCP tool workflows, or the C# plugin/server. Covers safe writes, send_code_to_revit escalation, IFC, tool development, audit security, and .NET 10 build checks.
---

# MCPRVTT27 skill router

Load only the references needed for the current task.

## Always-on rules

1. For Revit model work, read `operator_01_Session_Start_Locale.md` first.
2. For writes, read `operator_03_Destructive_Operations_DryRun.md`; preview with
   `dryRun: true` whenever the tool supports it.
3. `send_code_to_revit` is a last resort; read
   `operator_10_SendCodeToRevit_Escalation.md` before proposing it.
4. C# development targets Revit 2027, .NET 10, and x64 only.
5. Keep named-pipe isolation, audit logging, structured errors, and the Roslyn
   sandbox intact.

## Routing

| Request | References |
|---|---|
| Session/model discovery | `operator_01`, `operator_02` |
| Parameter changes | `operator_01`, `operator_03`, `operator_04` |
| Health, warnings, clashes | `operator_01`, `operator_05` |
| Views and annotations | `operator_01`, `operator_06` |
| IFC | `operator_01`, `operator_07` |
| Script escalation | `operator_10` |
| New C# tool | `developer_20`, `developer_21`, `developer_23`, `developer_24`, `developer_25` |
| Build or test failure | `developer_25` |
| Security/audit | `developer_24` |

`index_40_Tool_Signature_Index.md` explains where the canonical MCP signatures
live. Do not rely on the removed generated schema catalog.
