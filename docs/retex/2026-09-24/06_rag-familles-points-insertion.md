# 06 — Familles du gabarit 2026 : origine, orientation, emprise, pose (base RAG)

Relevé du 23/09/2026 sur les solides de la géométrie des familles, en coordonnées famille (mm, rotation 0).

## 1. Conventions communes

- **Face avant vers −Y** ; dos (côté mur) sur Y = 0 pour les éléments adossés.
- Rotation autour de Z (sens trigonométrique) : 90° → avant vers +X ; 180° → vers +Y ; 270° → vers −X.
- **Ne jamais supposer l'origine au centre.**
- **Ne jamais utiliser `get_BoundingBox` comme emprise** : elle inclut gabarits et lignes invisibles.

## 2. Catalogue

| Famille · type | Placement | Emprise X (mm) | Emprise Y (mm) | Origine | Pose recommandée |
|---|---|---|---|---|---|
| `MOB_Lit · 140 x 190 cm` | Niveau | 1161 → 2656 | −1900 → 0 | Tête du lit, décalée en X | Tête contre mur ; centrer l'emprise visible |
| `MOB_Lit · 140 x 190 cm_Gabarit PMR 90 x 120 x 90 cm` | Niveau | 0 → 3200 | −3100 → 0 | Angle du gabarit | La chambre doit contenir 3,20 × 3,10 m |
| `MOB_Lit · 140 x 190 cm_Gabarit PMR 90 x 120 cm` | Niveau | à relever | à relever | — | Variante plus compacte |
| `SAN_Douche sans ressaut · 90 x 120` | Niveau | 5 → 1305 | −1705 → 21 | Angle haut-gauche | Dans l'angle de deux murs |
| `SAN_WC · Suspendu PMR` | Niveau | −193 → 1137 | −1100 → 200 | Axe cuvette, sur le mur | Espace d'usage inclus côté +X |
| `SAN_WC · Suspendu` | Niveau | à relever | à relever | Axe cuvette | Dos au mur |
| `SAN_Lavabo · lavabo_60x55x85 cm_meuble vasque` | Niveau | −300 → 300 | −550 → 0 | Milieu du dos | Dos au mur |
| `SAN_Meuble évier 120 · 120x65`, `SAN_Kitchenette · 120x65` | Niveau | −555 → 645 | −650 → 0 | Dos, décalé de 45 mm | Dos au mur |
| `ELC_Emplacement électroménager · RF / Cuisson / LL / LV` | Niveau | à relever | à relever | Dos | Aligner avec l'évier |
| `MOB_Linéaire supplémentaire` | Niveau | −155 → 245 | −600 → 0 | Dos | Dos au mur |
| `MOB_Canapé générique`, `MOB_Table` | Niveau | centré | centré | Centre | Point au centre |
| `MGN_Cercle PMR 150 · 1.50m` | Niveau | −750 → 750 | −750 → 750 | Centre | Centre de la zone libre |
| `MGN_Gabarit PMR · 120x220` | Niveau | lignes 2D seules | — | Non relevé | Contrôler en plan ; tourner de 90° si l'entrée est plus large que longue |
| `PRK_Vélo · Vélo au sol` | Niveau | −375 → 375 | 0 → 2000 | Roue arrière | Longueur vers +Y |
| `SAN_Baignoire rectangulaire · 70 x 170 cm_blanc` | Niveau | à relever | à relever | — | — |
| `EQS_Ascenseur porte · 630KG` | Hébergée (mur de gaine) | 1650 × 2030 (cabine incluse) | — | Axe de porte | **Ne pas ajouter `EQS_Ascenseur`** |
| `ELC_ETEL · ETEL` | Hébergée (mur) | — | — | — | Sur `CLO_Distribution_10`, vérifier `FacingOrientation`, `flipFacing()` sinon |
| Portes `PTE_Porte simple`, `PTE_Porte palière`, fenêtres `FEN_*` | Hébergées (mur) | — | — | Axe de la baie | Z = altitude absolue du niveau |

## 3. Méthode de pose robuste (en attendant `place_in_room`)

1. Poser l'occurrence au point visé, **Z = 0** pour une famille basée sur un niveau (altitude absolue pour une famille hébergée).
2. Remettre à 0 `INSTANCE_FREE_HOST_OFFSET_PARAM` si modifiable.
3. `Regenerate()`, puis rotation autour de l'origine.
4. Mesurer l'**emprise visible** (solides de volume > 0 de `GetInstanceGeometry()`).
5. Déplacer pour que le centre de l'emprise tombe sur le point visé.
6. Plaquer aux murs demandés (N/S/E/O) à 15 mm de la limite de la pièce (`room.get_BoundingBox`).
7. Recadrer dans la pièce ; signaler toute emprise plus grande que la pièce (indicateur de non-conformité PMR).
8. Contrôler : `|bbox.Min.Z − niveau.Elevation| < 300 mm` pour toutes les occurrences.

Limites : la boîte d'une pièce en L déborde de son contour réel ; contrôler en plan.

## 4. Famille inconnue

Poser une occurrence témoin, relever l'emprise visible par rapport à l'origine et la face avant, supprimer le témoin, puis poser en série. Ajouter la famille à ce catalogue.
