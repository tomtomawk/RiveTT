# RiveTT 0.6.1

Version corrective issue de la session du 27/09/2026 : un étage type (R+1, un T3 et un T2
autour d'un noyau escalier-ascenseur) dessiné avec RiveTT 0.6.0 sur Revit 2026, projet vierge
du gabarit agence, en n'utilisant que les outils dédiés et les nouveaux outils de la 0.6.0.
Les deux logements construits sont conformes à `validate_dwelling` ; six défauts de l'outillage
ont été trouvés en chemin, dont trois produisaient un résultat faux **sans le dire**.

## 1. Défauts corrigés

| # | Outil | Constaté en session | Correction |
|---|---|---|---|
| 1 | `create_room_separation_line` | `z` pris comme altitude absolue du plan d'esquisse : `z = 0` dans le plan du R+1 posait les lignes à l'altitude du RDC ; les pièces du R+1 n'étaient pas coupées (séjour + entrée + Dgt fusionnés, 30,9 m² au lieu de 24,9), aucun avertissement | Les lignes sont tracées à l'altitude du niveau du plan (`plan.GenLevel`) ; le `z` du chemin est ignoré et signalé s'il diffère ; `levelName` et `elevationMm` dans la réponse |
| 2 | `create_stair` | Niveaux non ronds (5 610,000000000197 − 2 889,99999999999 = 2 720,0000000002 mm) : l'aperçu promettait 16 contremarches de 170, Revit en a exigé 17 de 160 et l'escalier s'est arrêté sous le R+2 ; après correction des volées, la réponse réelle affichait encore « raccourcir les volées » | L'estimation prend le plafond strict, comme Revit, sur le rapport calculé en pieds, et signale une hauteur posée sur une limite de contremarche. L'appel réel ne recopie plus l'estimation : il se fie à `DesiredRisersNumber` |
| 3 | `place_in_room` | « Contre N » visait le Y maximal de toute la pièce : dans un séjour en L, l'évier mordait de 35 mm dans la cloison. La douche sans ressaut (receveur sans volume) était calée sur son seul accessoire : point d'insertion 100 mm dans le mur extérieur, `insideRoom: true`. Marge par défaut de 15 mm visible en double trait à 1:50 | Premier mur rencontré dans la bande de l'emprise (`PlanGeometry.FirstBoundary`, testé sans Revit) ; `footprintBasis: visible \| plan \| full` ; avertissement quand les volumes couvrent moins de la moitié de ce que le plan dessine et quand le point d'insertion sort de la pièce ; `marginMm` par défaut à 0 |
| 4 | `capture_view` | Correspondance pixels/mm fausse de 21 à 27 % dès que l'image n'a pas les proportions du cadrage (moyenne des deux échelles) ; les annotations hors cadrage (trait de coupe) élargissaient l'image de façon asymétrique | Échelle = la plus grande des deux (ajustement à la page), marges centrées, `cropPx` situe le cadrage dans l'image ; décalages du cadrage des annotations ramenés au minimum sur la vue temporaire. `verified` reste `false` |
| 5 | `create_point_based_element` | `facingFlipped` / `handFlipped` n'étaient appliqués qu'aux portes et fenêtres : sur un ETEL, la demande était ignorée et la réponse rendait `false` sans raison | Appliqués à toute catégorie quand la famille le permet (`CanFlipFacing`, `CanFlipHand`), avertissement nommant la famille sinon ; plus d'appel à `flipFacing` sans contrôle |
| 6 | `describe_family` | Emprises mesurées autour de l'origine géométrique de la famille (`GetTransform`) alors que la réponse annonçait « insertion point at 0,0,0 » : décalage de 44,7 mm sur l'évier, le RF, la cuisson et le linéaire du gabarit (RF posé 44,7 mm dans l'évier), 200 mm sur le WC suspendu. Sans instance posée : aucune géométrie pour le lit et l'ascenseur | Mesure depuis le point d'insertion (`LocationPoint`, axes de l'instance) ; `geometryOriginOffsetMm` ; un type posé sur niveau sans instance est posé puis annulé pour la mesure, comme la vue temporaire de `capture_view` |

## 2. Icône du serveur

- `rivett-server.ico` régénéré par `tools/make-server-icon.py` : BMP 32 bits de 16 à 64 px
  (16 et 32 px repris des images dessinées du ruban), PNG seulement en 256 px. La 0.6.0 ne
  contenait que des PNG, format que les petites tailles ne garantissent pas partout.
- `AssemblyTitle` → `FileDescription` : « RiveTT — serveur MCP pour Revit », le nom affiché
  sur la ligne du processus au lieu de « RiveTT.Server ».
- Mesuré sur le poste : l'exécutable 0.6.0 installé portait déjà l'icône (`SHGetFileInfo` et
  `ExtractIconEx` rendent le rivet en 16 px). Le serveur est lancé par Claude, application
  MSIX : le gestionnaire des tâches le range sous la ligne de Claude, qui peut afficher
  l'icône du paquet. **À vérifier à l'œil après installation** ; si l'icône du paquet
  l'emporte, aucun réglage de l'exécutable ne peut le changer.

## 3. Documentation

`SKILL.md` : familles du gabarit relevées (ETEL en niche de `CLO_Distribution_7`, jamais sur la
cloison séparative ; décalage de 44,7 mm des éléments de cuisine ; bâti du WC suspendu et coffre
de 200 mm ; douche sans ressaut à poser sur son emprise en plan ; famille `PTE_Porte pallière`),
conventions de `create_room_separation_line`, `create_stair`, `capture_view`, `describe_family`
et `place_in_room`. Inventaire régénéré.

## 4. Vérifié en session Revit 2026 (27/09/2026, serveur et plugin 0.6.0)

Points du complément 0.6.0 de `CHANGELOG_0.3.0.md` §6 exercés sur maquette :

| Point | Résultat |
|---|---|
| `create_stair` deux volées R+1 → R+2, U | La 2ᵉ volée part du haut de la 1ʳᵉ (1 440 mm), palier créé, 17/17, `reachesTopLevel: true` — après allongement d'une volée (défaut 2) |
| `create_point_based_element` | Portes, fenêtres, ETEL, porte d'ascenseur et électroménager en lots : hôte trouvé par `findHost`, `levelName` + `familyName`/`typeName`, `zCorrectedByMm: -2890` sur chaque famille posée sur niveau au R+1 |
| `send_code_to_revit` `readonly` | `modelKept: false`, lecture des lignes de séparation et des points d'insertion ; ligne de l'exception rapportée ; script enregistré même en échec ; `fromScript` + `edits` appliqué |
| `capture_view` | Plans recadrés, `highlightIds`, 3D `ShadingWithEdges` + `isolateCategories` reçus en image par le client ; correspondance fausse (défaut 4) |
| `describe_family` | WC, douche, ETEL, évier mesurés ; lit et ascenseur sans géométrie avant pose (défaut 6) |
| `place_in_room` | Lavabos, WC, lits PMR, éviers posés ; pièce en L et douche fautives (défaut 3) |
| `attach_walls` | 11 cloisons attachées sous le plancher R+2, aperçu fidèle, hauteur réelle 2 460 mm lue sur le solide |
| `validate_spec` / `validate_dwelling` | Spec conforme avant construction ; construit conforme, surfaces à 0,01 m² de la spec |
| `tag_rooms` | 13 étiquettes, type par défaut de la vue |

Non exercés : `get_selected_elements`, `tag_rooms` avec `tagTypeId` et `onePerParameter`,
`test_image_relay` hors Claude, Revit 2027. Les correctifs 0.6.1 eux-mêmes n'ont été vérifiés
que par les tests (777 dont 3 ignorés) et la compilation contre les DLL Revit 2026 et 2027 : à
rejouer sur maquette (§6 de `CHANGELOG_0.3.0.md`, complément 0.6.1).

## 5. Constats de conception laissés au projet

- Le bâti du WC suspendu (200 mm) traverse une cloison de 100 mm : ni Revit ni
  `validate_dwelling` ne le voient (le bâti n'est pas un volume visible).
- Dans une Sde de 2,20 m de profondeur, douche 90 × 120 + zone d'usage + WC suspendu en
  coffre ne tiennent pas dans la même travée (2,40 m nécessaires).
- Les cercles de 1,50 m peuvent empiéter sur un receveur de plain-pied : le validateur ne
  déduit que les volumes visibles. Règle à trancher par l'agence.

## 6. Vérification

    dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release
    dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release -p:RevitVersion=2026
    dotnet build .\RiveTT.sln -c Release
    python tools/audit-tool-surface.py
