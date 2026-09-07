---
name: rivett-lite
description: "Skill RiveTT compact pour le travail quotidien dans Revit 2026.5+ ou 2027 : ouvrir et sauvegarder des documents, modéliser l'architecture, créer des vues et des feuilles, placer les vues et exporter en PDF."
---

# RiveTT Lite — architecture et présentation

Ce skill privilégie les opérations courantes de production : cycle de vie des fichiers Revit, modélisation architecturale, vues, feuilles, annotations, nomenclatures et export PDF. Il omet volontairement l'installation, le développement du connecteur, les audits détaillés et l'inventaire exhaustif afin de limiter le contexte chargé par l'agent.

## Règles essentielles

1. Au début d'une session, appeler `get_project_info` une fois au complet. Pour les contrôles suivants, désactiver les listes déjà connues avec `includeLevels`, `includePhases`, `includeWorksets` et `includeLinks` à `false`.
2. Avant la première écriture et après tout changement de document, vérifier `execution.documentTitle` et `execution.revitProcessId`. Tous les appels suivants ciblent le document devenu actif.
3. Chaque session Revit démarre en lecture seule. Seul un humain peut autoriser les écritures avec **Compléments → RiveTT → Écriture**. Sur `PermissionDenied` avec `writesAllowed: false`, s'arrêter et demander le déverrouillage.
4. Utiliser `dryRun: true` quand `execution.supportsDryRun` vaut `true`, résumer l'aperçu, puis appliquer avec `dryRun: false`. Quand cette capacité vaut `false`, limiter étroitement la portée et vérifier aussitôt le résultat.
5. Après une modification, relire les éléments ou la vue concernés. La réponse doit décrire ce qui a réellement été appliqué, pas seulement la demande.
6. Utiliser des identifiants découverts dans le document. Préférer `categoryBic` (`OST_*`) aux noms de catégories localisés.
7. Les entrées géométriques sont en millimètres sauf indication contraire. En sortie, lire `unit`, `displayValue` et `internalValue` ; Revit stocke notamment les longueurs en pieds.
8. Résoudre les paramètres en anglais ou dans la langue du document. Un nom non reconnu doit apparaître dans `unresolvedParameterNames` ou `skippedFields[].reason`.
9. Préférer toujours l'outil dédié. `send_code_to_revit` reste un dernier recours et exige une demande explicite de l'utilisateur après présentation de l'alternative dédiée.

## Séquence de travail recommandée

1. `get_project_info` pour identifier le projet, les niveaux et la cible active.
2. `list_system_types` ou `list_family_types` pour obtenir les types réellement disponibles.
3. `filter_elements` ou `get_current_view_elements` pour trouver les hôtes et objets existants.
4. Prévisualiser l'opération quand elle accepte `dryRun`.
5. Appliquer, puis contrôler avec un outil de lecture.
6. `save_document` lorsque le résultat est validé.

Ne pas inventer un identifiant, un nom de type ou un paramètre. La signature MCP publiée par l'outil est le contrat exact ; ne transmettre que les paramètres qu'elle expose.

## Documents Revit

| Besoin | Outil | Point d'attention |
|---|---|---|
| Identifier la cible active | `get_project_info` | Premier appel de la session |
| Créer un projet vierge | `create_document` | Part d'un `.rte`; `targetPath` est un chemin `.rvt` absolu |
| Ouvrir et activer un projet | `open_document` | Enregistrer d'abord le document courant; le changement vide les caches |
| Enregistrer | `save_document` | Prévisualise le chemin et les blocages |
| Enregistrer sous | `save_as_document` | Duplique le modèle ouvert; ne crée pas un projet vierge |
| Ouvrir une famille ou un gabarit | `open_family`, `open_template` | Le document actif change jusqu'au retour vers le projet |
| Fermer un document | `close_document` | `saveModified` vaut `false` par défaut; un autre document doit être ouvert pour fermer l'actif |

Après `open_document`, `create_document` avec `activate: true`, `open_family` ou `open_template`, relire `execution.documentTitle` avant toute autre écriture.

## Découverte du modèle

| Besoin | Outil |
|---|---|
| Capacités effectives, verrou, `dryRun` et cycle de vie | `get_server_capabilities` |
| Types système : murs, sols, toits, plafonds, escaliers, garde-corps, cartouches | `list_system_types` |
| Types de familles chargeables : portes, fenêtres, mobilier | `list_family_types` |
| Éléments par catégorie, niveau, classe ou boîte englobante | `filter_elements` |
| Éléments de la vue active | `get_current_view_elements` |
| Paramètres d'éléments connus | `get_element_parameters` |
| Sélection Revit courante | `get_selected_elements` |
| Matériaux | `list_materials` |
| Structure multicouche d'un type | `get_compound_structure` |

Les murs, sols, plafonds, toits, escaliers, garde-corps et cartouches sont des types système. Les dupliquer avec `duplicate_system_type`, pas avec `duplicate_family_type`.

## Architecture

### Structure du projet

| Besoin | Outil | Notes |
|---|---|---|
| Créer, modifier, renommer ou supprimer un niveau | `create_level` | `action=create|set|rename|delete` |
| Créer ou gérer des axes | `create_grid` | Espacements et étendues en mm |
| Dupliquer un étage | `duplicate_storey` | Prévisualise groupes, contraintes et dépendances |

### Éléments architecturaux

| Besoin | Outil | Notes |
|---|---|---|
| Mur natif | `create_wall` | `wallTypeId` et `baseLevelId` requis; l'altitude vient du niveau et de `baseOffset` |
| Plusieurs murs ou éléments linéaires | `create_line_based_element` | Accepte aussi un arc avec `pMid` |
| Porte ou fenêtre | `create_door`, `create_window` | Hôte requis; préférer `zMode: relativeToLevel` pour seuil ou allège |
| Sol architectural | `create_floor` | Contour explicite ou `roomId`, trous facultatifs |
| Sol, plafond ou toiture par contour | `create_surface_based_element` | Catégories `OST_Floors`, `OST_Ceilings`, `OST_Roofs` |
| Pièce | `create_room` | Le point doit appartenir à une boucle fermée |
| Séparation de pièce | `create_room_separation_line` | Lignes 2D dans une vue en plan |
| Trémie, ouverture d'hôte ou ouverture de mur | `create_opening` | Choisir `shaft`, `host` ou `wall` |
| Escalier, rampe ou garde-corps | `create_stair`, `create_ramp`, `create_railing` | Niveaux et chemins explicites |
| Objet ponctuel générique | `create_point_based_element` | Pour portes et fenêtres, préférer les outils dédiés |

Pour modifier l'existant : `modify_element` déplace, tourne, symétrise ou copie; `change_element_type` remplace le type; `set_element_parameters` écrit des paramètres; `delete_element` prévisualise aussi la cascade de suppression.

## Éléments complémentaires

Ces outils restent disponibles dans le contexte Lite. Consulter leur signature MCP au moment de l'appel plutôt que de charger ici tous leurs paramètres.

| Domaine | Outils |
|---|---|
| Murs-rideaux | `get_curtain_grid_info`, `add_curtain_grid_line`, `add_curtain_mullions` |
| Création avancée | `create_array`, `create_assembly`, `create_detail_line`, `create_model_line`, `create_structural_framing_system`, `create_toposolid`, `manage_area_plans` |
| Copie et transformation | `copy_elements`, `match_element_properties`, `detach_wall_constraint` |
| Recherche et mesure | `filter_by_parameter_value`, `find_undimensioned_elements`, `get_elements_by_unique_id`, `get_elements_in_spatial_volume`, `get_linked_elements`, `get_room_openings`, `measure_between_elements` |
| Sélections et affichage | `capture_selection`, `manage_selection`, `manage_view_display` |
| Familles | `load_family`, `edit_family`, `rename_families`, `export_families` |
| Groupes | `manage_model_groups`, `edit_group_members` |
| Données | `export_elements_data`, `export_room_data` |
| Nommage et propriétés | `batch_rename`, `renumber_elements`, `set_element_phase`, `set_material_properties` |

Précautions ciblées :

- `copy_elements` peut viser une autre vue ou un autre document déjà ouvert; vérifier la cible avant exécution.
- `edit_group_members` recrée un type de groupe, car l'API Revit ne modifie pas un groupe en place.
- `detach_wall_constraint`, `batch_rename` et les modifications de groupes peuvent avoir une portée large : prévisualiser et contrôler les identifiants affectés.
- `create_detail_line` appartient à une vue; `create_model_line` appartient au modèle.

## Projet et maintenance

| Domaine | Outils |
|---|---|
| Santé et analyse | `check_model_health`, `list_warnings`, `analyze_model_statistics`, `audit_families`, `detect_clashes`, `count_lines_per_view` |
| Phases, variantes et sous-projets | `list_phases`, `manage_phase_filters`, `list_design_options`, `list_worksets`, `manage_worksets` |
| Matériaux et compositions | `create_material`, `duplicate_material`, `delete_material`, `get_material_properties`, `get_material_quantities`, `set_compound_structure` |
| Paramètres partagés | `list_shared_parameters`, `export_shared_parameter_file` |
| Nomenclatures et exports | `create_key_schedule`, `list_schedulable_fields`, `export_schedule` |
| Réglages du projet | `manage_additional_settings`, `manage_project_units`, `set_project_info` |
| Liens et nettoyage | `manage_links`, `clean_cad_links`, `purge_unused`, `list_family_sizes` |
| Travail collaboratif | `synchronize_with_central` |

Pour un contrôle rapide, appeler `check_model_health`, puis `list_warnings` avec `maxWarnings: 10`. Ne lancer `detect_clashes` que sur les deux catégories utiles.

`synchronize_with_central` affecte toute l'équipe et n'est pas annulable depuis RiveTT. Il exige le verrou d'écriture ouvert et `dryRun: false`; toujours commencer par son aperçu.

## Paramètres

| Besoin | Outil |
|---|---|
| Créer, supprimer, modifier ou renommer un paramètre de projet | `manage_project_parameters` |
| Ajouter un paramètre partagé | `add_shared_parameter` |
| Lister, créer, calculer ou organiser des paramètres globaux | `manage_global_parameters` |
| Modifier des valeurs en masse | `batch_modify_parameter_values` |
| Ajouter préfixes ou suffixes | `batch_rename_affix` |
| Effacer des valeurs | `clear_parameter_values` |
| Copier des valeurs entre éléments | `transfer_parameters` |
| Synchroniser depuis un CSV | `sync_csv_parameters` |

Pour une valeur de longueur, surface ou volume, préférer une chaîne avec unité telle que `"3000 mm"` plutôt qu'un nombre brut en unités internes Revit. Toute opération de masse doit commencer par une portée stable : identifiants explicites, sélection capturée ou catégorie étroitement filtrée.

## Vues

| Besoin | Outil | Notes |
|---|---|---|
| Lire la vue active | `get_current_view_info` | Donne type, échelle et contexte |
| Créer une vue | `create_view` | Plans, plafonds, coupe, élévation, détail, callout ou 3D |
| Dupliquer une vue | `duplicate_view` | `Duplicate`, `AsDependent` ou `WithDetailing` |
| Créer des vues par pièce | `create_views_from_rooms` | Callouts, coupes ou élévations |
| Renommer des vues en série | `rename_views` | Recherche/remplacement, préfixe ou suffixe |
| Créer une boîte de coupe 3D | `create_section_box_from_selection` | Part de la sélection |
| Appliquer un gabarit | `apply_view_template` | Lister, appliquer ou retirer |
| Gérer les gabarits | `manage_view_templates` | Lister, dupliquer, renommer ou supprimer |
| Régler la plage de vue | `batch_modify_view_range` | Décalages en mm |
| Créer ou appliquer un filtre | `create_view_filter` | Règles de paramètres AND/OR |
| Modifier l'affichage | `override_graphics`, `color_elements` | Toujours cibler explicitement `viewId` quand possible |
| Gérer les zones de définition | `manage_scope_boxes` | L'API ne sait pas en créer; partir d'une zone dessinée dans Revit |
| Repérer les vues non placées | `manage_unplaced_views` | Lister avant toute suppression |

Lors de `create_view`, définir si possible l'échelle, le gabarit et le recadrage (`cropActive`, `cropMin`, `cropMax`) dès la création. Une vue non recadrée peut produire un viewport beaucoup plus grand que la feuille.

## Feuilles et mise en page

### Workflow vue → feuille

1. Obtenir le type de cartouche avec `list_system_types` sur `OST_TitleBlocks`.
2. Créer la vue avec `create_view`, son échelle et son recadrage, ou la dupliquer avec `duplicate_view`.
3. Créer la feuille avec `create_sheet`; fournir `titleBlockId` ou un nom de famille/type. Sans cartouche, Revit crée une feuille nue de 210 × 297 mm.
4. Placer la vue avec `place_viewport`. `positionX` et `positionY` désignent le **centre** du viewport en millimètres dans les coordonnées de la feuille; les omettre pour centrer.
5. Contrôler `sheetSizeMm`, `viewportOutlineMm` et `fitsOnSheet` dans la réponse.
6. Utiliser `align_viewports` pour harmoniser plusieurs feuilles.

| Besoin | Outil |
|---|---|
| Créer une feuille | `create_sheet` |
| Créer plusieurs feuilles et placer leurs vues | `batch_create_sheets` |
| Ajouter un cartouche à une feuille existante | `place_title_block` |
| Placer une vue sur une feuille | `place_viewport` |
| Aligner des viewports entre feuilles | `align_viewports` |
| Dupliquer une feuille | `duplicate_sheet_with_content`, `duplicate_sheet_with_views` |
| Gérer les feuilles réservées | `create_placeholder_sheets` |
| Gérer les révisions et nuages | `create_revision` |

Une vue ne peut normalement être placée que sur une seule feuille. Le `dryRun` de `batch_create_sheets` signale les numéros dupliqués et les vues déjà placées.

## Annotations et nomenclatures

| Besoin | Outil |
|---|---|
| Cotes linéaires | `create_dimensions` |
| Cotes altimétriques | `create_spot_dimension` |
| Notes de texte | `create_text_note` |
| Étiquettes de pièces ou de murs | `tag_rooms`, `tag_walls` |
| Rechercher les éléments non étiquetés | `find_untagged_elements` |
| Région remplie ou légende colorée | `create_filled_region`, `create_color_legend` |
| Créer une nomenclature | `create_schedule`, `create_preset_schedule` |
| Lire ou modifier une nomenclature | `get_schedule_data`, `modify_schedule` |
| Dupliquer ou supprimer une nomenclature | `duplicate_schedule`, `delete_schedule` |

Les annotations appartiennent à une vue. Fournir `viewId` lorsqu'il est exposé; sinon l'outil travaille sur la vue active. `tag_walls` utilise uniquement la vue active.

## Export PDF

1. Identifier les feuilles avec `filter_elements` sur `OST_Sheets`, ou constituer un jeu nommé avec `manage_sheet_sets`.
2. Vérifier les vues placées, le cartouche et `fitsOnSheet` avant export.
3. Appeler `batch_export` au format PDF avec les identifiants de feuilles ou le jeu demandé. Utiliser un chemin absolu et les options publiées dans la signature MCP courante.
4. Vérifier les fichiers produits dans la réponse; l'export écrit sur disque même s'il ne modifie pas la maquette.
5. Sauvegarder séparément le modèle avec `save_document` si la mise en page a été modifiée.

`batch_export` couvre aussi DWG, DXF, DGN et PNG, mais le PDF est le choix par défaut pour une livraison de planches.

## Lire les réponses

Chaque succès précise notamment `pluginVersion`, `mcpServerVersion`, `revitVersion`, `revitProcessId`, `documentTitle`, `toolReadOnly`, `toolDestructive`, `supportsDryRun`, `writesAllowed`, `cached` et, si nécessaire, `versionMismatch`.

- `toolReadOnly` classe l'outil; `writesAllowed` représente le verrou de session.
- `cached: true` indique une réponse mise en cache, pas une observation fraîche.
- Un aperçu valide contient `dryRun: true` et `mutated: false`.
- Une transaction échouée doit fournir `warnings`, `errors`, `rolledBack`, `failedElementIds` et des pistes de réparation.
- `filter_elements` pagine avec `totalCount`, `returnedCount`, `appliedLimit` et `nextCursor`.
- Si `versionMismatch` apparaît, arrêter les opérations : l'application d'IA doit être quittée complètement, l'installateur relancé, puis l'application rouverte.
