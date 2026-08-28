# Workflows IFC

**Portée :** l'ordre des opérations IFC — liaison, reconstruction en éléments natifs,
export. Le détail outil par outil est dans `../IFC.md`.
**Sources :** les 20 outils `ifc_*`, `inventaire-des-outils.md`.
**Vérifié le :** 2026-08-28

## Toujours commencer par les capacités

`ifc_get_capabilities` en premier appel IFC de la session : il dit quelles versions
IFC sont prises en charge et si le module `revit-ifc` est présent. Importer un IFC
lourd sans l'avoir demandé, c'est découvrir l'incompatibilité après l'attente.

## Reconstruire en éléments natifs

La séquence, dans cet ordre :

1. `ifc_open_or_import` ou `ifc_link` ;
2. `ifc_analyze_rebuildability` avec `compact: true` — ce qui est reconstructible ;
3. `ifc_list_rebuild_candidates` avec `compact: true`, filtré par catégorie ;
4. la reconstruction, **une catégorie à la fois** :
   `ifc_rebuild_walls` · `ifc_rebuild_floors` · `ifc_rebuild_roofs` ·
   `ifc_rebuild_openings` · `ifc_rebuild_structural_members` ·
   `ifc_rebuild_family_instances` ;
5. `ifc_compare_original_vs_rebuilt` pour vérifier ;
6. `ifc_tag_unreconstructable_elements` pour marquer ce qui n'a pas pu l'être.

Avant une reconstruction coûteuse, `ifc_validate_request` valide la demande sans
l'exécuter.

`ifc_set_family_mapping_file` charge une correspondance de familles sur mesure avant
la reconstruction — à faire en amont de l'étape 4, pas après.

## Exporter

| Besoin | Outils |
|---|---|
| Export simple | `ifc_export_basic` |
| Export avec configuration | `ifc_get_export_configuration` puis `ifc_export_with_configuration` |

`ifc_list_export_configurations` énumère les configurations disponibles dans le
projet.

## À éviter

- Reconstruire sans avoir lancé `ifc_analyze_rebuildability`.
- Reconstruire toutes les catégories en une fois, ou en parallèle.
- Importer un IFC lourd sans avoir vérifié les capacités.
- Omettre `compact: true` sur les outils d'analyse : leurs réponses sont volumineuses.
