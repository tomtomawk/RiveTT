# 04 — Bugs RiveTT et pièges de l'API Revit (RiveTT 0.5.4, Revit 2026)

Format : symptôme → cause identifiée ou probable → contournement → correction souhaitée.

## 1. Bugs RiveTT

### 1.1 `create_stair` à 2 volées : 2ᵉ volée au sol, pas de palier

- **Symptôme** : 2 volées demandées (RDC → R+1, 2 890 mm). Résultat : 9 contremarches au lieu de 17 ; les deux volées vont de 0 à 1 530 mm. Avertissement « palier non créé, composants non connectés ». Le dry-run annonçait pourtant « 1 palier automatique, 17 contremarches ».
- **Cause probable** : chaque volée créée avec `BaseElevation = 0` ; pas de `StairsLanding.CreateAutomaticLanding` réussi.
- **Contournement** :
  1. Créer l'escalier du premier niveau, puis recaler la 2ᵉ volée : `TopElevation` puis `BaseElevation` (dans cet ordre, sinon exception).
  2. Poser le palier intermédiaire en dalle (`DAL_Béton18`, décalage = hauteur de la 1ʳᵉ volée).
  3. Copier l'escalier aux étages (`ElementTransformUtils.CopyElement`) et réaffecter niveau bas / niveau haut : Revit recalcule la hauteur de marche (170 → 160 mm pour 2 720 mm).
- **Pièges rencontrés** : les setters `StairsRun.TopElevation` / `BaseElevation` sur un escalier dont le niveau bas n'est pas à 0 ont renvoyé des erreurs incohérentes (« topElevation is less than the extension below base ») et, une fois, ont produit 35 et 49 contremarches. Comportement non maîtrisé : ne pas utiliser sur les étages.
- **Correction souhaitée** : empiler les volées en altitude, créer le palier dans le `StairsEditScope`, faire échouer le dry-run si le palier est impossible, retourner le nombre de contremarches réel.

### 1.2 `send_code_to_revit` en mode `manual` : transaction déjà ouverte

- **Symptôme** : « Starting a new transaction is not permitted » ; `StairsEditScope` impossible.
- **Correction souhaitée** : un vrai mode sans transaction englobante.

### 1.3 Rollback tout-ou-rien, messages sans identifiants

- **Symptôme** : « Impossible de couper l'occurrence de ETEL du mur » ×77, tout le script annulé, aucun id ni numéro de ligne.
- **Cause réelle** (ici) : familles hébergées posées avec Z = 0 au lieu de l'altitude du niveau (voir § 2.2).
- **Correction souhaitée** : ids des éléments en échec (`GetFailingElementIds`), regroupement des messages, `checkpoint()`.

### 1.4 Filtre de sécurité de `send_code`

- **Symptôme** : `(c is Line?…)` accepté mais `c.GetType().Name` refusé comme « réflexion ».
- **Correction souhaitée** : cibler les appels dangereux (`Assembly.Load`, `Activator`, `MethodInfo.Invoke`…), pas `GetType()`.

### 1.5 Verrou au démarrage

- **Symptôme** : après redémarrage de Revit, verrou fermé (`lockedBy: startup`) ; même `send_code` en `readonly` refusé.
- **Correction souhaitée** : autoriser la lecture ; garder le verrou pour l'écriture (comportement voulu).

### 1.6 `get_selected_elements` sans géométrie

- **Symptôme** : ids et catégories seulement ; impossible de lire des lignes de détail tant que le verrou est fermé.
- **Correction souhaitée** : courbes (extrémités, longueur), points, boîtes.

### 1.7 `batch_export` IMAGE ne renvoie pas l'image

Voir `03_rivett-capture-view.md`.

## 2. Pièges de l'API Revit rencontrés

### 2.1 `Wall.Orientation` juste après `Wall.Create`

- Exception tant que le document n'est pas régénéré → `document.Regenerate()` avant lecture.

### 2.2 Altitude du point d'insertion : deux règles opposées

| Type de famille | Appel | Z du point |
|---|---|---|
| Basée sur un niveau (mobilier, sanitaires, vélos) | `NewFamilyInstance(point, symbole, niveau, …)` | **0** (relatif au niveau). Sinon l'altitude est comptée deux fois : +2,89 m au R+1, +5,61 m au R+2… |
| Hébergée par un mur (portes, fenêtres, ETEL, portes d'ascenseur) | `NewFamilyInstance(point, symbole, mur, niveau, …)` | **Altitude absolue du niveau**. Sinon « impossible de couper l'occurrence du mur ». |

Contrôle : après pose, remettre à 0 `INSTANCE_FREE_HOST_OFFSET_PARAM` pour les familles basées sur un niveau, et vérifier `|bbox.Min.Z − niveau.Elevation| < 300 mm`.

### 2.3 Boîte englobante ≠ emprise visible

`get_BoundingBox(null)` inclut les gabarits et lignes invisibles de la famille. Mesures relevées :

| Type | Boîte Revit | Emprise visible |
|---|---|---|
| Lit 140 × 190 | 2 850 × 3 900 | 1 495 × 1 900 |

Méthode : parcourir `get_Geometry(options)` → `GeometryInstance.GetInstanceGeometry()` → solides de volume > 0 → arêtes tessellées → min/max X/Y.

### 2.4 Attache d'un mur à un toit

- **Symptôme** : « Impossible de conserver le joint entre le mur et toit » pour le seul mur ouest du plot B.
- **Cause** : le débord de 30 cm du toit pénétrait les murs du noyau, plus hauts. `UnjoinGeometry` et `DisallowWallJoinAtEnd` n'y ont rien fait.
- **Contournement** : recréer le toit sans débord côté noyau, puis `wall.AddAttachment(roofId, AttachmentLocation.Top)` sur les 4 murs.
- **Méthode de diagnostic** : attacher mur par mur pour trouver le fautif.

### 2.5 `FootPrintRoof` : type introuvable par nom exact

- `T(typeof(RoofType), "TOI_Zinc1+volige2+chevron8")` a renvoyé null (écart de nom). Rechercher par préfixe (`StartsWith("TOI_Zinc")`) puis contrôler.

### 2.6 `SlabShapeEditor` d'un toposolid

- Après `ResetSlabShape()`, l'éditeur est désactivé : relire `GetSlabShapeEditor()` et appeler `Enable()` avant `AddPoints()`.
- `ResetSlabShape()` efface **toutes** les modifications de forme du terrain : ne l'appeler que si le terrain était plat ou avec accord.

### 2.7 Familles d'ascenseur

- `EQS_Ascenseur porte` (hébergée par le mur de gaine) **contient déjà la cabine** (emprise 1 650 × 2 030 mm). Poser aussi `EQS_Ascenseur` produit une cabine en double.

### 2.8 Pièces et séparations

- `NewRoomBoundaryLines(sketchPlane, curves, view)` exige une vue en plan du niveau ; créer un `SketchPlane` à l'altitude du niveau.
- `Room.Name` renvoie « nom + numéro » ; lire `ROOM_NAME` pour le nom seul.

### 2.9 Étiquettes de pièce

- `NewRoomTag(new LinkElementId(roomId), new UV(x, y), viewId)` pose le type par défaut ; `ChangeTypeId` ensuite.

### 2.10 Divers

- `new[]{ {1.0,2}, {3,4} }` : tableau implicite refusé par le compilateur si les types sont mélangés ; utiliser `double[,]`.
- `Railing`, `RailingType`, `Stairs`, `StairsRun`, `Room` sont dans `Autodesk.Revit.DB.Architecture` ; `StairsEditScope` est dans `Autodesk.Revit.DB`.
