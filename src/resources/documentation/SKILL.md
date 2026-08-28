---
name: rivett
description: Use when working with RiveTT operations, Revit 2026.5+ or 2027 model automation, MCP tool workflows, or the C# plugin/server. Covers safe writes, send_code_to_revit escalation, IFC, tool development, audit security, and .NET 10 build checks.
---

# RiveTT skill router

Load only the references needed for the current task. They sit in `references/`,
next to this file, and are the same documents a human operator reads — there is no
separate AI copy to drift out of step.

## Always-on rules

1. For Revit model work, read `references/session-et-locale.md` first.
2. For writes, read `references/operations-destructives.md`; preview with
   `dryRun: true` whenever the tool supports it.
3. `send_code_to_revit` is a last resort; read
   `references/escalade-send-code-to-revit.md` before proposing it.
4. C# development targets Revit 2026.5+ and 2027, .NET 10, and x64 only. A single
   build runs against ONE Revit version: the plugin is rebuilt per target, not
   multi-targeted.
5. Keep named-pipe isolation, audit logging, structured errors, and the Roslyn
   sandbox intact.
6. `execution.toolReadOnly` classifies the tool that answered — it is NOT a
   session lock. `execution.writesAllowed` IS the session lock: every Revit
   session starts read-only, and only a human unlocks it from the RiveTT
   ribbon panel (Add-Ins tab). On `PermissionDenied` with
   `writesAllowed: false`, stop and ask for the unlock — no tool, and no
   `dryRun`, gets past it. `execution.cached: true` means the answer came from
   the cache.
7. `execution.versionMismatch` in any response means RiveTT is only half updated:
    the command list you can see is the older half's, so a renamed tool answers
    "not found" and a newer parameter is silently dropped. Stop and tell the user,
    in their terms: fully quit the AI application they are using with Revit (quit
    the app, not just the window), re-run the installer, reopen it. Restarting Revit
    does not help. Skip the plugin-versus-server explanation unless they ask.
8. Parameter names resolve in English or in the document language. A name that
   resolves to nothing comes back in `unresolvedParameterNames` (or
   `skippedFields[].reason`), never as an empty value — treat an empty column
   without such a report as real data.
9. Numeric parameter values carry `unit` and `internalValue`. Never read a bare
   number as project units.
10. Prefer `categoryBic` (`OST_*`) over the localized category label: Revit FR
   names the viewport category "Fenêtres ", like windows.
11. System types (walls, floors, railings, stairs, title blocks) are not
    loadable families: enumerate with `list_system_types`, duplicate with
    `duplicate_system_type`.

## Routing

| Request | References |
|---|---|
| Session/model discovery | `session-et-locale.md`, `choix-des-outils.md` |
| Parameter changes | `session-et-locale.md`, `operations-destructives.md`, `parametres.md` |
| Health, warnings, clashes | `session-et-locale.md`, `sante-du-modele.md` |
| Views and annotations | `session-et-locale.md`, `vues-et-annotations.md` |
| IFC | `session-et-locale.md`, `workflows-ifc.md` |
| Script escalation | `escalade-send-code-to-revit.md` |
| Which tool exists, and does it have a known defect | `inventaire-des-outils.md` |
| Exact signature of a tool | `signatures-des-outils.md` |

`references/index.md` lists all of them. `references/inventaire-des-outils.md` is
generated from the code and is the only exhaustive tool list — do not rely on a
hand-maintained one, and do not rely on the removed generated schema catalog.

## Working on RiveTT itself

Writing a C# tool, changing the response contract, or producing a release is
repository work, not workstation work: those references are NOT installed with the
product. In a clone of the repository they are under `docs/references/` —
`nouvel-outil.md`, `contrats-et-erreurs.md`,
`outils-dynamiques-et-capacites.md`, `securite-et-audit.md`,
`checklist-release.md` — and `AGENTS.md` at the root comes first.
