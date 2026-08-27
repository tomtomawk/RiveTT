# Signatures des outils

**Portée :** où trouver la signature exacte d'un outil. Ce document n'en tient
pas la liste.
**Sources :** attributs `McpServerTool` du serveur C#, `inventaire-des-outils.md`.
**Vérifié le :** 2026-08-28

## Où regarder

La source canonique d'une signature est le code du serveur C# ; aucun schéma
généré n'est conservé. Pour la liste exhaustive des outils publiés, avec leur
nature, leur `dryRun` et leurs défauts connus, voir `inventaire-des-outils.md`,
généré depuis le code par `tools/audit-tool-surface.py`.

## Catégories

| Préfixe | Catégorie | Exemples |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Lecture | `get_project_info`, `analyze_model_statistics` |
| `set_`, `batch_`, `sync_`, `create_`, `delete_`, `purge_`, `rename_`, `modify_`, `override_`, `change_` | Écriture | `set_element_parameters`, `batch_modify_parameter_values` |
| `ifc_*` | IFC | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `workflow_*` | Workflows composés | `workflow_model_audit`, `workflow_room_documentation` |
| `get_*` | Méta | Diagnostic, capacités du serveur |

## Signatures

Aucune liste n'est recopiée ici. Une l'a été, tenue à la main, et elle a cessé
d'être exacte au premier renommage : `filter_elements`, `list_family_types` et
`list_materials` y figuraient encore après avoir disparu en 0.3.0. Le README du
dépôt a supprimé sa propre section « Fonctions ajoutées » pour la même raison.

Pour la signature d'un outil : l'attribut `[McpServerTool]` correspondant dans
`src/RiveTT.Server/Tools/`. Pour savoir quels outils existent :
`inventaire-des-outils.md`.

## Contrat de réponse

Tout succès porte `execution.{connector, serverVersion, revitVersion, mode,
toolReadOnly, toolDestructive, writesAllowed, cached}`. `toolReadOnly` classe
l'outil, pas la session. Les anciens noms `readOnly`/`destructive` n'existent
plus.

## Mise à jour

Les descriptions se corrigent directement sur les attributs `[McpServerTool]`,
puis `python tools/audit-tool-surface.py` régénère l'inventaire.
