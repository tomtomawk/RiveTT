# Plan de correction — RiveTT 0.2.0

Source des constats : campagne du 26/08/2026 sur Revit **2027.2**, gabarit d'agence
`club_GABARIT 2026.rte` (`docs/developpement/PROTOCOLE_TEST.md`), complétée par un audit
de relecture de code mené séparément.

Chaque entrée porte son **statut de preuve** :

- **mesuré** — reproduit en session Revit, appel et réponse consignés ;
- **localisé** — défaut confirmé par lecture du code, ligne citée, non exercé en session ;
- **hypothèse** — plausible, non établi ; l'action est de trancher, pas de corriger.

Rien de ce qui suit n'a été corrigé pendant la campagne.

## État — branche `dev/0.3.0`

Corrigés (code compilé, tests unitaires verts ; non revérifiés en session Revit live,
aucune n'étant disponible sur ce poste au moment des correctifs) : **P0.1, P0.2, P1.1,
P1.3, P1.5, P1.6, P1.7, P2.1, P2.2, P2.3, P2.4, P2.5**, et le correctif documentaire de
**P4.1** (l'affirmation d'interblocage d'`EditFamily`).

Non corrigés, avec raison :

- **P0.3, P1.2** — le code actuel contredit le défaut décrit (voir les notes ajoutées
  dans chaque section). À revérifier en session Revit avant de rouvrir ou de refermer.
- **P1.4** — mesuré le 27/08 en session Revit réelle, **non reproduit** (voir la
  section : protocole exact, réserve sur sa couverture, et une trouvaille distincte —
  `get_element_parameters(parameterNames:[...])` casse sur un tableau JSON natif,
  candidat pour une partie des erreurs opaques de P0.3).
- **P3.1, P3.2** — hygiène, le plan demande une passe dédiée séparée.
- **P4.1** — reste à construire : `open_template`, `open_family`, `close_document`,
  `edit_family` en arrière-plan. Plus bloqué (P1.7 fait), taille d'un lot séparé.
  Constat en passant (P1.4) : `Document.Close(false)` refuse le document actif, donc
  `close_document` devra d'abord activer un autre document — pas un détail mineur.

---

## P0 — Bloquants

### P0.1 `create_stair` : la ligne de volée est construite à z = 0 absolu

**Statut : mesuré + localisé. Cause racine établie.**

`src/RiveTT.Tools/Elements/CreateStairTool.cs:318-333` — `TryReadRuns` construit les deux
extrémités de la volée avec un `z` codé en dur à `0` :

```csharp
var startPoint = new XYZ(x / MmPerFoot, y / MmPerFoot, 0);
var endPoint   = new XYZ(x / MmPerFoot, y / MmPerFoot, 0);
```

L'élévation du niveau de base n'entre jamais dans la géométrie passée à
`StairsRun.CreateStraightRun`. La ligne est donc toujours dans le plan Z = 0 du projet,
quel que soit le niveau de base demandé.

Matrice mesurée — géométrie, type (`ESC_LGT INDIV Hmax18 Gmin 24`) et volée
(`{p0:{x:20000,y:0}, p1:{x:20000,y:8000}}`) strictement identiques à chaque appel :

| Niveau de base | Élév. base (mm) | Δ (mm) | Résultat | Contremarches obtenues / voulues |
|---|---|---|---|---|
| TEST_NEG_A (neuf, exact) | −9 000 | 2 720 | OK | **71 / 16** |
| SOUS SOL | −3 060 | 3 060 | OK | **34 / 17** |
| RDC | 0 | 2 890 | OK | 34 / 17 |
| RDC | 0 | 5 610 | OK | 34 / 32 |
| TEST_POS_A (neuf, exact) | +500 | 2 720 | **ÉCHEC** `locationPath` | — |
| R+1 | +2 890 | 2 720 | **ÉCHEC** `locationPath` | — |
| R+1 | +2 890 | 5 440 | **ÉCHEC** `locationPath` | — |
| R+2 | +5 610 | 2 720 | **ÉCHEC** `locationPath` | — |
| R+3 | +8 330 | 2 720 | **ÉCHEC** `locationPath` | — |
| TEST_EXACT_A (neuf, exact) | +20 000 | 2 720 | **ÉCHEC** `locationPath` | — |

Message intégral des échecs :

```
The input locationPath is not a valid location path line for straight run.
Parameter name: locationPath
```

**Règle : l'échec suit le signe de l'élévation du niveau de base.** Base ≤ 0 → passe.
Base > 0 → échoue. Douze observations, aucun contre-exemple.

Deux hypothèses écartées par l'expérience, pas par raisonnement :

- **La hauteur entre niveaux** — Δ 5 440 échoue, Δ 5 610 passe ; Δ 2 720 échoue depuis
  R+1 et passe depuis TEST_NEG_A. Aucune corrélation.
- **Le résidu flottant des élévations du gabarit** — les niveaux qui échouaient portent
  bien un résidu (`R+2` = 5.6100000000001975 m), mais `TEST_EXACT_A` créé à **20 000 mm
  exacts** échoue aussi, et `TEST_NEG_A` à **−9 000 mm exacts** passe. Le résidu n'est
  pas discriminant. Il provient du gabarit, pas d'une conversion RiveTT.

**Le défaut est plus large que l'échec.** Quand base ≠ 0, la volée est mal placée même
quand Revit accepte : base −9 000 produit **71 contremarches** pour 16 voulues, base
−3 060 en produit **34 pour 17**. Seul base = 0 donne une géométrie juste. Ce n'est donc
pas « `create_stair` échoue sur certains niveaux » mais **« `create_stair` n'est correct
qu'au niveau 0, et devient fatal au-dessus »**.

**Correction.** Ancrer la volée sur le niveau de base :

```csharp
var baseZ = baseLevel.Elevation;           // déjà en pieds, unités internes Revit
var startPoint = new XYZ(x / MmPerFoot, y / MmPerFoot, baseZ);
```

`TryReadRuns` est aujourd'hui `static` et ne reçoit pas le niveau : lui passer
`baseLevel.Elevation` en paramètre. Vérifier ensuite le comportement attendu de
`StairsRun.CreateStraightRun` — si l'API attend un `z` **relatif** au scope
`StairsEditScope` plutôt qu'absolu, la valeur à injecter est `0` et le défaut est
ailleurs dans le scope ; les deux variantes se départagent par la matrice ci-dessous.

**Recette de non-régression** — une volée de 3 840 mm (16 × 240) entre niveaux distants
de 2 720 mm, rejouée pour chaque signe de base : `−9 000`, `−3 060`, `0`, `+500`,
`+20 000`. Critère : création réussie **et** `actualRiserCount == desiredRiserCount ==
16` dans les cinq cas. Aujourd'hui trois échouent et deux donnent le mauvais compte.

### P0.2 Le journal d'audit n'enregistre rien

**Statut : mesuré.**

`%LOCALAPPDATA%\RiveTT\audit.jsonl` est resté inchangé (26/08 00:42, 115 lignes datées
des 24 et 25/08, issues des tests unitaires) pendant toute la campagne : **0 entrée**
pour environ 60 appels dont une trentaine d'écritures — création de document, 7 murs,
3 pièces, 2 dalles, 8 vues, 6 nomenclatures, 4 escaliers et **trois suppressions
réelles**, dont `delete_schedule` avec une cascade de 7 éléments. Aucun autre fichier
d'audit sur le poste.

`get_server_capabilities` publie pourtant `auditLogPath`, et les consignes du connecteur
affirment que chaque écriture y est consignée. La garantie est fausse en l'état.

Conséquence directe : le §4.3 du protocole désigne `audit.jsonl` comme *« la seule
matière exploitable »* si `PipeClosed` se reproduit. Elle n'existerait pas.

**Correction.** Rétablir l'écriture depuis le chemin d'exécution réel des outils, pas
seulement depuis le harnais de test. Test d'intégration : un appel d'écriture réussi et
un refusé produisent chacun leur ligne, vérifié sur le fichier.

### P0.3 Le serveur MCP ne redécouvre pas une session Revit apparue après lui

**Statut : mesuré.**

`RiveTT.Server` démarré à 20:19:17, Revit à 20:35:33. `sessions\<pid>.json` écrit et
valide, pipe `RiveTT.Revit.<pid>` acceptant les connexions (vérifié par
`NamedPipeClientStream.Connect` → `CONNECT OK`). Tous les outils échouaient malgré tout.
Le serveur ne relit jamais le répertoire de sessions. Reproductible à volonté : démarrer
Revit après le serveur.

Aggravant — **l'erreur remontée est opaque** : `An error occurred invoking '<outil>'`,
sans code, sans message, sans entrée d'audit. Impossible de distinguer « pas de document
actif » de « add-in injoignable » ou « verrou d'écriture ». Un agent ne peut pas décider
quoi faire.

**Précision mesurée le 26/08 en fin de campagne, qui restreint la recherche.** Revit
fermé *après* le démarrage du serveur, le même appel rend une erreur **parfaitement
structurée** :

```json
{ "code": "NoRevitSession", "tool": "get_element_parameters",
  "message": "No RiveTT Revit session is available. Open Revit (2026.5+ or 2027)...",
  "stage": "transport", "modelChanged": false }
```

Le chemin générique « aucune session Revit » est donc correct et n'est pas à refaire.
L'erreur opaque est **spécifique au cas serveur-avant-Revit** : un chemin distinct, qui
échoue avant d'atteindre la couche rendant `NoRevitSession`. C'est là qu'il faut
chercher, pas dans la gestion d'erreur générale.

**Investigation de code du 26/08 (branche `dev/0.3.0`), sans session Revit disponible
pour rejouer le scénario.** `RevitPipeBridge.SendCommandAsync` relit déjà
`%LOCALAPPDATA%\RiveTT\sessions\*.json` à **chaque appel** (`RevitSessionDiscovery.
FindPreferredPipe`, pas de cache figé au démarrage), et `RevitConnectionManager.
ExecuteAsync` capture déjà toute exception de transport vers `TransportError.Describe`
(codes structurés `NoRevitSession`/`Timeout`/`PipeClosed`/`PipeAccessDenied`), ce que
confirme la précision ci-dessus. Le code lu ne contredit donc PAS que le chemin
générique fonctionne — mais il ne montre pas non plus de cache de session figé à
corriger : aucune relecture différée trouvée sur aucun chemin.

Piste restante, non vérifiée : `RevitConnectionManager._mutex` (`SemaphoreSlim(1,1)`)
est acquis **avant** le `try/catch` qui mappe les exceptions. Si un appel antérieur
reste bloqué dans `pipe.ConnectAsync` sans jamais atteindre son `finally`, tout appel
suivant attend indéfiniment le sémaphore et remonte comme un timeout MCP générique
plutôt que comme un code structuré — compatible avec le symptôme observé, non prouvé.
À instrumenter (horodatage prise/relâche du mutex) en session réelle avant tout
correctif.

Aggravant confirmé côté code : `RevitPipeBridge.cs` n'appelle jamais `AuditLogger`
(propre à `RiveTT.Plugin`, absent de `RiveTT.Server`) — un échec de transport n'est
donc jamais audité, contrairement au refus `PermissionDenied`.

**Correction.** Surveiller `%LOCALAPPDATA%\RiveTT\sessions\` (watcher ou relecture à
chaque appel en échec de connexion), et mapper toute rupture sur un code explicite —
`NoActiveDocument`, `NoRevitSession`, `PipeUnavailable` — avec le même bloc `context`
que `PermissionDenied` fournit déjà.

Deux processus `RiveTT.Server` tournaient simultanément (PID 6620 et 25632, même
horodatage de démarrage). Confirmer si c'est nominal (un par client MCP) ou une fuite,
et si deux serveurs peuvent viser la même session Revit — ce point rejoint P1.4.

---

## P1 — Contrats faux ou trompeurs

### P1.1 `create_preset_schedule` : trois presets sur six inexploitables

**Statut : mesuré.**

| Preset | Résultat |
|---|---|
| `door_by_room`, `window_by_room` | OK, 5 champs |
| `room_finish` | OK, 3 champs |
| `sheet_list` | `Failed: categoryId is not a valid category for a regular schedule.` |
| `view_list` | message identique |
| `material_takeoff` + `categoryName` | créé, **`fieldCount: 0`** |
| `material_takeoff` sans `categoryName` | refus correct, paramètre nommé |

Ce n'est pas un `Unknown preset` : `sheet_list` et `view_list` sont routés vers un
schedule de catégorie ordinaire alors qu'ils exigent `ViewSchedule.CreateSheetList` et
une nomenclature de vues. `material_takeoff` produit une nomenclature vide, ce qui est
pire qu'un refus : l'appelant croit avoir un livrable.

**Correction.** Router `sheet_list` vers `ViewSchedule.CreateSheetList`, `view_list` vers
son constructeur propre, peupler les champs de `material_takeoff`, et faire échouer tout
preset qui produirait zéro champ plutôt que de rendre un id.

### P1.2 `place_viewport` sur feuille sans cadre : cadre nul puis cadre circulaire

**Statut : mesuré.**

Premier appel sur feuille sans cartouche : `frameOutlineMm.source: "unknown"` (le
protocole attend `"sheetSize"` ou `"viewOutline"`) et contour `minX/minY/maxX/maxY = 0.0`
— **exactement la signature d'échec `[0,0]×[0,0]`** que le correctif devait supprimer.
`fitsOnSheet: null` est en revanche conforme, et le `warnings` explique pourquoi.

Second appel **sans** `positionX`/`positionY` sur la même feuille : au lieu du refus
attendu avec renvoi vers `place_title_block`, l'outil centre le viewport et déduit
`source: "viewOutline"` du **contour du viewport qu'il vient lui-même de poser**. La
même feuille rend deux cadres différents selon l'ordre des appels.

**Correction.** Quand aucun cadre n'est mesurable : refuser le centrage implicite avec le
message prévu ; et ne jamais dériver le cadre d'un viewport posé par l'outil lui-même —
seul un cartouche ou la taille de feuille Revit fait foi.

**Déjà en place dans le code (26/08, branche `dev/0.3.0`), non revérifié en session
Revit.** `src/RiveTT.Tools/Utilities/SheetFrame.cs` porte exactement ce contrat :
`Measure` ne dérive `Frame` que du cartouche, de `SHEET_WIDTH`/`SHEET_HEIGHT` ou de
`sheet.Outline` — jamais d'un `Viewport` — et `PlaceViewportTool.Execute` refuse
explicitement le centrage (`CortexErrorCode.InvalidInput`) quand `!frame.IsKnown`. Les
commentaires du fichier (« batch_create_sheets placed every viewport at a hardcoded
(0.5 ft; 0.5 ft)… ») indiquent qu'un correctif antérieur au 26/08 a déjà unifié cette
logique. Soit le binaire testé en session était antérieur à ce correctif (build/install
non resynchronisé avec `main`), soit un scénario non couvert par cette lecture reste à
isoler. À revérifier en session Revit avant de rouvrir ce point.

### P1.3 `create_sheet` sans `titleBlockId` pose un cartouche

**Statut : mesuré.**

Sa description annonce « Without any of them Revit creates a bare 210x297 mm sheet with
no frame ». L'appel a rendu `titleBlockType: "A4H"`, `hasTitleBlock: true`. Le repli
silencieux est précisément ce que la description dit ne pas faire. Il a fallu supprimer
l'instance à la main pour obtenir la feuille sans cadre du §1.5.

**Correction.** Aligner le code sur la description (feuille nue), ou la description sur le
code (repli documenté, avec le type effectivement retenu dans `warnings`). Le choix est
ouvert ; l'écart, lui, ne l'est pas.

### P1.4 Invalidation du cache aux transitions de document

**Statut : mesuré le 27/08/2026, non reproduit.** Session Revit réelle, build installé
`0.2.0.0` (pas les correctifs de code de `dev/0.3.0` — non pertinent ici, aucun d'eux ne
touche ce chemin), projet `RiveTT_TEST_0.2.0.rvt`.

**Protocole joué**, sur la pièce « Bureau 1 » (id `10568284`) :

1. Lecture témoin : `Numéro = "101"`.
2. `set_element_parameters(dryRun:false)` → `Numéro = "999TEST"` ; relecture immédiate
   confirme l'écriture (`cached: false`).
3. **Constat en cours de route, utile pour P4.1** : `Document.Close(false)` sur le
   document ACTIF est refusé par l'API — `The active document may not be closed from
   the API` (`InvalidOperationException`). Contournement obligatoire : activer un
   autre document (`open_document` sur un second `.rvt`), ce qui rend
   `RiveTT_TEST_0.2.0` inactif ; alors seulement `Document.Close(false)` réussit
   dessus, retrouvé via `app.Documents`.
4. `open_document(RiveTT_TEST_0.2.0.rvt)` : rouvre et réactive. `get_project_info`
   confirme le bon `filePath`, `cached: false`.
5. Relecture ciblée du même élément : `Numéro = "101"` — **la valeur sur disque, pas
   `"999TEST"`**. Aucune valeur périmée, `cached: false`.

**Conclusion.** Avec ce protocole précis (bascule via un document intermédiaire —
imposée par la contrainte API du point 3, pas un choix), le cache s'invalide
correctement et l'identité de document reste cohérente. Le défaut décrit par l'audit de
relecture (`A108` puis `108`) **ne s'est pas reproduit**. Réserve : le protocole exact
ayant produit ce défaut à l'origine n'est pas connu (fermeture depuis l'UI Revit,
séquence différente ?) — cette mesure ne le couvre donc pas à 100%, mais fournit un
premier fait négatif là où il n'y en avait aucun. Pas de correctif à appliquer sans un
scénario qui reproduit.

**Trouvaille distincte en cours de route, à noter séparément** : `get_element_parameters`
avec `parameterNames` **non vide** échoue systématiquement avec l'erreur opaque
`An error occurred invoking 'get_element_parameters'` (aucun code, aucun message, aucune
entrée d'audit) — reproduit sur plusieurs noms de paramètre différents, y compris un
tableau à un seul élément. `src/RiveTT.Server/Tools/JsonArrayParam.cs` documente déjà
avoir corrigé cette classe de défaut pour 55 paramètres sur 41 outils (tableau optionnel
qui ne bind pas côté hôte MCP, avant même d'atteindre le code RiveTT) en typant ces
paramètres `string?` porteur de JSON encodé plutôt que `string[]?`. Mais l'appel a été
fait ici en passant `parameterNames` comme un **tableau JSON natif** — exactement ce que
la description de l'outil invite à faire (« JSON array, e.g. [\"A\",\"B\"] ») — et casse
quand même : le contrat réel exige une **chaîne contenant du JSON**, pas un tableau, ce
que ni la description ni le schéma exposé (`"parameterNames": {}`, sans type) ne
signalent à l'appelant. Le correctif `JsonArrayParam` résout « le tableau ne bind pas »
seulement si l'appelant obéit déjà au bon format ; il ne résout pas l'ambiguïté qui fait
qu'un appelant raisonnable — humain ou modèle — envoie un tableau natif. Candidat
plausible pour une partie des erreurs opaques génériques que P0.3 documente par
ailleurs, à vérifier sur les 40 autres outils concernés avant de le traiter comme
résolu.

### P1.5 `create_view_filter` résout les paramètres sur un seul élément témoin

**Statut : localisé.**

`src/RiveTT.Tools/Views/CreateViewFilterTool.cs:90-93` :

```csharp
// A sample element from the first category, used to resolve parameter ids by name.
var testElem = new FilteredElementCollector(doc)
    .OfCategoryId(catIds[0])
    .WhereElementIsNotElementType()
    .FirstOrDefault();
```

Un paramètre porté par une partie seulement de la catégorie — cas courant d'un partagé
comme `ARC_PAR_TYPOLOGIE` — est déclaré introuvable si le premier élément rencontré ne le
porte pas. Non exercé en session : le modèle de test ne portait pas ces paramètres.

**Correction.** Résoudre sur l'ensemble des éléments et des types de la catégorie ;
distinguer paramètre d'instance, de type et partagé ; échouer explicitement quand la
règle ne peut s'appliquer ; **ne jamais rendre un filtre annoncé comme configuré avec
zéro règle**.

### P1.6 Le refus sous verrou nomme le mauvais outil

**Statut : mesuré.**

`create_wall` refusé sous verrou renvoie :

```
'create_line_based_element' can modify the model and RiveTT is currently in read-only mode.
```

La couche de permission remonte le nom du handler interne, pas celui de l'outil appelé.
L'utilisateur ne peut pas rattacher le message à son appel. Le reste du refus est
exemplaire : `stage: "permission"`, `modelChanged: false`, `lockedSince`, `lockedBy`.

**Correction.** Propager le nom d'outil public jusqu'à la couche de permission.

### P1.7 `load_family` ne peut pas mettre à jour une famille existante

**Statut : mesuré.**

`src/RiveTT.Tools/Elements/LoadFamilyTool.cs:62` appelle
`doc.LoadFamily(familyPath, out var family)` — l'overload **sans `IFamilyLoadOptions`**.
Cet overload rend `false` dès que la famille existe déjà dans le projet, et n'écrase
jamais.

Conséquence : `load_family` ne sait charger qu'une famille **absente** du projet.
Recharger une famille modifiée hors Revit — le cas d'usage principal, et celui que
`get_server_capabilities` recommande explicitement à la place d'`edit_family` — est
sans effet.

**Mesure décisive** sur `CAR_A4_Entête projet` (id 9614731), aller-retour complet avec
une différence réelle et le document de famille refermé :

```
typesInFamilyAfterEdit=2 ; saved ; closed=ok ;
typesInProjectBefore=1 ; loadFamilyReturned=False ; typesInProjectAfter=1
```

Un type `RIVETT_AUDIT` est ajouté au document de famille, enregistré sur disque, puis le
document est fermé. Le fichier porte 2 types, la famille du projet 1 : ils diffèrent
réellement, ce n'est pas un « identique, rien à faire ». `LoadFamily` rend malgré tout
`false` et le projet reste à 1 type.

Ni la fermeture du document, ni l'ordre des opérations, ni l'ampleur des modifications
n'y changent quoi que ce soit.

Le message d'échec aggrave le diagnostic —
*« Failed to load family (may already be loaded or path invalid) »* confond « déjà
chargée » et « chemin invalide » : deux causes opposées, l'une bénigne, l'autre une
faute d'appel.

**Correction.** Utiliser `LoadFamily(string, IFamilyLoadOptions, out Family)` avec une
implémentation répondant `true` à `OnFamilyFound` (écrasement) et arbitrant
`OnSharedFamilyFound`. Exposer le choix côté outil (`overwriteExisting`), et distinguer
les deux causes d'échec dans le message.

**Dépendance.** P4.1 (`open_family`) ne peut pas livrer son aller-retour sans ce
correctif. Celui-ci se corrige seul, sans attendre P4.1.

---

## P2 — Robustesse

### P2.1 Lots intolérants à un identifiant invalide

**Statut : rapporté par l'audit, non exercé.** `operate_element` ferait échouer tout le
lot sur un seul ID invalide pour `hide`, `select`, `unhide`, là où la suppression valide
déjà proprement.

`delete_element` et `get_element_parameters` montrent le contrat visé, mesuré cette
campagne : `invalidIds`, `notFoundIds`, `found: false` par élément, cascade détaillée.
Généraliser ce contrat — `succeededIds`, `skippedIds` et le motif par élément — plutôt
que d'annuler le lot.

À exercer avant correction : un lot mêlant IDs valides et invalides sur chaque action.

### P2.2 `get_current_view_elements` : pas de curseur, et deux compteurs non expliqués

**Statut : mesuré + localisé.**

`src/RiveTT.Tools/Elements/GetCurrentViewElementsTool.cs:79,149-154` — l'outil expose
`limit` et un booléen `truncated`, mais **aucun curseur** : au-delà de la limite, la
suite est inatteignable autrement qu'en découpant par catégorie à la main.
`ai_element_filter` fournit déjà `pageSize`/`nextCursor` — le contrat existe, il n'est pas
appliqué ici.

Mesuré par ailleurs sur un appel **sans filtre** : `totalElementsInView: 88` pour
`filteredElementCount: 18`, sans que la réponse dise que les deux comptent des choses
différentes (ligne 132 compte la vue entière, ligne 173 le résultat filtré). Le même
appel remonte `unresolvedCategories: ["OST_SpaceTags","OST_ViewportLabels"]` alors
qu'aucune catégorie n'a été demandée.

**Correction.** Aligner sur `ai_element_filter` (`pageSize`, `nextCursor`) ; nommer les
deux compteurs sans ambiguïté ; ne remonter `unresolvedCategories` que pour les catégories
effectivement demandées par l'appelant.

### P2.3 `create_room` crée des pièces non encloisonnées

**Statut : localisé.**

`src/RiveTT.Tools/Elements/CreateRoomTool.cs:160-179` — la pièce est créée, puis
`enclosed: false`, `areaM2: null` et un avertissement sont rendus. Le diagnostic est bon,
le comportement laisse une pièce inutilisable dans le modèle.

**Correction.** Refuser par défaut quand `enclosed == false`, avec une option explicite
`allowUnenclosed` pour les cas légitimes. Les 3 pièces de la campagne étaient encloses
(33,6 m² chacune) : le cas d'échec n'a pas été exercé.

### P2.4 `color_elements` : libellé de paramètre non résolu

**Statut : mesuré.**

`color_elements(categoryName:"OST_Walls", parameterName:"Type Name")` sur 6 murs tous de
type `MUR_Béton20` a rendu un groupe unique — correct — mais étiqueté
`parameterValue: "None"`, et **rien dans `unresolvedParameterNames`**.
`get_server_capabilities` annonce pourtant que `Type Name` / `Nom du type` se résout, et
que tout paramètre non résolu est signalé « never as an empty column ».

**Correction.** Faire passer `color_elements` par le même résolveur localisé que
`get_element_parameters`, qui a correctement proposé `Etage de bâtiment` pour
`Niveau de bâtiment` pendant la campagne.

### P2.5 `create_dimensions` mode éléments : entraxe rendu, nus annoncés

**Statut : mesuré.**

La description annonce « measured between the faces facing each other ». Sur deux murs de
200 mm d'entraxe 7 200 (nus opposés à 7 000, confirmé par les pièces à 33,6 m² =
4,80 × 7,00), l'outil rend **7,20 m**. C'est l'entraxe.

Sans rapport avec le défaut corrigé du 25/08 : la valeur pathologique `−0,3048 m` ne
s'est jamais présentée, et 4 murs / 3 segments rendent bien 15,00 m d'écarts réels.

**Correction.** Trancher le contrat — cotation d'axes ou de nus — puis aligner code et
description. Pour l'usage architectural, la résolution des références de faces avant
création est la voie ; c'est aussi ce que recommande l'audit de relecture.

---

## P3 — Hygiène, sans effet fonctionnel

### P3.1 Branches `REVIT2024_OR_GREATER`

**Statut : localisé — et l'audit de relecture sous-estime la portée tout en surestimant
l'urgence.**

L'audit ne cite que `OperateElementTool.cs`. Le dépôt en compte **194 occurrences dans
101 fichiers**.

Mais ces branches sont **inertes** : `src/RiveTT.Tools/RiveTT.Tools.csproj:22` et
`src/RiveTT.Plugin/RiveTT.Plugin.csproj:21` définissent `REVIT2024_OR_GREATER`
inconditionnellement. Toutes prennent systématiquement la voie « vraie ». Aucun
comportement n'en dépend, aucune n'est un défaut.

**Correction.** Suppression mécanique en une passe dédiée, hors de tout correctif
fonctionnel, pour que le diff reste lisible. Priorité basse : rien ne casse aujourd'hui.

### P3.2 La constante `304.8` en dur

**Statut : localisé.**

68 fichiers portent `304.8` en littéral au lieu de
`UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters)` ; `UnitUtils` n'apparaît
que dans 2 fichiers.

**À dire explicitement, parce que la question a été posée : ceci n'est pas la cause du
défaut escalier, et le remplacer ne le corrigera pas.**

- L'API Revit stocke **toutes** les longueurs en pieds décimaux en interne. Il n'existe
  pas de « mètre natif » : `XYZ`, `Level.Elevation`, `Line.CreateBound` sont en pieds.
  On ne peut pas remplacer l'unité, seulement la façon de convertir.
- La conversion elle-même est exacte : 1 pied = 304,8 mm par définition. La division
  n'introduit rien au-delà de l'arrondi IEEE 754 ordinaire.
- Le résidu observé sur les élévations du gabarit (`R+2` = 5.6100000000001975 m) vient
  du fichier, pas de RiveTT — et `TEST_EXACT_A` créé à 20 000 mm exacts échoue quand
  même. Le résidu est innocent.

La centralisation reste souhaitable — un point de conversion unique, testable, cohérent
avec `unitPolicy` — mais comme hygiène, pas comme correctif.

---

## P4 — Évolution demandée : ouvrir gabarits et familles

### P4.1 `open_template` et `open_family`

**Statut : faisabilité établie, quatre segments mesurés.** Évolution inscrite à la
demande de l'équipe — ce n'est pas un défaut. Mesures détaillées en annexe A.

Aujourd'hui `open_document` ne prend que les `.rvt`.
`create_document(templatePath: .rte)` crée bien un projet neuf depuis un gabarit
(vérifié : `levelCount: 6`, activé dans Revit), mais ouvrir un `.rte` **pour l'éditer**
et ouvrir une `.rfa` restent absents.

#### Ce qui bloque réellement : une garde d'extension

`src/RiveTT.Tools/Project/DocumentCreationTools.cs:289` refuse tout chemin ne finissant
pas par `.rvt`, **avant tout appel à Revit**. C'est une comparaison de chaîne, pas une
limite d'API. Tout ce qui suit dans la même méthode (lignes 323-343) est agnostique à
l'extension : `ResolveUiApplication`, `OpenDialogAutoAnswer`, puis
`OpenAndActivateDocument(filePath)` sur un simple chemin.

La note de capacités (`GetServerCapabilitiesTool.cs:100`) et `USER_GUIDE.md:240`
déclarent que `Document.EditFamily` « a provoqué un interblocage depuis ce dispatcher ».
**Cette affirmation est fausse et doit être corrigée en premier** : c'est elle qui a fait
renoncer à la fonctionnalité. Le dépôt appelle `EditFamily` en production à deux endroits
(`ExportFamiliesTool.cs:71`, `ListFamilySizesTool.cs:197`), et les quatre segments du
chemin ont été exercés le 26/08 sans le moindre blocage (annexe A).

#### Trois règles d'implémentation, chacune établie par une mesure

**1. Le nom du fichier EST l'identité de la famille.** Un `.rfa` écrit sous un nom
arbitraire rouvre avec `OwnerFamily.Name` vide : aucun nom indépendant ne survit dans le
fichier. Charger un tel fichier créerait une famille homonyme du fichier **à côté** de
l'originale, occurrences pointant toujours sur l'ancienne.

→ `SaveAs` doit écrire sous `<dossier>\<family.Name>.rfa`, jamais sous un nom choisi par
l'outil. Vérifié : avec le bon nom, le projet reste à 209 familles.

**2. Le document actif change, et tout appel ultérieur suit.** Après ouverture d'une
famille, `get_project_info` rend le `.rfa` et `get_current_view_info` rend la vue de la
famille. L'outil doit l'annoncer dans sa réponse et donner le chemin du retour.

**3. Le document reste ouvert — c'est l'objet même de l'outil.** `ExportFamiliesTool` et
`ListFamilySizesTool` referment les leurs dans un `finally` (`H18`) ; `open_family` ne
le peut pas. Sans `close_document` en regard, une session accumule les documents de
famille. Constaté en fin de campagne : quatre documents ouverts, trois résiduels.

#### Deux points d'API à ne pas réinventer

- **`SaveAs` est obligatoire pour afficher une famille du modèle.** Le `Document` rendu
  par `EditFamily` a un `PathName` **vide** ; `OpenAndActivateDocument` exige un chemin.
  `new UIDocument(famDoc)` se construit mais n'active rien, et affecter `ActiveView` lève
  *« Changing the active view is not applicable to inactive documents »*. Le `SaveAs`
  n'est pas un détour : il donne au document le chemin sans lequel il ne peut pas être
  activé.
- **Aucune fermeture n'est requise.** Ni pour afficher — l'activation réussit sur un
  document non refermé —, ni pour charger. La fermeture est une hygiène, pas une
  contrainte.

#### Action

1. **Corriger la documentation** — `GetServerCapabilitiesTool.cs:100` et
   `USER_GUIDE.md:240`. Tant que l'affirmation d'interblocage y figure, elle fera
   abandonner la fonctionnalité à nouveau.
2. **Exposer `open_template` et `open_family`** sur la plomberie existante de
   `DocumentCreationTools` : assouplir la garde ligne 289, séparer les outils pour que
   chaque contrat reste lisible (`open_document` = projet, `open_family` = famille,
   `open_template` = gabarit). `OpenDialogAutoAnswer` et le couple
   `activated` / `activationError` sont déjà là.
3. **Ajouter `close_document`**, sans quoi le point 3 ci-dessus n'a pas de remède.
4. **Exposer `edit_family` en arrière-plan** sur le patron d'`ExportFamiliesTool` :
   `EditFamily` → modifier → `LoadFamily` → `Close(false)` dans un `finally`. Ne jamais
   activer dans l'interface le `Document` rendu par `EditFamily` — seul assemblage non
   mesuré, et nécessaire à rien.
5. **Tests d'intégration** : ouverture `.rvt` / `.rte` / `.rfa` ; transition entre
   documents et cohérence de session après transition ; aller-retour ouvrir → modifier →
   recharger, en vérifiant que le nombre de familles du projet **n'a pas augmenté**.

**Bloqué par P1.7.** L'aller-retour ne peut pas fonctionner tant que `load_family`
utilise l'overload sans `IFamilyLoadOptions`.

---

## Non exerçable sur ce poste

À traiter à la main, hors session agent :

- **§2.7** — aucune variante de conception dans le modèle (création sans API publique,
  documenté), et aucun `CurtainSystem` multi-faces constructible sans masse. Le mur-rideau
  simple face répond correctement (6 lignes V, 7 panneaux, 22 meneaux, aucun plantage).
- **§5, installateur** — assistant interactif à double-cliquer, poste sans Revit,
  refus d'un Revit 2026.0–2026.4 (absent du poste), désinstallation Revit fermé.

## Points clos, à ne pas rouvrir

Mesurés conformes le 26/08 : cotation entre éléments et point à point (§1.1, §1.2),
`viewId` sur vue non active (§1.4), refus propre des nomenclatures et vues déjà posées
(§1.6), dryRun de `create_floor` (§1.7, 20,0 m² annoncés = 20,0 m² réels), alias
`phaseCreatedId` (§1.8), cadre A1 français et pavage 2×2 (§2.1), `workflow_sheet_set`
(§2.2), parité `clash_detection` / `workflow_clash_review` (§2.3, 6 = 6, mêmes paires),
aperçus de suppression et fidélité de la cascade (§2.4), dryRun de `send_code_to_revit`
(§2.5, aucun fichier écrit), verrou IFC (§2.6), verrou d'écriture dans les deux sens
(§3).

Anomalie §4.2 (filtre d'annotations à zéro) **non reproduite** : quatre voies d'accès
rendent 3 sur 3. Le signalement portait sur 39 étiquettes ; l'écart d'échelle reste une
variable non couverte.

Anomalie §4.3 (`PipeClosed`) **non reproduite** : aucune rupture de transport sur
~60 appels, dont des lots de 6 et 11 écritures consécutives.

---

## Annexe A — Mesures : ouverture de documents et familles

Session du 26/08/2026, Revit 2027.2, projet `RiveTT_TEST_0.2.0.rvt`. Appels par
`send_code_to_revit` avec `transactionMode: "none"`, interdiction levée explicitement
par l'équipe pour ces points d'audit. Conservées ici parce qu'elles étayent P4.1 et P1.7,
et parce qu'elles évitent de refaire le travail.

### A.1 Les quatre segments du chemin d'ouverture

| Opération | Appel | Résultat |
|---|---|---|
| Famille du **disque**, arrière-plan | `Application.OpenDocumentFile(.rfa)` | OK |
| Famille du **document**, arrière-plan | `Document.EditFamily(family)` | OK |
| Projet `.rvt` **activé dans l'interface** | `OpenAndActivateDocument` | OK |
| Famille **activée dans l'interface** | `OpenAndActivateDocument(.rfa)` | OK |

Aucun n'interbloque. L'affirmation d'un interblocage n'est étayée par rien de mesurable.

**A.1.1 — `.rfa` du disque, en arrière-plan**

```
title=Centre de gravité; isFamilyDocument=True;
pathName=C:\ProgramData\Autodesk\RVT 2027\...\Centre de gravité.rfa; closed=ok
```

**A.1.2 — `EditFamily` sur des familles du document**

`list_family_sizes(includeSize: true, categories: ["OST_TitleBlocks"], limit: 5)` appelle
`EditFamily` → `SaveAs` → `Close(false)` par famille
(`ListFamilySizesTool.cs:190-220`). `sizeMeasured: true` et une taille non nulle pour les
cinq (632, 724, 1216, 1140, 844 KB). `MeasureSize` rend `null` sur la moindre exception :
une taille non nulle prouve les trois étapes. Sans interface, donc sans rien à voir à
l'écran — c'est précisément ce qui rend l'opération sûre.

**A.1.3 — `.rfa` activé dans l'interface, avec restauration**

```
projectAtEntry=RiveTT_TEST_0.2.0; ACTIVATED title=Centre de gravité;
isFamilyDocument=True; restored=RiveTT_TEST_0.2.0
```

Contrôle de cohérence de session juste après ce double changement de document — scénario
de P1.4 : `get_project_info` rend le bon `filePath` et `ai_element_filter(OST_Walls)` les
8 murs attendus. **Aucune trace de document périmé.**

**A.1.4 — Famille DU MODÈLE ouverte dans l'interface**

```
family=CAR_A4_Entête projet; isEditable=True; isInPlace=False;
editFamilyOk isFamilyDocument=True; savedAndClosed;
ACTIVATED title=OUVERTURE_FAMILLE_TEST; isFamilyDocument=True
```

### A.2 Pourquoi `SaveAs` est incontournable

```
newUIDocumentOk; activeAfterCtor=RiveTT_TEST_0.2.0;
tryingActiveView=Feuille;
uiDocumentError=Changing the active view is not applicable to inactive documents.
famPathName=[]
```

`new UIDocument(famDoc)` se construit sans erreur mais n'active rien. Revit refuse
ensuite explicitement, et la cause est lisible : `famDoc.PathName` est vide.

### A.3 Le nom de fichier porte l'identité

Fichier écrit sous un nom arbitraire puis rouvert :

```
docTitle=OUVERTURE_FAMILLE_TEST; isFamilyDocument=True; ownerFamilyName=; closed=ok
```

`OwnerFamily.Name` **vide**. Aucun nom de famille indépendant ne survit dans le `.rfa`.

Contre-épreuve au bon nom :

```
familiesBefore=209; familyName=[CAR_A4_Entête projet];
savedAs=[...\CAR_A4_Entête projet.rfa]; pathAfterSaveAs=[...\CAR_A4_Entête projet.rfa];
ACTIVATED_WITHOUT_CLOSING title=CAR_A4_Entête projet; isFamilyDocument=True
```

Deux résultats en un : le bon nom préserve l'identité (209 familles avant et après,
aucun doublon), et **l'activation réussit sans fermeture préalable**.

### A.4 L'aller-retour échoue sur `load_family` — voir P1.7

```
typesInFamilyAfterEdit=2 ; saved ; closed=ok ;
typesInProjectBefore=1 ; loadFamilyReturned=False ; typesInProjectAfter=1
```

### A.5 Documents laissés ouverts en fin de campagne

```
CAR_Cartouche A4_étiquette pochette.rfa | RiveTT_TEST_0.2.0 | Centre de gravité |
CAR_A4_Entête projet
```

Trois documents de famille résiduels, dont un `Document` d'`EditFamily` jamais fermé.
Motive le `close_document` de P4.1.

### A.6 Note d'ingénierie — le délai de garde

Un délai de garde ne débloque pas un interblocage, et il n'y en a pas ici à traiter. À
conserver comme principe si la question revient : l'API Revit est mono-thread et liée au
thread d'interface, sans jeton d'annulation, et `Thread.Abort` n'existe plus en .NET 10.
Un chien de garde sur un autre thread ne peut pas interrompre un appel Revit bloqué — il
ne ferait abandonner que le côté MCP, rendant une erreur de délai pendant que Revit reste
gelé, et laissant un `Document` ouvert qui verrouille le `.rfa` pour la session.

La discipline correcte, si une activation posait problème, est le **retour immédiat** :
le handler rend la main, l'activation est postée sur `Idling` ou par `PostCommand`, et
l'outil répond « activation demandée » sans attendre. C'est déjà la forme du contrat de
`create_document`, qui sépare `activated` de `activationError`.
