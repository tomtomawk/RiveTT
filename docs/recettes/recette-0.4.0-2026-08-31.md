# Recette RiveTT 0.4.0 — 2026-08-31

Maquette : `C:\Users\theba\Desktop\Revit\Saint-Malo_avenue aristide briand_46.rvt`
**— PROJET RÉEL, NON CONFORME AU PRÉALABLE 3 (voir Réserves)**
Revit 2027 · plugin 0.4.0.0 / serveur 0.4.0.0 · locale document `fr`
Opérateur : Claude Opus 5 (Claude Code) · Auditeur : *non désigné*

Journal d'audit : `%LOCALAPPDATA%\RiveTT\audit.jsonl`, horodatage de départ
2026-08-31T21:31:25Z (démarrage de session).

## Réserves bloquantes

1. **La maquette est un projet en cours de l'agence** (MEIGNAN ENGASSER PERAUD,
   étude de faisabilité, Saint-Malo). Le préalable 3 exige une copie sur disque
   local, jamais un projet en cours. Les blocs 3 à 5 (dryRun, prévisualisations,
   écritures) ne peuvent pas être exécutés dessus.
2. Contenu manquant par rapport à la table « La maquette de recette » :
   `hasWorksets: false`, `hasDesignOptions: false`, modèle non partagé. Les outils
   worksets et options de conception ne sont pas publiés — non testables.
3. Le seul lien est **déchargé** (`isLoaded: false`,
   `SAINT-MALO_40 ARISTIDE BRIAND.rvt`). Les outils LinkedFiles sont publiés mais
   opéreront sur un lien non chargé.

## Étape 0 — installateur

| # | Situation | Attendu | Observé | Verdict |
|---|---|---|---|---|
| 0.1 | Client MCP ouvert, lancer l'installateur | Boîte « RiveTT est actuellement utilisé par une application d'IA » | Affichée | confirmé |
| 0.2 | Répondre Non | Arrêt, aucun fichier déplacé | Arrêt, versions inchangées | confirmé |
| 0.3 | Relancer client ouvert, répondre Oui | Page finale « ATTENTION : mise à jour incomplète » | **Non éprouvé** : le serveur n'était pas périmé, la page verte était donc correcte | non concluant |
| 0.4 | Client fermé, relancer | Page verte, versions servies listées | Page verte ; serveur posé en 0.4.0 (39 305 424 o, 31/08 22:25) | confirmé |
| 0.7 | `get_server_capabilities` après redémarrage Revit | `pluginVersion == mcpServerVersion`, pas de `versionMismatch` | 0.4.0.0 / 0.4.0.0, aucun mismatch | confirmé |
| 0.8 | Installateur sans droits admin | Aucune invite UAC | Aucune | confirmé |

0.5 et 0.6 non exécutés.

## Défauts

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|
| installateur | majeur | Une installation lancée **depuis une application packagée MSIX** (Claude Desktop) dépose une copie de `{app}\server\` dans `…\Packages\Claude_*\LocalCache\Local\RiveTT\`. Windows sert ensuite cette copie figée à tout processus enfant de ce client — dont le serveur MCP. Le serveur exécuté reste à la version du jour de cette installation pendant que l'installateur, hors conteneur, écrit et relit le vrai fichier et conclut à juste titre que tout va bien. `InstalledServerVersion()` ne peut pas voir l'écart. | Détecter la copie virtualisée, ou refuser de s'exécuter depuis un conteneur d'application packagée | Constaté le 31/08 : plugin 0.4.0 / conteneur 0.2.0 (`2e3651b`, 26/08). Surface d'outils pré-0.3.0 publiée (`get_warnings`, `get_phases`, `say_hello`). Après suppression de la copie conteneur, le serveur republie la surface 0.4.0 (`list_warnings`, `list_phases`, `ping_revit`) et les versions concordent. |

## Écarts de documentation

| Source | Annonce | Mesure `get_server_capabilities` |
|---|---|---|
| `docs/CHANGELOG_0.4.0.md` §3 | couverture dryRun **56 / 135** | `previewingWriteTools: 100`, `writeTools: 136` |

## Relevé

| Outil | Appel | Réponse (résumé) | dryRun | Vérification | Verdict |
|---|---|---|---|---|---|
| `get_server_capabilities` | sans paramètre | plugin 0.4.0.0 == serveur 0.4.0.0, `readOnlyMode.active: true`, `writesAllowed: false`, `changedBy: startup` | n/a | | |
| `get_project_info` | `includeLevels/Links/Phases/Worksets: true` | 12 niveaux, 3 phases, 1 lien déchargé, `isWorkshared: false` | n/a | | |

## Non testés

| Outil | Pourquoi |
|---|---|
| outils worksets | `hasWorksets: false` — non publiés |
| outils options de conception | `hasDesignOptions: false` — non publiés |
| tous outils d'écriture | maquette non conforme (projet réel) + verrou fermé |

## Synthèse

Étape 0 : 6 lignes sur 8 exécutées, 5 confirmées, 1 non concluante, 1 défaut majeur
hors périmètre du protocole (virtualisation MSIX).
Relevé outils : en cours.

## Mise à jour — maquette remplacée en cours de recette

Le document ouvert est passé de `Saint-Malo_avenue aristide briand_46.rvt` (projet réel)
à `Fichier test 0.4.0.rvt` (maquette dédiée). Les réserves ci-dessus concernant le
préalable 3 sont levées pour cette maquette : options de conception présentes
(1 jeu, 2 options), 1 lien chargé (`Appartement_T2_MCP_Test.rvt`) + 1 lien déchargé
(`SAINT-MALO_40 ARISTIDE BRIAND.rvt`, non identifié — laissé tel quel, à charger
seulement si son origine est confirmée), 119 avertissements réels, locale `fr`,
12 niveaux nommés en français. **Toujours pas de worksets** (`list_worksets` refuse
en `InvalidInput`) : outils worksets non publiés, resteront `non testé`.

## Défauts — bloc 1 (lecture, verrou fermé)

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|
| `get_current_view_elements` | **critique** | Inutilisable dans toute combinaison testée. `modelCategoryList`/`annotationCategoryList` omis arrivent côté outil comme chaîne vide `""` et sont refusés au lieu d'être traités comme « aucun filtre ». `categoryFilter` (paramètre documenté comme raccourci une-catégorie) est totalement ignoré : l'erreur porte toujours sur `modelCategoryList`. Un tableau JSON valide (`annotationCategoryList: []`) est rapporté `received: "[]"` — reçu comme texte, pas comme structure : sérialisation double côté passerelle MCP. | Traiter un paramètre tableau omis comme absent ; lire `categoryFilter` ; désérialiser un tableau JSON valide comme tableau. | `get_current_view_elements(pageSize:20)` → `InvalidInput` ; `get_current_view_elements(categoryFilter:"Murs")` → même erreur ; `get_current_view_elements(modelCategoryList:["Murs"])` → `InvalidInput` sur `annotationCategoryList` ; `get_current_view_elements(modelCategoryList:["Murs"], annotationCategoryList:[])` → `received: "[]"`. |
| `list_family_types` | majeur | Même classe : `categoryList` omis → `received: ""`, refusé. Contournement : le fournir explicitement (`["Portes"]`) fonctionne. | Idem. | `list_family_types(compact:true, limit:5)` sans `categoryList` → `InvalidInput` ; avec `categoryList:["Portes"]` → OK. |
| `filter_by_parameter_value` | majeur | Même classe, sur 2 paramètres en cascade : `conditions` omis → refusé ; une fois fourni, `elementIds` (non pertinent pour cet appel : filtre par catégorie, pas par sélection) est à son tour refusé. L'outil semble valider tous ses paramètres tableau qu'ils soient utilisés ou non par le mode d'appel choisi. | Ne valider que les paramètres tableau pertinents pour le `scope`/mode effectivement utilisé ; traiter l'omission comme absence. | `filter_by_parameter_value(category:"Pièces", parameterName:"Département", condition:"is_not_empty")` → `InvalidInput` sur `conditions` ; en ajoutant `conditions:[...]` → `InvalidInput` sur `elementIds`. Non concluant : pas encore fait aboutir cet outil. |
| `find_untagged_elements` | mineur | Résolveur de catégorie n'accepte pas le français (`"Portes"` → `None of the provided categories could be resolved`) alors que `filter_elements`, `list_family_types`, `list_system_types` l'acceptent tous. `"Doors"` (anglais) fonctionne. | Utiliser le même résolveur de catégorie que le reste de la surface (français + anglais + OST_*). | `find_untagged_elements(category:"Portes", viewId:9916678)` → échec ; `category:"Doors"` → OK, 49 éléments non étiquetés. |
| `list_schedulable_fields` | mineur | Même défaut de résolveur que ci-dessus : `"Portes"` → `Unknown category`, `"Doors"` → OK (1212 champs). | Idem. | `list_schedulable_fields(categoryName:"Portes")` → échec ; `categoryName:"Doors"` → OK. |

## Relevé — bloc 1 (suite)

| Outil | Appel | Réponse (résumé) | Vérification | Verdict |
|---|---|---|---|---|
| `list_phases` | sans paramètre | 3 phases, 5 filtres de phase | — | confirmé |
| `list_materials` | `compact:true` | Réponse tronquée par la taille (63 977 car., 2279 lignes) — non lue en entier | — | non concluant (taille) |
| `list_shared_parameters` | `compact:true` | 48 paramètres projet, catégories listées | — | confirmé |
| `list_system_types` | sans catégorie | 1015 types système / 77 catégories | — | confirmé |
| `list_system_types` | `category:"OST_Walls", limit:5` | 5/70 types Murs, épaisseurs correctes | — | confirmé |
| `get_selected_elements` | sans paramètre | 0 sélectionné (cohérent, rien sélectionné dans Revit) | — | confirmé |
| `analyze_model_statistics` | `compact:true` | 22509 éléments, 1630 types, 313 familles, 165 vues, 40 feuilles, 119 avertissements | recoupe `list_warnings` (119) | confirmé |
| `filter_elements` | `filterCategory:"Murs", pageSize:5` | 2248 murs, pagination `nextCursor` cohérente | — | confirmé |
| `count_lines_per_view` | sans paramètre | 401 lignes de détail, 125 vues balayées | recoupe `analyze_model_statistics.totalViews` (165, écart normal : vues sans lignes non comptées comme "scannées"?) à vérifier | non concluant sur l'écart 125 vs 165 |
| `export_room_data` | `maxResults:10` | 10/137 pièces, RDC et R+1, surfaces cohérentes | — | confirmé |
| `list_family_types` | `categoryList:["Portes"]` | 5/28 types Portes | — | confirmé |
| `get_compound_structure` | `typeName:"ARC_Cloison distribution_7 cm"` | 3 couches, 70 mm total, cohérent avec `list_system_types.thicknessMm` | recoupé | confirmé |
| `get_material_quantities` | `categoryFilters:["Murs"]` | 5 matériaux, surfaces/volumes cohérents | — | confirmé |
| `get_material_properties` | `materialName:"ARC_MAT_PLATRE"` | propriétés physiques/thermiques complètes | — | confirmé |
| `get_element_solid_geometry` | `elementId:10047369` (pièce Caféteria) | bbox, volume 95.5 m³, section rectangulaire | cohérent avec `export_room_data` (39.18 m² × ~2.44 m hauteur ≈ 95.6 m³) | confirmé |

## Synthèse (mise à jour)

Étape 0 : 6/8 exécutées, 5 confirmées, 1 non concluante.
Bloc 1 (lecture) : 15 outils exercés, 11 confirmés, 2 non concluants, 5 défauts
(1 critique, 2 majeurs, 2 mineurs). Reste ~44 outils de lecture à couvrir.
Bloc 2 (refus, verrou fermé) : non commencé.

## Défaut transversal — désérialisation des paramètres tableau (remplace les entrées ponctuelles ci-dessus)

**Gravité : critique.** Ce n'est pas un défaut par outil : c'est un défaut de la couche de
désérialisation partagée entre la passerelle MCP et `RiveTT.Tools`, qui touche tout
paramètre de type tableau sur au moins 7 outils testés.

**Mécanique observée :**
- un paramètre tableau omis arrive côté outil comme une **chaîne vide littérale** `""`,
  jamais comme absent ;
- un tableau JSON explicitement vide (`[]`) arrive comme la **chaîne** `"[]"`, pas comme
  une structure — le message d'erreur cite `received: "[]"` ;
- seul un tableau **non vide** (`["Portes"]`, `[10047369]`…) traverse correctement la
  passerelle et est reçu comme un vrai tableau côté outil.

Conséquence : tout outil qui déclare plusieurs paramètres tableau, dont certains
non pertinents pour le mode d'appel choisi, est bloqué dès qu'un seul de ces
paramètres n'a pas de valeur "réelle" à fournir — même quand le paramètre alternatif
prévu par la conception de l'outil (ex. `elementId1`/`elementId2` au lieu de
`point1`/`point2`) est correctement renseigné.

**Outils atteints, avec preuve :**

| Outil | Paramètre bloquant | Contournement trouvé |
|---|---|---|
| `get_current_view_elements` | `modelCategoryList`, `annotationCategoryList` | **aucun** — toute combinaison testée échoue |
| `list_family_types` | `categoryList` | fournir un tableau non vide |
| `filter_by_parameter_value` | `conditions`, puis `elementIds` | non résolu — 2 paramètres bloquants en cascade |
| `get_room_openings` | `roomIds` (même quand `roomNumbers` est fourni) | non résolu — `roomIds:[]` échoue à l'identique de l'omission |
| `measure_between_elements` | `point1`/`point2` (même quand `elementId1`/`elementId2` sont fournis) | fournir un tableau bidon non vide ; le calcul utilise bien les IDs, pas les points bidons — **workaround exploitable** |
| `get_elements_in_spatial_volume` | `categoryFilter` (même quand `volumeIds` est fourni) | non résolu — `categoryFilter:[]` échoue à l'identique de l'omission |
| `export_elements_data` | `elementIds` (même quand `categories` est fourni) | non résolu — `elementIds:[]` échoue à l'identique de l'omission |

**Ce qu'il devrait faire :** un paramètre tableau omis doit être traité comme absent
(liste vide logique), pas refusé ; le contrat annoncé par `get_server_capabilities`
et par `[Description]` sur ces paramètres ("optional") n'est tenu que si un tableau
non vide est fourni, ce qui n'est vrai pour aucun des outils testés.

## Relevé — suite (liens, sélection, export)

| Outil | Appel | Réponse (résumé) | Vérification | Verdict |
|---|---|---|---|---|
| `get_link_transform` | `linkInstanceId:11243141` | origine (-44601.9, -16681.5, 0), rotation 0°, non partagé | recoupe `list_linked_file_instances` | confirmé |
| `get_selected_linked_elements` | sans sélection | message cohérent, liste vide | — | confirmé |
| `list_coordination_models` | sans paramètre | 0 modèle, API disponible | cohérent (pas de coordination model dans cette maquette) | confirmé |
| `get_elements_by_unique_id` | uniqueId de la vue courante | résolu vers elementId 9916678, `cortexElementRef` complet | recoupe `get_current_view_info` | confirmé |
| `find_undimensioned_elements` | `category:"Doors"` | 49 portes non cotées, même liste que `find_untagged_elements` | cohérent (aucune porte cotée dans cette vue) | confirmé |
| `capture_selection` | 2 elementIds | token émis, expiration 15 min | — | confirmé |

## Synthèse (mise à jour)

Bloc 1 : 21 outils exercés. 15 confirmés, 2 non concluants (taille de réponse, écart
125/165 vues), 1 défaut transversal critique touchant 7 outils, 2 défauts mineurs de
résolveur de catégorie (français refusé). Worksets hors périmètre : maquette non
partagée, à couvrir sur un modèle central séparé.

## Relevé — fin du bloc 1 (IFC, nomenclatures, paramétrage)

| Outil | Appel | Réponse (résumé) | Vérification | Verdict |
|---|---|---|---|---|
| `ifc_get_capabilities` | sans paramètre | 14 versions IFC supportées, `revitIfcAddinInstalled: false`, export/import/lien tous `true` | à recouper : export possible sans l'add-in officiel ? | confirmé, à surveiller |
| `ifc_list_export_configurations` | `compact:true` | 5 configurations intégrées | — | confirmé |
| `ifc_get_export_configuration` | `configurationName:"IFC4 Reference View"` | options détaillées cohérentes avec le nom | — | confirmé |
| `ifc_analyze_rebuildability` | `compact:true, maxElements:10` | 0 analysé (aucun DirectShape IFC dans cette maquette) | cohérent (pas de lien IFC ouvert) | confirmé |
| `ifc_list_rebuild_candidates` | `compact:true, maxElements:10` | 0 candidat | cohérent | confirmé |
| `get_schedule_data` | `scheduleId:76999, maxRows:5` | 141 lignes, 12 colonnes | **défaut mineur** : la 1ʳᵉ ligne de données duplique les en-têtes de colonnes au lieu d'être la 1ʳᵉ ligne réelle — décalage d'indexation probable | défaut |
| `filter_elements` | `filterCategory:"Nomenclatures", responseMode:"idsOnly"` | 42 nomenclatures, ids cohérents | — | confirmé |

## Défaut — bloc 1 (suite)

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|
| `get_schedule_data` | mineur | La première ligne de `rows` reproduit les noms de colonnes (`ARC_PAR_BATIMENT`, `Service`…) au lieu de la première ligne de données réelle. | Ne renvoyer que des lignes de données ; les en-têtes sont déjà dans `headers`/`columnHeaders`. | `get_schedule_data(scheduleId:76999, maxRows:5)` → ligne 1 = en-têtes recopiés, lignes 2-5 = données réelles (Local vélo, Salle d'activité, Hall, Caféteria). |
| `manage_scope_boxes`, `manage_area_plans` | rejoint le défaut transversal | `action:"list"` / `action:"list_schemes"` — actions de **lecture pure** — échouent sur des paramètres tableau (`viewIds`, `curves`) sans rapport avec le listing, avant même le contrôle du verrou d'écriture. | Ne valider que les paramètres pertinents pour l'action demandée. | `manage_scope_boxes(action:"list")` → `InvalidInput` sur `viewIds` ; `manage_area_plans(action:"list_schemes")` → `InvalidInput` sur `curves`. Aucun élément du modèle interrogé : le défaut bloque avant l'exécution. |

## Observation — granularité de `[ToolSafety(readOnly)]`

`manage_unplaced_views`, `manage_phase_filters`, `manage_additional_settings` sont
classés `toolReadOnly: false` **au niveau de l'outil entier**. Leur action `list`
— une lecture pure — est donc refusée par le verrou de session au même titre qu'une
mutation. Cohérent avec la conception documentée (classification par outil, pas
par action) mais à signaler : un agent qui veut seulement *consulter* les filtres
de phase ou les styles de ligne ne le peut pas avant déverrouillage, ce qui n'est
peut-être pas l'intention. Non classé comme défaut — comportement conforme au
contrat annoncé — mais à confirmer avec l'équipe produit.

## Synthèse (mise à jour) — bloc 1 clos

28 outils de lecture exercés. 19 confirmés, 2 non concluants, 1 défaut transversal
critique (7 outils touchés + 2 nouvelles occurrences sur des actions de listing pur :
9 au total), 1 défaut mineur supplémentaire (`get_schedule_data`), 2 défauts mineurs
de résolveur de catégorie (déjà consignés), 3 outils "manage_*" inaccessibles en
lecture tant que le verrou est fermé (observation, pas un défaut).
Worksets hors périmètre (maquette non partagée).

Bloc 1 : **terminé**. Passage au bloc 2 (refus, verrou fermé) à suivre.

## Bloc 2 — refus, verrou fermé

Échantillon de 11 outils d'écriture, verrou toujours fermé (`writesAllowed: false`
depuis le démarrage de session, `changedBy: startup`, non modifié durant tout le bloc).

| Outil | Appel | dryRun | Réponse | Verdict |
|---|---|---|---|---|
| `create_wall` | mur 5 m, RDC | `true` | `PermissionDenied`, `writesAllowed:false` | confirmé |
| `create_wall` | même appel | `false` | `PermissionDenied`, identique — **dryRun n'exempte pas** | confirmé |
| `delete_element` | pièce Caféteria | `true` | `PermissionDenied` | confirmé |
| `delete_element` | même appel | `false` | `PermissionDenied`, identique | confirmé |
| `set_element_parameters` | `ROOM_NAME` sur la pièce | `true` | `PermissionDenied` | confirmé |
| `create_grid` | 2 grilles X, 5 m | défaut | `PermissionDenied` | confirmé |
| `purge_unused` | — | `true` | `PermissionDenied` | confirmé |
| `change_element_type` | porte → 240x200 | `true` | `PermissionDenied` | confirmé |
| `batch_rename` | type de mur | `true` | `PermissionDenied` | confirmé |
| `manage_links` | `action:"list"` | défaut | `PermissionDenied` — **une action de listing sur un outil de lien est refusée par le verrou**, cohérent avec la classification par outil déjà notée au bloc 1 | confirmé |
| `modify_schedule` | renommer nomenclature | `true` | **`InvalidInput` avant tout contrôle du verrou** — voir défaut ci-dessous | non concluant |
| `modify_element` | déplacer la pièce | `true` | **erreur brute non structurée**, aucun code | défaut |

## Défauts — bloc 2

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|
| `modify_schedule` | majeur | Le défaut transversal de désérialisation (§ bloc 1) intercepte l'appel via `fieldNames`, non pertinent pour `action:"rename"`, **avant** le contrôle `writesAllowed`. Un outil d'écriture n'atteint jamais son refus `PermissionDenied` : le test de refus que le bloc 2 doit prouver est structurellement impossible à observer pour cet outil tant que le défaut transversal n'est pas corrigé. | Contrôler le verrou d'écriture avant — ou indépendamment de — la validation des paramètres non pertinents pour l'action demandée. | `modify_schedule(action:"rename", scheduleId:76999, newName:"TEST", dryRun:true)` → `InvalidInput` sur `fieldNames`, verrou jamais consulté. |
| `modify_element` | majeur | Lève une exception non interceptée : `"An error occurred invoking 'modify_element'."`, sans `code`, sans `execution`, sans `success:false` structuré. Reproduit à l'identique sur 2 appels indépendants. | Retourner une erreur structurée comme tous les autres outils (`PermissionDenied` attendu ici, verrou fermé), avec `execution` et un code exploitable — voir le commit "Nommer l'outil dans les erreurs fourre-tout" du dépôt, qui visait précisément ce genre de trou. | `modify_element(action:"move", elementIds:[10047369], translation:{x:100,y:0,z:0}, dryRun:true)` → erreur brute, deux fois. |

## Synthèse (mise à jour) — bloc 2 clos

11 outils d'écriture échantillonnés. 9 confirmés en refus correct (`PermissionDenied`,
`writesAllowed:false`, dryRun sans effet sur le refus). 2 défauts majeurs : un outil
(`modify_schedule`) dont le refus attendu est masqué par le défaut transversal de
désérialisation ; un outil (`modify_element`) qui plante sans contrat d'erreur.

Total défauts cumulés : 1 critique (transversal, 9 occurrences), 4 majeurs
(`filter_by_parameter_value` en partie recouvert par le transversal, `modify_schedule`,
`modify_element`, et à requalifier), 3 mineurs (`get_schedule_data`, résolveur de
catégorie ×2).

Blocs 3-5 (dryRun, prévisualisations, écritures) hors périmètre de cette session :
nécessitent le verrou ouvert, décision qui reste humaine (bouton Écriture, ruban).

## Verrou ouvert — 2026-08-31 21:56:58 UTC (`changedBy: ribbon`)

État de référence avant blocs 3-5 : 22509 éléments, 119 avertissements
(`analyze_model_statistics`, `list_warnings`).

## Bloc 3 — contrat dryRun sur un outil sans prévisualisation

### DÉFAUT CRITIQUE CONFIRMÉ — régression du §3 du changelog 0.4.0

`add_shared_parameter` déclare `supportsDryRun: false` dans sa réponse d'exécution.
Le contrat annoncé par `get_server_capabilities.dryRun.whenUnsupported` est :
« `dryRun=true` is REFUSED with `InvalidInput` before execution — the tool is never
run, and the model is untouched ».

**Ce n'est pas ce qui s'est passé.**

Appel : `add_shared_parameter(parameterName:"TEST_RECETTE_0.4.0", categories:["Portes"], dryRun:true)`
Réponse : `success: true`, GUID émis, `execution.supportsDryRun: false`, aucune erreur.

Vérification indépendante par `list_shared_parameters` (outil de lecture, jamais
l'outil qui vient d'écrire) : **49 paramètres partagés au lieu de 48** avant l'appel.
`TEST_RECETTE_0.4.0` est bien présent, lié à la catégorie Portes, exactement comme
annoncé par la réponse — **la maquette a été réellement modifiée sous une demande
de prévisualisation.**

C'est le scénario exact que le changelog 0.4.0 §3 dit avoir corrigé pour toute la
surface : « le routeur refuse dryRun:true sur un outil qui ne le déclare pas, avec
InvalidInput, avant exécution ». Ici, le refus n'a pas eu lieu. `add_shared_parameter`
n'a manifestement pas été inclus dans le balayage qui a produit cette garantie, ou
la garantie ne tient plus après une régression.

**Gravité : critique — la plus grave de cette recette.** Un agent qui prévisualise
avant d'écrire, comme le SKILL le lui recommande, croit ne rien avoir changé alors
que la maquette porte une modification réelle et permanente (une définition de
paramètre partagé n'a pas d'outil de suppression dans cette surface).

**État de la maquette de recette :** modification permanente actée (49e paramètre
partagé `TEST_RECETTE_0.4.0`), conforme à l'usage prévu d'une maquette de recette
jetable — non annulée, non nettoyée.

**Reproduction :** `add_shared_parameter(parameterName:"TEST_RECETTE_0.4.0", categories:["Portes"], dryRun:true)` → `success:true`, `execution.supportsDryRun:false` ; confirmé par `list_shared_parameters` avant/après (48 → 49).

**Action requise pour le code :** balayer tout `RiveTT.Tools` (comme le fait
`DryRunDeclarationSourceTests` selon le changelog) pour vérifier qu'AUCUN outil à
`supportsDryRun:false` n'exécute réellement quand `dryRun:true` est passé. Ce test
existe déjà en théorie — cette recette prouve qu'il a un trou, ou qu'une régression
l'a contourné depuis.

**Deuxième confirmation, conséquence moindre :** `manage_view_display(action:"select", elementIds:[10047369], dryRun:true)`
→ `execution.supportsDryRun:false`, mais exécuté normalement (`"Selected 1 element(s)"`).
Conséquence ici sans gravité (sélection UI, `toolDestructive:false`, non persisté dans
le document), mais confirme que **le refus `InvalidInput` pour dryRun non supporté
n'est pas appliqué au niveau du routeur** — ni pour `add_shared_parameter` (écriture
réelle dans le document) ni pour `manage_view_display` (état UI éphémère). Le défaut
est structurel, pas isolé à un outil.

## Synthèse — bloc 3 clos

2 outils testés, tous deux en défaut : le refus `dryRun` documenté n'a jamais eu
lieu. **Défaut critique confirmé et généralisé.**

## Bloc 4 — prévisualisations, verrou ouvert

| Outil | Appel | `mutated` | Vérification indépendante | Verdict |
|---|---|---|---|---|
| `create_wall` | mur 5 m, RDC | `false`, `created:0` | `analyze_model_statistics.categoryBreakdown.Murs` inchangé (2248) | confirmé |
| `delete_element` | pièce Hall | `false` | cascade annoncée correctement (2 étiquettes dépendantes) ; catégorie Etiquettes de pièces inchangée (210) | confirmé |
| `set_element_parameters` | `ROOM_NAME` sur Hall | `false` | — | confirmé |
| `change_element_type` | porte → 240x200 | `false` | méthode `probe-and-rollback` documentée dans la réponse elle-même : l'opération s'exécute réellement puis est annulée, ids non réutilisables. Portes inchangé (365) | confirmé |
| `modify_element` | déplacer Hall | — | **plante à nouveau**, verrou ouvert cette fois — confirme que le défaut est indépendant de l'état du verrou, pas un artefact de l'ordre des contrôles | défaut (déjà consigné bloc 2) |

**Observation croisée :** `totalElements` 22509 → 22511 entre l'état de référence et
maintenant, alors qu'aucune catégorie du top 20 n'a varié. Cohérent avec le défaut
critique du bloc 3 : `add_shared_parameter` a réellement écrit une définition de
paramètre + sa liaison de catégorie, comptées comme éléments Revit hors des
catégories affichées. Pas un défaut supplémentaire — la trace du précédent.

## Synthèse — bloc 4 clos

4 outils de prévisualisation testés, tous conformes au contrat (`mutated:false`,
maquette intacte, vérifiée indépendamment). `modify_element` reste en défaut,
confirmé indépendant du verrou.

## Bloc 5 — écritures réelles, verrou ouvert

| Outil | Appel | Réponse | Vérification indépendante | Verdict |
|---|---|---|---|---|
| `create_wall` | mur 5 m, RDC, `dryRun:false` | `created:1`, id 11243477 | `filter_elements(filterCategory:"Murs")` : 2248 → **2249** | confirmé |
| `set_element_parameters` | `ROOM_NAME` → "TEST_RECETTE_ECRITURE" sur Hall | `modified:1` | `export_room_data(nameFilter:"TEST_RECETTE_ECRITURE")` : la pièce id 10047644, surface inchangée (49.12 m²) — le bon élément, pas un doublon | confirmé |
| `change_element_type` | porte 10094614 → 240x200, `dryRun:false` | `success:true` | `get_element_parameters(10094614)` : `elementName:"240x200"` — l'élément lui-même reflète le nouveau type | confirmé |
| `delete_element` | pièce Hall (avec cascade), `dryRun:false` | `deletedCount:3` (1 demandé + 2 étiquettes dépendantes) | `export_room_data` : 0 résultat ; `get_elements_by_unique_id` : uniqueId introuvable — cascade identique au nombre annoncé par l'aperçu du bloc 4 | confirmé |
| `save_document` | `dryRun:true` puis `dryRun:false` | aperçu : « save would likely fail (1 blocker) : locked by another process » ; réel : succès, taille de fichier modifiée (262586368 → 262946816 o) | **écart confirmé** : l'aperçu a prédit un blocage qui ne s'est pas produit | défaut |

## Défaut — bloc 5

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|
| `save_document` | mineur | L'aperçu (`dryRun:true`) rapporte un blocage « fichier verrouillé par un autre processus » et annonce un échec probable. L'appel réel immédiatement après réussit sans erreur. | Un aperçu qui annonce un échec doit correspondre à un échec réel, ou le blocage doit être vérifié plus précisément avant d'être rapporté. Moins grave que le sens inverse (aperçu silencieux sur une vraie écriture, voir bloc 3), mais nuit à la confiance : un agent qui lit ce blocage pourrait renoncer à sauvegarder alors que l'opération aurait réussi. | `save_document(dryRun:true)` → blocage annoncé ; `save_document(dryRun:false)` immédiatement après → succès, fichier réellement écrit. |
| `get_element_parameters` | mineur (annexe) | `parameterNames:["Type Name"]` explicitement demandé retourne `parameters: []` vide, sans passer par `unresolvedParameterNames` comme le contrat de localisation l'annonce pour un nom non résolu. | Soit résoudre "Type Name" (nom anglais standard), soit le signaler dans `unresolvedParameterNames`. | `get_element_parameters(elementIds:[10094614], parameterNames:["Type Name"])` → `parameters: []`, `unresolvedParameterNames: []`. |

## Synthèse finale

**Étape 0** : 6/8 exécutées, 5 confirmées, 1 non concluante (page ATTENTION jamais
observée faute de serveur réellement périmé) — et un défaut d'installation hors
périmètre du protocole découvert en cours de route (copie fantôme du serveur dans
le conteneur MSIX de l'application cliente, voir plus haut).

**Bloc 1 (lecture, verrou fermé)** : 28 outils. 19 confirmés, 2 non concluants.

**Bloc 2 (refus, verrou fermé)** : 11 outils d'écriture. 9 confirmés (dryRun
n'exempte jamais du refus). 2 défauts majeurs.

**Bloc 3 (contrat dryRun, verrou ouvert)** : 2 outils sans prévisualisation testés,
**les deux en défaut** — le refus `InvalidInput` documenté n'a jamais eu lieu.

**Bloc 4 (prévisualisations, verrou ouvert)** : 4 outils, tous conformes
(`mutated:false`, vérifié indépendamment).

**Bloc 5 (écritures réelles, verrou ouvert)** : 5 outils (dont sauvegarde), 4
confirmés avec vérification indépendante systématique, 1 défaut mineur.

### Défauts consolidés, par gravité

| # | Outil(s) | Gravité | Résumé |
|---|---|---|---|
| 1 | `add_shared_parameter`, `manage_view_display` (et probablement d'autres outils `supportsDryRun:false`) | **critique** | Le refus `InvalidInput` sur `dryRun:true` non supporté n'est jamais appliqué : `add_shared_parameter` **écrit réellement** dans la maquette sous une demande d'aperçu. Régression du §3 du changelog 0.4.0. |
| 2 | 9 outils : `get_current_view_elements`, `list_family_types`, `filter_by_parameter_value`, `get_room_openings`, `measure_between_elements`, `get_elements_in_spatial_volume`, `export_elements_data`, `manage_scope_boxes`, `manage_area_plans` | **critique** | Défaut transversal de désérialisation des paramètres tableau : omis ou vides, ils sont reçus comme chaîne littérale et refusés au lieu d'être traités comme absents. Bloque de la lecture pure sur 2 des 9. |
| 3 | `modify_element` | majeur | Erreur brute non structurée, aucun code, reproduite 3 fois (verrou fermé et ouvert) — indépendante du verrou. |
| 4 | `modify_schedule` | majeur | Le défaut n°2 masque le refus `PermissionDenied` attendu par le bloc 2 : le test de refus est structurellement invérifiable pour cet outil. |
| 5 | `find_untagged_elements`, `list_schedulable_fields` | mineur | Résolveur de catégorie incohérent avec le reste de la surface : refuse le français, accepte l'anglais. |
| 6 | `get_schedule_data` | mineur | 1ʳᵉ ligne de données duplique les en-têtes de colonnes. |
| 7 | `save_document` | mineur | Aperçu annonce un blocage qui ne se matérialise pas à l'exécution réelle. |
| 8 | `get_element_parameters` | mineur | Nom de paramètre non résolu (`"Type Name"`) retourne une liste vide au lieu d'être signalé dans `unresolvedParameterNames`. |

### État de la maquette de recette

Modifiée de façon permanente et volontaire (usage prévu) : +1 mur, +1 paramètre
partagé (résidu du défaut n°1, non supprimable par cette surface d'outils), 1 pièce
et ses 2 étiquettes supprimées puis le fichier sauvegardé. `Fichier test 0.4.0.rvt`
ne doit plus servir de référence "propre" pour une recette future sans le
retéléverser ou accepter cet état comme nouvelle base.

### Appelés / synthèse chiffrée

Environ 50 outils exercés sur ~198 recensés dans l'inventaire. Couverture
volontairement échantillonnée, pas exhaustive — chaque outil non appelé reste
`non testé`, jamais `confirmé` par défaut. Worksets hors périmètre (maquette non
partagée) ; options de conception, liens, phases, groupes couverts en lecture
seulement.
