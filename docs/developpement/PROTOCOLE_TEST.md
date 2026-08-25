# Protocole de test en session Revit — RiveTT 0.2.0

Ce document existe parce que 13 tests de la suite ne peuvent pas s'exécuter hors d'un
poste Revit, et parce que tout ce qui touche à la géométrie, aux transactions et aux
messages d'erreur de Revit n'est prouvable que sur maquette. Le reste du dépôt est
vérifié par `dotnet test` ; **ce qui suit ne l'est pas**.

Consignez le résultat dans la pull request ou le commit qui porte la correction : la
fiche de résultat en fin de document est faite pour y être collée. Les deux journaux
de campagne qui tenaient ce rôle ont été retirés — un historique qui grossit sans
qu'on le relise ne vaut pas la ligne de contexte qu'il coûte.

---

## Avant de lancer l'agent — à faire par un humain

1. **Fermer puis rouvrir Revit 2026.5.** Obligatoire : l'installation du 26/08 a parqué
   les anciennes DLL en `.old-<horodatage>`, mais l'instance en cours garde l'ancien
   code **en mémoire**. Sans redémarrage, vous testez la version précédente.

2. **Vérifier le ruban.** Onglet *Compléments* → panneau **RiveTT**. Trois boutons :
   *Lecture seule*, *Écriture*, *État*. Les trois doivent porter une icône visible.
   Si les icônes de verrouillage manquent encore, arrêtez : le correctif de dimensions
   n'a pas pris et le reste du protocole n'a pas de sens.

3. **Nouveau projet**, gabarit **français** avec cartouche (`A1 métrique` ou le gabarit
   d'agence). Le cartouche A1 français est le cas qui a révélé le défaut de placement —
   un gabarit anglais ne testerait rien.

4. **Poser un minimum de matière** : 2 murs parallèles distants de 7 000 mm, 2 niveaux,
   3 pièces, 1 feuille avec cartouche, 1 feuille **sans** cartouche.

5. **Presser *Écriture*** dans le panneau RiveTT. Chaque session démarre en lecture
   seule et aucun outil ne peut lever ce verrou — c'est voulu.

6. Noter les IDs : les 2 murs, les 2 niveaux, les 3 pièces, les 2 feuilles, une vue en
   plan **non active**.

---

## Consigne à donner à l'agent

> Tu testes RiveTT 0.2.0 sur cette maquette. Exécute le protocole de
> `docs/PROTOCOLE_TEST.md` dans l'ordre, section par section.
>
> Pour chaque point : note l'appel exact, le résultat obtenu, et **conclus par CONFORME
> ou NON CONFORME** au regard du résultat attendu. Ne corrige rien, ne contourne rien —
> un contournement masque exactement ce qu'on cherche à mesurer. Si un outil échoue,
> reporte le message d'erreur intégral.
>
> Trois points (§4) sont des anomalies **non reproduites** : là, ton but est de
> reproduire, pas de conclure. Si tu ne reproduis pas, dis-le.
>
> Ne lance jamais `send_code_to_revit` avec `dryRun:false`.

---

## §1 — Les correctifs de l'audit du 25/08

### 1.1 `create_dimensions`, mode éléments

```
create_dimensions(dimensions: "[{viewId:<plan actif>, elementIds:[<mur A>,<mur B>], linePoint:{x:0,y:0,z:0}}]")
```

**Attendu** : une cote est créée, et sa *Longueur totale* vaut **~7 000 mm**.
**Échec caractéristique du bug corrigé** : exactement `-0,3048 m` (= −1 pied), quelle
que soit la distance réelle. Relever la valeur, pas seulement le succès.

Puis, avec 4 murs (3 segments) : la somme doit correspondre aux écarts réels, pas à
`3 × -0,3048`.

### 1.2 `create_dimensions`, mode point à point

```
create_dimensions(dimensions: "[{viewId:<plan actif>, startPoint:{x:0,y:0,z:0}, endPoint:{x:5000,y:0,z:0}}]")
```

**Attendu** : création réussie. Le `z` fourni est **sans importance** — les points sont
projetés dans le plan de la vue.
**Échec caractéristique** : `Curve must be in the plane`.

Refaire avec `z:3000` : doit réussir aussi. Si `z:0` passe et `z:3000` échoue, la
projection ne fonctionne pas.

### 1.3 `create_preset_schedule`

Les six presets réels, un par un : `door_by_room`, `window_by_room`, `room_finish`,
`sheet_list`, `view_list`, puis `material_takeoff` **avec** `categoryName:"OST_Walls"`.

**Attendu** : six nomenclatures créées. Un `Unknown preset` sur l'un d'eux est un échec.
Tester aussi `material_takeoff` **sans** `categoryName` : doit donner une erreur qui
*nomme* le paramètre manquant.

### 1.4 `tag_rooms` et `color_elements` sur une vue non active

Sans changer de vue dans Revit :

```
tag_rooms(viewId: <une vue en plan NON active>)
color_elements(categoryName:"OST_Walls", parameterName:"Type Name", viewId: <la même>)
```

**Attendu** : les deux opèrent sur la vue désignée. Ouvrir ensuite cette vue dans Revit
pour le constater de visu. **Aucune intervention humaine ne doit être nécessaire** —
c'était le point de l'anomalie n°5.

Vérifier aussi le refus propre : `tag_rooms(viewId: <id d'un mur>)` doit répondre que
ce n'est pas une vue, pas planter.

### 1.5 `place_viewport` sur une feuille **sans** cartouche

**Attendu** : `frameOutlineMm.source` vaut `"sheetSize"` ou `"viewOutline"` (pas
`"titleBlock"`), `frameOutlineMm.known` est renseigné, et **`fitsOnSheet` vaut `null`**
si le cadre est indéterminable — surtout pas `false`.
**Échec** : `frameOutlineMm` à `[0,0]×[0,0]`.

Puis, sans `positionX`/`positionY` sur cette même feuille sans cadre : doit **refuser**
en expliquant qu'il n'y a pas de centre, et proposer `place_title_block`.

### 1.6 `place_viewport` sur une nomenclature

```
place_viewport(sheetId: <feuille>, viewId: <une nomenclature créée en 1.3>)
```

**Attendu** : le message dit que c'est une **nomenclature** et qu'elle se place par
`ScheduleSheetInstance`. **Échec** : le générique « already placed or not placeable ».

Puis placer une vue **déjà posée** sur une autre feuille : le message doit **nommer la
feuille** qui la détient.

### 1.7 `create_floor` — le dryRun existe enfin

```
create_floor(boundaryPoints:"[{x:0,y:0},{x:5000,y:0},{x:5000,y:4000},{x:0,y:4000}]")
```
sans préciser `dryRun`.

**Attendu** : **aucune dalle créée**. La réponse annonce le type et le niveau résolus,
et `approxAreaM2` ≈ **20**. Confirmer par `ai_element_filter(filterCategory:"OST_Floors")`
avant/après : le compte ne bouge pas.

Puis `dryRun:false` : la dalle est créée, et l'aire réelle correspond à l'aperçu.

### 1.8 `set_element_phase` — les deux orthographes

```
set_element_phase(requests:"[{elementId:<mur A>, createdPhaseId:<phase>}]")
set_element_phase(requests:"[{elementId:<mur B>, phaseCreatedId:<phase>}]")
```

**Attendu** : **les deux** posent la phase. Vérifier par `get_element_parameters` sur
les deux murs. La seconde orthographe est l'ancienne, conservée en alias ; si elle ne
fait rien, l'alias est cassé.

---

## §2 — Correctifs antérieurs jamais prouvés sur maquette

### 2.1 `batch_create_sheets` — le cas qui a tout déclenché

```
batch_create_sheets(sheets:"[{number:\"T-01\", name:\"Test\", viewIds:[<vue1>]}]", dryRun:false)
```
sur le **cartouche A1 français**.

**Attendu** : la vue est **dans le cadre**. L'origine du cartouche A1 français est à
650 mm à l'intérieur du cadre — c'est précisément ce qui envoyait les dessins hors
papier. Ouvrir la feuille et regarder.

Puis avec **3 `viewIds`** sur une seule feuille : elles doivent être **pavées**, pas
empilées au même point.

### 2.2 `workflow_sheet_set` — les vues arrivent-elles

```
workflow_sheet_set(sheets:"[{number:\"T-02\", name:\"Jeu\", viewIds:[<vue2>]}]", dryRun:false)
```

**Attendu** : `requestedViewCount` == `placedViewCount`, et la feuille **n'est pas
vide**. C'était le défaut critique : succès annoncé, feuilles vides.

### 2.3 `clash_detection` vs `workflow_clash_review` — même réponse

Lancer les deux sur **le même couple de catégories**.

**Attendu** : **le même `clashCount`**, et `method:"solid_geometry"` des deux côtés.
Un écart signifie que le partage de passe a régressé.

### 2.4 Les aperçus de suppression

`delete_material`, `delete_schedule`, `delete_selection` sans préciser `dryRun`.

**Attendu** : **rien n'est supprimé**, et `dependentCount` est renseigné. Vérifier
ensuite qu'avec `dryRun:false` la suppression réelle correspond à la cascade annoncée.

### 2.5 `send_code_to_revit`

Avec un script anodin, **sans** préciser `dryRun`.

**Attendu** : `sandbox:"passed"`, **rien n'est exécuté**, et **aucun fichier écrit**
dans `%LOCALAPPDATA%\RiveTT\scripts\`. Vérifier le dossier.

### 2.6 `ifc_set_family_mapping_file` derrière le verrou

Presser *Lecture seule*, puis appeler l'outil.

**Attendu** : **`PermissionDenied`**. Il traversait le verrou avant reclassement.

### 2.7 Les outils qui ne compilaient pas

`list_design_options` sur un modèle portant de vraies variantes, et
`manage_curtain_grid(action:"get_grid_info")` sur un **CurtainSystem multi-faces**.

**Attendu** : réponse correcte. Sur le multi-faces, seule la **première face** est
adressée — c'est documenté, pas un bug ; confirmer que ça ne plante pas.

---

## §3 — Le verrou d'écriture

En **Lecture seule** : un outil de lecture répond, un outil d'écriture renvoie
`PermissionDenied` **sans rien modifier**. Basculer en *Écriture* et refaire : passe.

Vérifier que `execution.writesAllowed` reflète l'état réel dans les deux cas.

---

## §4 — Anomalies NON reproduites — objectif : reproduire

### 4.1 `create_stair` lié à un `baseLevelId` précis

Signalé : échec systématique avec `baseLevelId=1910`, succès avec `608`, même géométrie.
Message : `The input locationPath is not a valid location path line for straight run`.

Procédure :
1. `get_project_info` → relever **tous** les niveaux avec id et élévation.
2. Pour **chaque paire** de niveaux consécutifs, tenter le **même** `create_stair`, en
   ne changeant que `baseLevelId`/`topLevelId`.
3. Tabuler : id de base, élévation, hauteur entre niveaux, résultat.

Ce qu'on cherche : l'échec suit-il **l'ID** (piste cache) ou **la géométrie** (hauteur
inhabituelle, niveau non-étage, élévation négative) ? Relever `isBuildingStory` et
l'élévation de chaque niveau — un niveau non-étage est un suspect plus probable qu'un
cache.

### 4.2 `get_current_view_elements` — filtre d'annotations à zéro

Signalé : `ai_element_filter` trouve 39 étiquettes de pièces, `get_current_view_elements`
avec `categoryFilter:"OST_RoomTags"` sur la même vue renvoie 0, sans catégorie non
résolue.

Procédure :
1. Se placer sur la vue qui **contient visiblement** les étiquettes.
2. `get_current_view_info` → **confirmer que la vue active est bien celle-là**.
3. `get_current_view_elements()` **sans filtre** → les étiquettes sont-elles dans la
   liste brute ?
4. Puis avec `categoryFilter:"OST_RoomTags"`, puis `annotationCategoryList:["OST_RoomTags"]`.

L'étape 3 tranche : si le sans-filtre les trouve, le bug est dans le filtrage ; s'il ne
les trouve pas non plus, c'est la portée de la vue. Relever le résultat des deux.

### 4.3 `PipeClosed` en plein lot d'écriture

Signalé pendant un lot de `create_door`. Sans trace, rien à corriger.

Si cela se reproduit : noter l'outil, la taille du lot, le dernier appel abouti, et
récupérer **`%LOCALAPPDATA%\RiveTT\audit.jsonl`** ainsi que le journal Revit
(`%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals\`). Ces deux fichiers sont
la seule matière exploitable.

---

## §5 — Installateur : ce qui reste non exercé

L'installation silencieuse est prouvée (code 0, `asInvoker`, Revit ouvert). Restent :

- **L'assistant interactif** : double-cliquer `RiveTT-Setup-0.2.0.exe`. Aucune invite
  UAC ne doit apparaître. L'écran final doit lister les versions de Revit servies.
- **`/REVIT=2026,2027`** sur un poste où Revit n'est pas installé.
- **Le refus d'un Revit 2026.0 à 2026.4** — aucun sur ce poste ; à tester ailleurs. Le
  message doit **nommer** la version trouvée et renvoyer vers Autodesk Access.
- **La désinstallation** par *Applications installées*, Revit fermé.

---

## Fiche de résultat

| § | Point | Résultat | Note |
|---|---|---|---|
| 1.1 | Cote entre éléments | | valeur relevée : |
| 1.2 | Cote point à point | | |
| 1.3 | 6 presets | | |
| 1.4 | viewId annotations | | |
| 1.5 | Feuille sans cartouche | | source : |
| 1.6 | Nomenclature refusée proprement | | |
| 1.7 | dryRun create_floor | | aire : |
| 1.8 | Alias de phase | | |
| 2.1 | Cadre A1 français | | |
| 2.2 | Vues posées | | |
| 2.3 | Parité clash | | counts : |
| 2.4 | Aperçus de suppression | | |
| 2.5 | dryRun du code | | |
| 2.6 | Verrou IFC | | |
| 2.7 | Options / mur-rideau | | |
| 3 | Verrou d'écriture | | |
| 4.1 | create_stair reproduit ? | | |
| 4.2 | Filtre annotations reproduit ? | | |
| 4.3 | PipeClosed reproduit ? | | |
