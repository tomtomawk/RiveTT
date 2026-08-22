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

## Campagne de validation en session live — 2026-08-21, build 0.2.0

Menée sur `Saint-Malo_avenue aristide briand_46_V4.rvt` (bac à sable), Revit 2027,
document en français. Éléments de test supprimés en fin de campagne.

### Validé

| Test | Preuve |
|---|---|
| `create_document` (aucun document ouvert) | `MCP_TEST_0.2.0_nouveau_projet.rvt` créé depuis le gabarit, 6 niveaux, 4,1 Mo, 1,86 s |
| `open_document` à froid | V4 (217 Mo) ouvert et activé ; `get_project_info` renvoie ensuite ses 14 niveaux |
| `list_system_types` | 8 types d'escalier et 18 cartouches avec `typeId`, `categoryBic`, `instanceCount` — inaccessibles auparavant |
| `create_stair` | Escalier 11021980 : 18/18 contremarches, `reachesTopLevel: true`, giron 230 mm, contremarche 188,9 mm, largeur 1200 mm appliquée |
| `create_sheet` avec cartouche | Feuille 11022139, `hasTitleBlock: true`, **420 × 297 mm** (A3) au lieu du 210 × 297 par défaut ; instance de cartouche confirmée indépendamment par `place_title_block` |
| Unités explicites | `Hauteur d'escalier souhaitée` : `value 3.40` / `unit "meters"` / `internalValue 11.15` (pieds) |
| `execution.cached` | Second `get_project_info` identique : `cached: true` |
| `get_materials.nameFilter` | « béton » → 5 matériaux au lieu de 221 |
| `export_room_data.levelName` | « RDC » → `matchedCount: 23` au lieu des 138 pièces du modèle |
| `create_room_separation_line` | Ligne 11022146 créée dans le plan « RDC Travail » |
| `delete_element` | 260 éléments : `deletedCount` = ids listés = 4 demandés + 256 dépendances **nommées** (volées, marches, garde-corps auto) |
| Erreurs structurées | Aucune exception brute ; « No document open », « No UIApplication in session », rollback d'escalier avec le message Revit capturé |

### Deux bugs transverses découverts en direct, corrigés

| Bug | Détection | Portée |
|---|---|---|
| Un paramètre `bool?` fait échouer tout l'appel avant Revit | `list_system_types(category)` répond ; `list_system_types(category, includeLoadable: true)` échoue | 148 paramètres sur 110 outils, dont `dryRun` sur 15 outils d'écriture |
| Un paramètre tableau **optionnel** échoue de même | `get_element_parameters(elementIds:[…])` (tableau requis) répond ; ajouter `parameterNames:[…]` casse le même appel | 55 paramètres sur 41 outils : plus aucun filtre de catégorie, d'ids ni de liste de champs |

Ces deux défauts sont antérieurs aux ajouts de cette série et expliquent
vraisemblablement une partie des « An error occurred invoking » inexpliqués du
rapport de campagne. Deux bugs latents ont été trouvés au passage :
`batch_rename` transmettait les **caractères** de la chaîne d'ids, et
`filter_by_parameter_value` publiait `elementIds` sans jamais le transmettre.

### Corrigé après observation

- `create_stair` : l'avertissement inversait le sens (27 contremarches pour 18
  = volée trop **longue**, il conseillait de l'allonger) ; il donne maintenant le
  sens et la correction en mm.
- `create_stair` : « already has associated railings » n'est plus une erreur —
  la plupart des types créent leurs garde-corps, l'outil retourne leurs ids.
- `create_stair` : le message de rollback nomme les deux causes réelles, dont les
  types préfabriqués catalogués à hauteur fixe (Revit ne dit que « Impossible de
  créer l'escalier »).
- `get_element_parameters` en mode `compact` supprimait `unit` — exactement
  l'ambiguïté que le travail sur les unités visait à supprimer.
- La session perdait l'`UIApplication` à chaque fermeture de document, ce qui
  rendait `create_document`/`open_document` inutilisables jusqu'à réouverture
  manuelle d'un fichier.

### Reste à valider (nécessite la build suivante)

`export_elements_data` avec noms de paramètres anglais et avec `elementIds`,
`get_elements_in_spatial_volume` avec `containment: boundary`, et
`edit_group_members` — tous trois bloqués par le bug des tableaux optionnels
jusqu'à réinstallation.

## Modèle de démonstration — 2026-08-22

`Bureau\MCP_Vitrine_2027.rvt` + `Bureau\Feuille-Niveau 0 - Plan et vue 3D.pdf`,
produits intégralement par appels MCP depuis le gabarit architectural français,
sans aucune intervention dans l'interface Revit.

Enchaînement : `create_document` (gabarit `.rte`, activation) → 5 murs
(extérieurs contraints Niveau 0→1 + cloison) → 3 dalles (RDC, étage, balcon) →
2 portes + 3 fenêtres (`zMode: relativeToLevel`, **9 poses sur 9 du premier
coup**) → escalier 16/16 contremarches `reachesTopLevel: true` avec ses 2
garde-corps automatiques → garde-corps de balcon → ligne de séparation de pièces
→ 3 pièces délimitées (17,3 / 17,3 / 22,6 m²) + étiquettes → nomenclature créée
avec des **noms de champs anglais sur document français** (`Number, Name, Area,
Level` → `Numéro, Nom, Surface, Niveau`, 4/4) → informations projet → vue 3D →
type de mur dupliqué + structure composite 320 mm (matériau inventé **refusé**,
matériaux réels acceptés) → lignes de modèle et de détail → feuille A1 avec
cartouche (`hasTitleBlock: true`) + 2 vues portées → export PDF → enregistrement.

Un escalier trop long a été supprimé et refait : 49 éléments retirés
(1 demandé + 48 dépendances nommées : volées, marches, garde-corps, barreaux,
esquisses), décompte cohérent.

### Défauts trouvés pendant la démonstration, corrigés

| Défaut | Correction |
|---|---|
| `create_view` refusait `ThreeD`, la valeur que sa **propre description** publie (seul `3d` passait) | alias `ThreeD`/`threeDimensional` acceptés, message d'erreur listant les valeurs |
| `create_text_note` échouait sur une largeur hors plage (« The given width is not valid ») sans donner les bornes | largeur bornée à la plage du type de texte, écart signalé en mm dans `warnings` |
| `batch_export` annonçait `Niveau 0 - Plan et vue 3D.pdf` alors que Revit écrit `Feuille-Niveau 0 - Plan et vue 3D.pdf` | le fichier réellement écrit est retrouvé sur le disque et rapporté |
| `manage_model_groups` ignorait `groupTypeId` : 20 types renvoyés pour 1 demandé | filtre appliqué |
| `create_railing` ne documentait pas sa convention d'altimétrie | description explicite : les `z` du chemin doivent seulement être égaux, `baseLevelId` fait foi (comme `create_wall`) |
| Mettre à jour le plugin obligeait à fermer le client MCP (exe verrouillé) | `install.ps1` renomme le fichier verrouillé — Windows l'autorise — et écrit le neuf à sa place ; il suffit de reconnecter le serveur MCP ensuite |

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
