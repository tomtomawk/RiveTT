# RiveTT 0.6.0

Version issue du retour d'expérience de la session de test des 23–24/09/2026 : projet
test (plots R+2 / R+3, 14 logements) créé avec RiveTT 0.5.4 sur Revit 2026, gabarit
`club_GABARIT 2026.rte`, piloté par un agent Claude. Les dix documents du retour
d'expérience sont archivés dans [`retex/2026-09-24/`](retex/2026-09-24/00_README.md) ;
les numéros ci-dessous (« 04 §1.1 ») y renvoient.

Le constat central : l'agent a travaillé **à l'aveugle** (aucune image ne lui
parvenait), **de mémoire** (la charte lue une fois, appliquée à la troisième version des
plans) et **en script** (`send_code_to_revit` pour ~60 % des appels, chaque erreur
renvoyant 150 à 300 lignes). Cette version s'attaque aux trois.

## 1. Défauts corrigés

### Signalés par le retour d'expérience (04 §1)

| Réf. | Défaut | Correction |
|---|---|---|
| 1.1 | `create_stair` à deux volées : 2ᵉ volée au sol, 9 contremarches au lieu de 17, pas de palier, alors que le dry-run annonçait « 1 palier, 17 contremarches » | Chaque volée part du haut de la précédente (`CreateStraightRun` lit le Z de la ligne comme base de volée, en coordonnées modèle). Palier vérifié par `CanCreateAutomaticLanding` ; un palier impossible **annule l'escalier** avec la géométrie en cause (`requireLandings: false` garde les volées). Le commit de l'edit scope est vérifié : un escalier annulé par Revit n'est plus rapporté comme créé. Le dry-run estime les contremarches volée par volée, refuse une jonction qu'aucun palier ne franchit, et liste ce qu'il ne vérifie pas (`notVerified`). La réponse donne les altitudes et contremarches réelles de chaque volée. |
| 1.2 | `send_code_to_revit` en mode `manual` : « transaction déjà ouverte », `StairsEditScope` impossible | `manual` (alias de `none`) n'ouvre plus de transaction. Toute valeur inconnue de `transactionMode` est refusée : elle retombait sur `auto`. |
| 1.3 | Rollback tout-ou-rien, « Impossible de couper l'occurrence » ×77 sans identifiant ni ligne | Messages regroupés avec leur nombre et les identifiants en cause (`errorGroups`), pour tous les outils. Une exception de script donne sa **ligne** (PDB portable). `Section(...)` valide un script par blocs. |
| 1.4 | `c.GetType().Name` refusé comme réflexion | Le motif prenait la parenthèse fermante de `GetType()` pour un argument. `GetType("…")` reste refusé. |
| 1.5 | Verrou au démarrage : même un script en lecture refusé | `transactionMode: "readonly"` est admis verrou fermé : groupe de transactions **toujours annulé**, appels hors maquette (enregistrement, export, ouverture, changement de vue) refusés d'avance. |
| 1.6 | `get_selected_elements` sans géométrie : quatre lignes fermées lues comme un axe | Géométrie en mm (courbes, points, boîtes), `categoryBic`, type, niveau, et `curveAnalysis` qui distingue contour fermé et ligne ouverte. `limit` publié ; `selectedCount` compte toute la sélection, plus la page rendue. |
| 1.7 | `batch_export` IMAGE : le PNG reste sur un disque que l'agent n'atteint pas | Nouvel outil `capture_view` (§2). |

### Trouvés en corrigeant

- **`transactionMode: "readonly"` validait les modifications du script.** La valeur était
  publiée sur la surface MCP et tombait sur la branche `auto`. C'est le défaut le plus
  grave de cette liste : un appelant qui demandait une lecture obtenait une écriture.
- **Mobilier « volant » (04 §2.2).** `create_point_based_element` posait une famille sur
  niveau avec un Z absolu, que Revit a compté deux fois (+2,89 m au R+1). L'altitude est
  désormais mesurée après pose et corrigée (`zCorrectedByMm`) ; une famille posée à 1 m ou
  plus sous son propre niveau est refusée (z relatif envoyé en absolu).
- `create_point_based_element` avec un `hostWallId` invalide annonçait « auto-détection »
  et posait l'élément **sans hôte** : c'est maintenant un échec de l'élément.
- `tag_rooms` cherchait un type d'étiquette, ne l'appliquait jamais, et ignorait
  silencieusement les `roomIds` introuvables.
- La CI comparait l'inventaire à `references/inventaire-des-outils.md`, fichier supprimé :
  le contrôle « inventaire à jour » ne vérifiait rien. Il compare maintenant `SKILL.md`,
  ligne de date exceptée.
- `SKILL.md` annonçait que seul `System.Reflection.Emit` était refusé : c'est tout
  `System.Reflection`.
- `batch_export` IMAGE annonçait `<vue>.png` alors que Revit ajoute le type et le nom de
  la vue : le fichier rapporté est désormais celui qui a été écrit.
- L'image de `test_image_relay` coupait le « 2 » de « TEST 42 » (vu au premier essai de
  bout en bout) : largeur portée à 360 px, test ajouté.

### Relevés par la revue de code de cette version

Une relecture indépendante du lot a trouvé, avant livraison :

- **quatre façons de contourner le verrou avec `transactionMode: "readonly"`** : un
  gestionnaire `Idling` (ou un updater, un `ExternalEvent`) qui écrit après l'annulation ;
  une écriture dans un autre document ouvert, hors du groupe annulé ; du code caché dans
  les trous `{…}` d'une chaîne interpolée, que le filtre texte effaçait avec la chaîne ; un
  groupe de méthodes (`document.Close` sans parenthèses). Le mode `readonly` est désormais
  vérifié **sur l'arbre compilé et ses symboles** (`ReadOnlyScriptAnalyzer`) : événements,
  exécution différée ou parallèle, `Application.Documents`, appels et accesseurs hors
  maquette, code hors du corps du script. Le document de l'interface est aussi annulé s'il
  diffère. Le filtre texte garde maintenant les trous d'interpolation comme du code — ce qui
  ferme aussi le contournement de `System.IO` par `$"{…}"`, antérieur à cette version ;
- `capture_view` : un cadrage non rectangulaire ignorait `bboxMm` sans le dire, et une marge
  symétrique pouvait passer pour une correspondance exacte ; elle n'est plus jamais déclarée
  vérifiée (`aspectMatchesCrop`, `verified: false`, étalonnage par `highlightIds`) ;
- les poteaux et gaines d'une pièce (boucles intérieures) sont des obstacles et sont
  retirés de la surface ; le cercle libre est affiné sous le pas de grille au lieu d'être
  accepté à 28 mm près ; « Ch1 », « CH.2 » sont des chambres ;
- `create_stair` estime avec le type par défaut du document, celui que Revit utilisera ;
  `FindHostWall(Pt(x, y), niveau)` trouve le mur d'un étage malgré z = 0 ;
  `get_selected_elements` analyse les contours sur toute la sélection, pas sur la page ;
- `create_point_based_element` refuse un `familyName` ou un `typeName` seul au lieu de
  poser le premier type de la catégorie ; `validate_dwelling` refuse `targetAreaM2` sans
  logement ; le bloc `execution` compacté garde toujours `documentTitle` et
  `revitProcessId`, le processus serveur pouvant survivre à la conversation.

## 2. Nouveaux outils

| Outil | Rôle | Réf. |
|---|---|---|
| `capture_view` | Rend une vue en PNG/JPEG et la renvoie **comme image MCP**, avec un bloc texte : vue, taille, correspondance pixels ↔ mm (`aspectMatchesCrop` ; jamais déclarée vérifiée sans étalonnage par `highlightIds`). Options appliquées à une copie temporaire dans un groupe annulé : `bboxMm`, `highlightIds`, `isolateCategories`, `detailLevel`, `displayStyle`. `returnMode: file` en repli. Lecture seule. | 03 |
| `test_image_relay` | Image de test « TEST 42 », sans Revit : dit si le client MCP relaie les images. | 03 §6.1 |
| `validate_spec` | Contrôle un logement **décrit en JSON, avant construction**, sans Revit, contre les règles de la charte. Traité par le serveur seul. | 05, 08 |
| `validate_dwelling` | Mêmes règles sur les logements **construits** d'un niveau (pièces groupées par `ARC_PAR_NUMERO_LOGEMENT`). `includeSpec` rend chaque logement en JSON : un logement livré devient un plan type. Lecture seule. | 02 §3.5, 08 §5 |
| `describe_family` | Origine, orientation, règle de Z et emprises en coordonnées famille : visible, complète, plan, boîte englobante — avec les écarts signalés. Lecture seule. | 02 §3.3, 06 |
| `place_in_room` | Pose une famille sur niveau dans une pièce par son **emprise visible** : centrée, plaquée aux murs N/S/E/O, gardée dans la pièce, collisions rapportées. | 02 §3.4, 06 §3 |
| `attach_walls` | Attache ou détache des murs à un toit, sol, plafond, toposolide ou mur, une transaction par mur, en nommant les éléments en cause de chaque refus. | 02 §3.7, 04 §2.4 |

Le moteur de règles (`RiveTT.Core.Design`) est commun à `validate_spec` et
`validate_dwelling` : les deux ne peuvent pas diverger sur une même règle. Le serveur MCP
référence désormais `RiveTT.Core`, qui n'a aucune dépendance à l'API Revit. Chaque
contrôle porte sa **source** : *Réglementaire* (arrêté du 24/12/2015, à vérifier sur le
texte en vigueur), *Charte agence*, *Pratique agence* (constat de la session, à confirmer
par l'agence). Une règle qui ne peut être évaluée faute de données est rapportée comme
telle (`notEvaluated`), jamais comme réussie ; `compliant: true` l'exige.

## 3. `send_code_to_revit`

- Modes : `auto`, `none` (alias `manual`), `group`, `readonly` ; les autres valeurs sont refusées.
- Le dry-run compile le script : erreurs de syntaxe et refus du mode `readonly` reviennent
  avant toute exécution.
- Aides injectées, longueurs en mm : `Mm`, `ToMm`, `Pt`, `Log`, `LevelByName`,
  `TypeByName<T>` (exact, puis sans casse, puis préfixe unique, choix journalisé — 04 §2.5),
  `SymbolByName`, `FindHostWall`, `PlaceOnLevel` (altitude mesurée et corrigée),
  `PlaceInWall` (Z absolu), `Section` (02 §4, points 2 et 3).
- `Autodesk.Revit.DB.Architecture` et `.Structure` importés (04 §2.10).
- `scriptArgs` (JSON) et `fromScript` + `edits` : rejouer un script enregistré avec d'autres
  valeurs, ou en corriger une ligne sans le renvoyer (02 §4 point 1). Le script est
  enregistré même en échec ; l'audit trace le texte réellement exécuté.
- `scriptRun` dans la réponse : mode, `modelKept`, journal, sections, et identifiants
  créés / modifiés / supprimés (`DocumentChanged`).
- Valeurs Revit sérialisées compactement : `Element` → id, nom, `categoryBic` ; `XYZ` en
  pieds avec `unit: "ft"` (02 §4 point 8).
- Le script compilé est chargé dans le contexte d'assemblage du plugin, pour que les aides
  et l'API Revit soient les mêmes instances que celles du plugin.

## 4. Lots et réponses

- `create_point_based_element` et `create_line_based_element` : `key` par élément, renvoyé
  dans `details` et dans `failed[]` (02 §3.2). Le premier accepte aussi `levelName`,
  `familyName` + `typeName` et `findHost: true`.
- `tag_rooms` : `tagTypeId` / `tagTypeName`, `roomNameContains`, `onePerParameter` (une
  étiquette de typologie par logement), `createdTagIds`, pièces sautées avec leur raison.
- Tout aperçu porte `previewLimits` : ce qu'il n'a pas pu voir (02 §5). Un aperçu par
  exécution puis annulation ne passe pas par le commit, où Revit contrôle jointures,
  attaches et découpes.
- Bloc `execution` : connecteur, versions et mode ne sont renvoyés que lorsqu'ils changent,
  remplacés sinon par `sessionUnchanged: true` (02 §5, 09 §6). Jamais sur un écart de
  versions, un changement de cible ou `get_project_info` / `get_server_capabilities` /
  `ping_revit`. `documentTitle` et `revitProcessId` restent sur chaque réponse : le
  processus serveur peut survivre à la conversation qui les a vus.
- Les instructions du serveur MCP — seul texte que tout client lit à coup sûr — portent une
  carte des outils par tâche : en session, 7 outils sur ~200 avaient servi, faute d'avoir
  cherché les autres (02 §2).

## 5. Documentation

- `SKILL.md` : carte des outils par tâche en tête ; règles 13 à 15 (regarder, concevoir
  avant de construire, mesurer une famille avant de la poser) ; section « Concevoir et
  contrôler un logement » (méthode en phases, schéma JSON, règles, familles du gabarit) ;
  pièges de l'API Revit rencontrés ; modes et aides de `send_code_to_revit` ; contrat de
  réponse complété. Inventaire régénéré : 207 outils publiés, 202 classes runtime.
- Points à vérifier en session réelle : `CHANGELOG_0.3.0.md` §6, complément 0.6.0.

## 6. Compatibilité

- Revit 2026.5+ et 2027 compilent depuis la même base ; toutes les API employées existent
  en 2026 (`Wall.AddAttachment` et `GetAttachmentIds` : depuis 2026).
- **Changements de comportement** :
  - `transactionMode` inconnu refusé ; `readonly` réellement en lecture seule ;
  - `send_code_to_revit` : `code` n'est plus obligatoire (remplacé par `fromScript`) ; la
    variable `scriptArgs` est réservée dans les scripts ;
  - `get_selected_elements` rend la géométrie par défaut (`includeGeometry: false` pour
    l'ancien volume de réponse) ;
  - `create_point_based_element` : un `hostWallId` invalide et une famille sur niveau
    posée à 1 m ou plus sous ce niveau font échouer l'élément ;
  - `tag_rooms` avec un type explicite étiquette une pièce déjà étiquetée d'un autre type ;
  - le bloc `execution` est compacté (voir §4).
- Serveur et plugin passent en 0.6.0 ensemble : installer les deux moitiés
  (`execution.versionMismatch` le signale sinon).

## 7. Non traité, et pourquoi

| Demande du retex | État |
|---|---|
| `build_from_spec` (08 §5) | Non livré : chaque sous-étape (murs, pièces, portes, équipements) doit d'abord être prouvée sur maquette. Les briques existent : lots avec `key`, `place_in_room`, `validate_spec` / `validate_dwelling`. |
| `render_spec` (08 §5), schéma SVG | Non livré ; `validate_spec` rend les coordonnées de ce qui échoue (centres de cercles, rectangles). |
| `edit_toposolid` (noue, talus — 02 §3.6), `create_shaft` (gaines palières) | Non livrés : géométrie à valider en session. |
| `capture_view` : `showIds`, repli par URL signée R2 (03 §7) | Non livrés. Le repli publie des images de projet sur un stockage externe : décision de l'agence. `returnMode: file` couvre le cas local. |
| Règles de niveau : ascenseur unique, circulations communes, gaines, portes-fenêtres sur balcon (05 §5-6) | Hors `validate_dwelling` ; à traiter par un contrôle de niveau. |
| Garde-fou « document de production », marquage `IA_SESSION`, journal des scripts par projet (09 §1-2) | Décisions d'organisation de l'agence, non tranchées ici. `scriptRun.changes` donne déjà les ids créés par un script. |
| Incendie, acoustique, structure, RE2020, réseaux (09 §5) | Hors périmètre de cette version. |

## 8. Vérification

    dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release                     760 réussis, 3 ignorés
    dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release -p:RevitVersion=2026 760 réussis, 3 ignorés
    dotnet build .\RiveTT.sln -c Release (2027 et -p:RevitVersion=2026)               sans erreur
    python tools/audit-tool-surface.py                                                  0 défaut confirmé

`builderuild.ps1 -SkipInstaller` : les deux cibles, les tests et la charge utile
(80,8 Mo) passent. Le serveur publié a été piloté de bout en bout par stdio, sans Revit :
poignée de main MCP (version 0.6.0.0, carte des outils dans les instructions), 207 outils
listés, `test_image_relay` reçu comme bloc image et lu (« TEST 42 »), `validate_spec` sur un
objet JSON natif, `capture_view` sans session Revit rendu comme erreur `NoRevitSession`.

Les tests qui touchent l'API Revit ont tourné contre les DLL réelles de Revit 2026 et 2027
installées sur le poste (compilation d'un script contre l'API réelle, notamment) ; rien de
ce qui dépend d'un document ouvert n'a pu être exécuté hors session. Aucune validation live
n'a été faite : voir la liste du §6 de `CHANGELOG_0.3.0.md`.
