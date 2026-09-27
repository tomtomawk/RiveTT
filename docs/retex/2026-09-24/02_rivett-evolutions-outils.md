# 02 — Évolutions des outils RiveTT

## 1. Constat d'usage (session de test)

| Catégorie | Nombre | Détail |
|---|---|---|
| Outils utilisés | 7 / ~200 | `send_code_to_revit` (~35 appels, ~60 %), `save_document` (~10), `create_stair` (4), `get_project_info` (2), `create_document` (2), `get_selected_elements` (1), `tool_search` (5) |
| Outils existants ignorés à tort | ~12 | Voir § 2 |
| Outils manquants | ~9 | Voir § 3 |

### Pourquoi `send_code_to_revit` a tout absorbé

- **Volume** : ~300 murs, 160 portes, 220 éléments de mobilier, 90 pièces. Outil par outil : plus de 700 appels ; en script : 5.
- **Composition** : poser, trouver l'hôte, retourner, vérifier dans le même appel.
- **Pas de chargement de schéma** (`tool_search`) : l'agent connaît l'API Revit.

### Ce qu'il a coûté

- Chaque erreur, même sur une ligne, oblige à renvoyer le script entier (6 fois pour 150 à 300 lignes).
- Rollback tout-ou-rien : une ETEL en échec annule tout le plan.
- Aucune règle métier ni garde-fou : l'agent porte seul les pièges (points d'insertion, altitudes).

**Conclusion** : il manque un niveau intermédiaire — des outils dédiés **par lots** (liste en entrée, une transaction, rapport d'échec par élément) qui couvriraient ~80 % de ce qui a été fait en C#.

## 2. Outils existants à mieux exposer

Ces outils auraient évité des erreurs. Ils n'ont pas été utilisés parce que, chargés à la demande, ils sont invisibles tant que l'agent ne les cherche pas.

| Outil | Ce qu'il aurait évité |
|---|---|
| `batch_export` (IMAGE) | Travail à l'aveugle — à condition que l'image remonte (voir 03) |
| `list_warnings`, `check_model_health` | Avertissements jamais regardés (murs superposés, pièces redondantes…) |
| `manage_area_plans` | SHAB réglementaire au lieu d'une somme de pièces |
| `get_room_openings`, `export_room_data` | Contrôle des surfaces et ouvertures sans script |
| `create_room_separation_line` | Recodée en C# |
| `list_family_types`, `list_system_types` | Inventaire recodé 3 fois |
| `find_untagged_elements` | Contrôle des étiquettes |
| `activate_view` | Montrer le résultat à l'utilisateur en fin d'étape |
| `create_material`, `duplicate_system_type`, `set_compound_structure` | Faits en C# |
| `mcp__Epure__creer_tutoriel` | Publier la charte dans la bibliothèque agence |

**Action** : ajouter en tête du `SKILL.md` une **carte des outils par tâche** (voir 07 § 3).

## 3. Outils à créer

Priorité : 1 = bloquant pour la qualité, 2 = gain fort, 3 = confort.

### 3.1 `capture_view` — priorité 1

Voir la spécification complète dans `03_rivett-capture-view.md`.

### 3.2 Versions « lot » des outils de création — priorité 1

```jsonc
// create_walls_batch
{
  "items": [
    { "key": "A_N", "typeName": "MUR_Béton20 / enduit2 / iso14+placo2_38",
      "p0": [0, 12000], "p1": [18000, 12000],
      "baseLevel": "RDC", "topLevel": "R+3", "topOffsetMm": 0,
      "locationLine": "FinishFaceExterior", "exteriorSide": "left" }
  ],
  "mode": "bestEffort"          // ou "atomic"
}
// retour
{ "created": [{ "key": "A_N", "id": 10639950 }],
  "failed":  [{ "key": "B_O", "error": "…", "relatedIds": [10639959, 10640392] }],
  "warnings": [ … ] }
```

Même principe pour `create_doors_batch` (hôte trouvé par point, altitude gérée par l'outil), `create_rooms_batch` (nom, paramètres ARC_PAR_*), `create_floors_batch`, `place_families_batch`.

Règles communes :
- `key` fourni par l'agent, renvoyé dans le résultat : permet de corriger un seul élément.
- `mode: "bestEffort"` : valide ce qui passe, liste les échecs avec les identifiants en cause.
- Coordonnées en mm, niveaux par nom ou id, types par nom exact.

### 3.3 `describe_family` — priorité 2

```jsonc
// entrée
{ "familyName": "MOB_Lit", "typeName": "140 x 190 cm" }
// retour
{
  "placement": "OneLevelBased",        // OneLevelBasedHosted, TwoLevelsBased…
  "zRule": "relativeToLevel",          // "absolute" pour les hébergées
  "originLocal": [0, 0],
  "frontDirection": [0, -1],           // face avant en coordonnées famille
  "visibleExtentMm": { "x": [1161, 2656], "y": [-1900, 0] },   // solides visibles seulement
  "clearanceExtentMm": { "x": [...], "y": [...] },            // gabarits / espaces d'usage
  "boundingBoxMm": { "x": [...], "y": [...] },                // ce que renvoie get_BoundingBox
  "hostCategory": null
}
```

Motif : la boîte englobante Revit inclut les gabarits invisibles (lit 1,50 × 1,90 m → 2,85 × 3,90 m). Sans ce descriptif, l'agent place faux.

### 3.4 `place_in_room` — priorité 2

```jsonc
{ "roomId": 10642999, "familyName": "SAN_WC", "typeName": "Suspendu PMR",
  "rotationDeg": 270, "against": ["E", "S"], "marginMm": 15, "anchor": "center" }
```

L'outil pose, tourne, centre l'emprise visible sur l'ancre, plaque aux murs demandés, recadre dans la pièce, contrôle les collisions avec les autres équipements, et renvoie la position finale et les conflits.

### 3.5 `validate_dwelling` — priorité 1

Vérifie un logement (ensemble de pièces ayant le même `ARC_PAR_NUMERO_LOGEMENT` sur un niveau) contre les règles de `05_rag-plan-logement.md`.

```jsonc
// entrée
{ "level": "R+1", "dwelling": "A2", "rules": "charte-club-2026" }
// retour
{
  "typology": "T2", "shabM2": 47.6,
  "checks": [
    { "rule": "ENTREE_OUVERTE_SEJOUR", "ok": true },
    { "rule": "ETEL_PRESENT", "ok": true, "elementId": 10643120 },
    { "rule": "CERCLE_150_SDE", "ok": false, "detail": "libre 1,38 m entre douche et lavabo" },
    { "rule": "RATIO_CIRCULATION", "ok": false, "value": 0.19, "max": 0.12 }
  ]
}
```

L'agent n'a plus à « se souvenir » de la charte : le validateur la porte.

### 3.6 `edit_toposolid` — priorité 3

Noue, talus, plateforme à partir de courbes : `{ curves | curveElementIds, mode: "contour"|"axis", depthMm, bottomWidthMm, slope, longitudinalSlopePct, outletEnd }`. Aujourd'hui fait par `SlabShapeEditor` en C#.

### 3.7 Autres

| Outil | Besoin |
|---|---|
| `attach_walls` | Attache haut/bas avec diagnostic nominatif des éléments en conflit |
| `tag_rooms` + `tagTypeId`, filtre de pièces | Étiquettes de typologie (un seul type, sur une seule pièce par logement) |
| `get_selected_elements` + géométrie | Courbes, points, boîtes des éléments sélectionnés, même en lecture seule |
| `create_shaft` / gaines palières | Encoche 37 cm, largeurs par nombre de logements |

## 4. Corrections et améliorations de `send_code_to_revit`

| # | Évolution | Motif |
|---|---|---|
| 1 | **Scripts enregistrés et paramétrés** : `save_script(name, code)`, `run_script(name, args)` | Ne plus renvoyer 300 lignes pour corriger une ligne |
| 2 | **Bibliothèque de fonctions communes** injectée : `Mm()`, `Pt(x,y,z)`, `Rect()`, `FindHostWall(x,y,level)`, `PlaceFamily(...)`, `LevelByName()` | Réécrites à chaque script |
| 3 | **`checkpoint()` / mode best effort** | Valider section par section ; ne plus perdre tout le travail sur une erreur |
| 4 | **Erreurs exploitables** : identifiants des éléments en échec, numéro de ligne, messages regroupés (« ×77 ») | « Impossible de couper l'ETEL » ×77 sans id ni ligne |
| 5 | **Lecture pendant le verrou** : `transactionMode: readonly` autorisé même si l'écriture est verrouillée | Lecture refusée au démarrage |
| 6 | **Mode `manual` réel**, sans transaction englobante | `StairsEditScope` impossible |
| 7 | **Filtre anti-réflexion ciblé** sur les appels réellement dangereux | `.GetType().Name` dans une chaîne de log bloqué |
| 8 | **Retour structuré** : autoriser `return new { … }` sérialisé en JSON | Tout est aujourd'hui concaténé en texte |

## 5. Ergonomie générale du serveur

- **Bloc `execution`** : identique à chaque réponse (~15 lignes). Ne le renvoyer que s'il change (document, verrou, mode), ou le réduire à une ligne.
- **Dry-run** : indiquer ce qu'il ne vérifie pas. `create_stair` annonçait « 1 palier automatique » alors que l'exécution l'a raté.
- **Messages d'erreur** : prendre modèle sur ceux du verrou et du dossier cible (cause + action). Enrichir les messages Revit relayés avec les ids concernés (`FailureMessage.GetFailingElementIds()`, `GetAdditionalElementIds()`).
- **Carte des outils** en tête du guide (voir 07).

## 6. Synthèse des priorités

| Priorité | Évolution | Gain attendu |
|---|---|---|
| 1 | `capture_view` | Supprime la plupart des erreurs non vues |
| 1 | `validate_dwelling` | La charte devient un contrôle, plus un texte à relire |
| 1 | Outils par lots + mode best effort | ~−20 % d'appels et de tokens, corrections ciblées |
| 2 | `describe_family` / `place_in_room` | Fin des erreurs de mobilier |
| 2 | Scripts paramétrés, `checkpoint()`, erreurs avec ids | Moins de renvois complets |
| 2 | Correction de `create_stair` | Voir 04 |
| 3 | `edit_toposolid`, `attach_walls`, géométrie de sélection, `tag_rooms` enrichi | Blocages ponctuels levés |
