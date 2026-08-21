# Traitement de la campagne de tests — corrections apportées

Réponse point par point à [MCP_AGENT_IMPROVEMENTS.md](MCP_AGENT_IMPROVEMENTS.md)
(campagne des 2026-08-20 et 2026-08-21, Revit 2027, modèle francophone).

Chaque ligne indique la **cause racine réelle** trouvée dans le code, la
correction, et le test qui empêche la régression. Les causes diffèrent parfois
de l'hypothèse du rapport : elles sont signalées explicitement.

## Bloquants

| Anomalie constatée | Cause racine | Correction | Garde-fou |
|---|---|---|---|
| `create_sheet.titleBlockId` totalement ignoré : feuille A4 sans cartouche, aucune erreur | Contrat rompu entre les deux moitiés du connecteur : le serveur MCP envoyait `titleBlockId`, `CreateSheetTool` lisait `titleBlockTypeId` | Les deux noms sont acceptés ; un `titleBlockId` inutilisable devient une erreur `InvalidInput` listant les cartouches disponibles au lieu d'un repli silencieux ; la réponse porte `hasTitleBlock`, `titleBlockFamily/Type`. Nouvel outil `place_title_block` pour réparer une feuille existante | `ServerRuntimeParameterContractTests` (2 tests dédiés) |
| `export_elements_data.elementIds` sans effet : renvoie les 100 premiers éléments du modèle | Le paramètre n'existait ni côté MCP ni côté runtime | `elementIds` ajouté et appliqué **avant** toute pagination ; `notFoundIds` retourné | Test d'ordre `CollectById` avant troncature |
| Impossible d'énumérer un type système (garde-corps non posé sur le balcon du T2) | `get_available_family_types` n'énumérait que `FamilySymbol` + 5 classes système codées en dur | Nouvel outil `list_system_types(category)` (inventaire par catégorie si aucune catégorie) ; `get_available_family_types` parcourt désormais tous les `ElementType` et retourne `kind: loadable\|system` | — |
| Aucun outil pour tracer une ligne (détail, modèle, séparation de pièces) | `create_line_based_element` exige un `FamilySymbol`, inexistant pour ces catégories | Nouveaux outils `create_detail_line`, `create_model_line`, `create_room_separation_line` (via `Autodesk.Revit.Creation.Document` + `SketchPlane`) | — |
| `get_element_parameters` / `get_element_solid_geometry` en échec après `save_as_document` | Non reproductible hors session live ; le mécanisme adjacent était bien fautif : cache non invalidé sur Save As | `DocumentSavedAs` désormais souscrit, invalidation totale (Session incluse), plus invalidation systématique après toute écriture de cycle de vie côté routeur. Toute erreur d'outil reste structurée | `SaveAs_InvalidatesEveryCachedRead` |

## Silences trompeurs

| Anomalie constatée | Cause racine | Correction | Garde-fou |
|---|---|---|---|
| `Mark`, `Level`, `Width`, `Height`, `Type Name` : colonnes vides sans avertissement | Comparaison au **nom d'affichage localisé** uniquement (`LookupParameter`) | `ParameterNameResolver` : `BuiltInParameter` → nom exact → table d'alias EN/FR → correspondance sans casse ni accents. Branché aussi dans `ParameterLookup`, donc `set_element_parameters`, `filter_by_parameter_value`, `bulk_modify_parameter_values`, `add_prefix_suffix`, `clear_parameter_values`, `sync_csv_parameters` | `NameMatchingTests` |
| `create_schedule` : `NotSchedulableForCategory` alors que le champ existe en français | Même cause ; le message accusait une limitation Revit inexistante | `SchedulableFieldResolver` distingue `ParameterNameNotFound` de `NotSchedulableForCategory`, avec `explanation` et suggestions | idem |
| Filtre `Niveau equals RDC` : 0 résultat sur 138 pièces | Le filtre comparait `"RDC"` à l'**ElementId numérique** du niveau | Les paramètres de type `ElementId` sont comparés sur le nom **et** l'id ; `is_empty`/`is_not_empty` fonctionnent enfin (ils exigeaient une `filterValue`) | — |
| `get_materials.nameFilter` ignoré (221 matériaux renvoyés) | Le paramètre n'existait pas sur la surface MCP, le runtime le supportait déjà | `nameFilter` et `materialClass` exposés | `ServerRuntimeParameterContractTests` |
| `get_available_family_types` avec `compact: true` : `count: 0` systématique | Le shaper client supposait un tableau JSON nu ; le routeur enveloppe (`{value:[…]}`) et, sur échec, l'erreur était **transformée en `count: 0`** | Le shaper ne met plus jamais en forme un échec et reconnaît les trois enveloppes ; le runtime retourne `{count, totalCount, truncated, items}` | 3 tests de shaper |
| `set_compound_structure` : couches créées avec `materialName: "(none)"` | Nom de matériau introuvable → `ElementId.InvalidElementId` silencieux | Échec explicite avec suggestions de matériaux réels | — |
| `create_room` : pièce non délimitée créée sans avertissement | Aucune lecture de l'aire après création | Réponse avec `enclosed`, `areaM2`, `warnings` ; `dryRun` signale la pièce qui occupe déjà le point | — |
| `get_element_parameters` sur IDs supprimés : « Retrieved parameters for 4 elements » | Le shaper `compact` supprimait le champ `error` de chaque ligne | `found: false`, `notFoundIds`, `foundCount`/`notFoundCount` ; le shaper conserve `found`/`error` | `ShapeGetElementParameters_KeepsFoundAndErrorForMissingIds` |
| `export_schedule.format`, `export_shared_parameter_file.outputPath`, `get_current_view_elements.categoryFilter`, `load_selection.action` | Même classe de bug que `titleBlockId` : paramètre publié, jamais lu | Alias acceptés côté runtime ou paramètre retiré de la surface ; `get_current_view_elements` résout les catégories comme les autres outils | `EveryPublishedParameter_IsReadByTheRuntimeToolItIsSentTo` |

## Ergonomie et lisibilité des réponses

| Anomalie constatée | Correction |
|---|---|
| `Surface: 122.81` en pieds² lu comme des m² | Toute valeur numérique porte `value` (unités du projet), `unit`, `internalValue` (unités internes Revit) et `displayValue` ; `unitPolicy` rappelé dans la réponse et dans `get_server_capabilities` |
| `execution.readOnly` compris comme un verrou global du serveur | Renommé `toolReadOnly` / `toolDestructive`, plus `writesAllowed` (toujours vrai) et `cached`. `get_server_capabilities` documente les quatre champs et affirme `readOnlyModeExists: false` |
| Réponse obsolète servie en 0 ms sans indication | `execution.cached: true` sur tout succès servi par le cache |
| `deletedCount: 2` mais un seul élément listé | Sonde du cascade dans une `SubTransaction` annulée : `deletedElementIds` complet, `requestedElements` + `cascadedElements` nommés, `deletedCount` cohérent |
| Catégorie « Fenêtres » retournée pour un viewport | Ce n'est pas un bug : Revit FR nomme la catégorie `OST_Viewports` « Fenêtres » (avec espace final). Les réponses portent désormais `categoryBic` (code `OST_*`), non ambigu |
| `save_document` / `save_as_document` sans `dryRun` malgré `dryRunDefault: true` | `dryRun` réel : chemins, existence de la cible, politique d'écrasement, accessibilité du dossier, verrou fichier, modifications non enregistrées, blocages prévisibles |
| Mauvais nom de paramètre → exception .NET brute | `save_as_document` accepte `filePath`/`path` en alias et, sans cible, retourne un `InvalidInput` nommant `targetPath` |
| `get_schedule_data` : `availableFields` (des centaines d'entrées) toujours renvoyé | `includeAvailableFields: false` par défaut, `availableFieldCount` conservé |
| Export volumineux impossible à dimensionner | `export_elements_data.countOnly` |
| 9 échecs de pose de porte/fenêtre avec `z: 0` | `zMode: absolute\|relativeToLevel` sur `create_door`/`create_window` ; contrôle préalable de l'insertion : plage verticale de l'hôte et largeur de l'ouverture comparée à la longueur du mur, en mm. Convention documentée dans chaque description |
| `duplicate_system_type` suggérant `get_available_family_types` | Suggestion corrigée vers `list_system_types(category)` |
| Escalier, nouveau document, propagation d'armatures : recherche vaine dans le catalogue | `get_server_capabilities.lifecycleLimitations` les déclare, avec la raison technique ; `discoveryHints` explique les types système, l'équivalent de « créer similaire » (`copy_elements`) et les lignes de séparation |

## Ajouts issus des demandes du rapport

| Demande | Réalisation |
|---|---|
| Mode « éléments de contour d'une pièce » pour `get_elements_in_spatial_volume` | `containment: "inside" \| "boundary"`. Le mode `boundary` s'appuie sur `Room.GetBoundarySegments` (les segments de Revit, pas une approximation géométrique) et retourne murs, poteaux et lignes de séparation avec la longueur de contour qu'ils fournissent. Chaque volume indique `geometryUsed` (`roomSolid`, `boundingBox` ou `roomBoundarySegments`) : la différence entre les deux algorithmes explique la plupart des résultats surprenants |
| Filtre de niveau natif sur `export_room_data` | `levelName` (insensible à la casse et aux accents), `levelId` et `nameFilter`, filtrés dans Revit ; `matchedCount` indique le nombre de correspondances avant troncature |

## Limitations levées après vérification de la documentation Autodesk

Deux limitations déclarées par le connecteur étaient fondées sur une lecture
trop large de la contrainte Revit. Vérification faite, elles ne s'appliquaient
pas au contexte d'exécution de ce connecteur.

| Limitation déclarée | Réalité | Implémentation |
|---|---|---|
| « `OpenAndActivateDocument` ne peut pas tourner dans un gestionnaire d'événement API » | Vrai pour les **événements API** (`Idling`, `DocumentChanged`), faux pour un **ExternalEvent** — le contexte de chaque outil ici. Position Autodesk (Arnošt Löbel) : passer par un External Event est « both supported and safe » | `open_document(filePath, detachFromCentral?, dryRun)` ; `create_document(templatePath?, targetPath, activate?, overwrite?, dryRun)` via `Application.NewProjectDocument` (document en mémoire, sauvegardé puis fermé) |
| « L'escalier standard passe par un éditeur d'esquisse modal » | Vrai pour l'escalier **esquissé** uniquement. L'escalier **par composant** se construit avec `StairsEditScope`, qui est une portée d'édition API sans aucune UI (comportement de `TransactionGroup`) | `create_stair(baseLevelId, topLevelId, runs, stairsTypeId?, widthMm?, railingTypeId?, dryRun)` : volées droites, paliers automatiques, garde-corps optionnel, `scope.Commit(preprocessor)` pour qu'aucun avertissement n'ouvre de dialogue modal |

`edit_group_members` complète la série, mais sans lever la limitation : l'API
Revit **ne permet pas** de modifier les membres d'un groupe en place (position
Autodesk confirmée). L'outil applique le seul contournement supporté
— dégrouper / modifier / regrouper — et refuse par défaut un type à plusieurs
occurrences, puisque Revit ne peut pas propager le changement.

## Non retenu

- **Ouverture du document de famille (`Document.EditFamily`).** Le dépôt
  consigne un interblocage constaté depuis ce dispatcher ; le risque est un gel
  de la session Revit de l'utilisateur. Chemin supporté : éditer le `.rfa` hors
  Revit puis `load_family`.
- **Escaliers esquissés, volées hélicoïdales, balancements.** `CreateSketchedRun`
  et `CreateSpiralRun` existent dans l'API : extension possible plus tard, non
  nécessaire pour une circulation verticale standard.

## Vérification

Version du connecteur : **0.2.0** (plugin et serveur MCP).

    dotnet build .\RevitCortex.sln -c Release
    dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c Release
    .\build.ps1

Les corrections ci-dessus n'ont pas pu être rejouées contre une session Revit
2027 live depuis cet environnement : elles sont couvertes par la compilation et
par la suite de tests (452 tests). Une nouvelle passe manuelle sur le modèle de
test reste nécessaire pour valider le comportement en session, en priorité
`create_sheet` avec cartouche, `export_elements_data` avec `elementIds` et noms
de paramètres anglais, et une lecture juste après `save_as_document`.
