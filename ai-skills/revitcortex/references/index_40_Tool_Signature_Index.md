# 40 — Tool Signature Index

**Scope:** Indice storico non più mantenuto in MCPRVTT27.
**Sources:** Attributi `McpServerTool` nel progetto serveur C#.
**Last verified:** 2026-08-19

## Come consultare

La fonte canonica delle signatures est le code du serveur C# ; aucun schéma généré n'est conservé.

## Categorie

| Prefisso | Categoria | Esempi |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Read-only | `get_project_info`, `analyze_model_statistics` |
| `set_`, `bulk_`, `sync_`, `create_`, `delete_`, `purge_`, `wipe_`, `rename_`, `modify_`, `override_`, `change_` | Write | `set_element_parameters`, `bulk_modify_parameter_values` |
| `ifc_*` | IFC integration | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `workflow_*` | Workflow composti | `workflow_model_audit`, `workflow_clash_review` |
| `cross_app_*` | NavisCortex bridge | `cross_app_selection` |
| `say_hello`, `get_*` | Meta | Diagnostica, capabilities |

## Aggiornamento

Mettre à jour les descriptions directement sur les attributs `McpServerTool`.
