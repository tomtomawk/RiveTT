# 05 — Plans de logement : règles et contrôles (base RAG)

> **Statut des sources.** « Charte » = charte de dessin Revit de l'agence (sections 2.11 à 2.13 notamment). « Réglementaire » = arrêté du 24 décembre 2015 relatif à l'accessibilité des bâtiments d'habitation collectifs neufs, à **vérifier sur le texte en vigueur** avant usage opposable. « Pratique agence » = constat de la session, à valider par l'agence.
>
> Chaque règle porte un identifiant (`RÈGLE_…`) destiné au validateur `validate_dwelling` (voir 02 § 3.5).

## 1. Données d'entrée à obtenir AVANT de dessiner

L'agent ne doit pas inventer le programme. Demander, ou présenter des hypothèses explicites et les faire valider :

| Donnée | Exemple | Pourquoi |
|---|---|---|
| Nombre de logements, mix typologique | 14 : 4 T2, 2 T3, 1 T4 par cage… | Structure du plan |
| Surfaces cibles par typologie | T2 45, T3 65, T4 80 m² | Évite T4 de 116 m² et Sde de 2,6 m² |
| Part de logements accessibles / adaptables | 100 % accessibles (neuf, ascenseur) | Dimensionnement des pièces d'eau et chambres |
| Locaux communs | Hall, vélos, OM, local ménage | Emprise RDC |
| Contraintes de façade | Trames, balcons, orientation | Position des séjours |
| Plans types agence à réutiliser | T2 réf. X | Point de départ fiable |

## 2. Distribution du logement

| ID | Règle | Valeur | Source |
|---|---|---|---|
| `ENTREE_OUVERTE_SEJOUR` | L'entrée s'ouvre sur le séjour, sans cloison ni porte entre les deux | — | Charte |
| `ENTREE_GABARIT` | Rectangle de manœuvre libre dans l'entrée devant la porte palière | 1,20 × 2,20 m | Réglementaire / Charte |
| `ETEL_ENTREE` | ETEL dans l'entrée, en niche, fond en cloison de 10 cm (`ELC_ETEL` hébergé sur `CLO_Distribution_10`), face vers l'entrée | niche 60 × 25 cm | Charte |
| `CIRC_INT_LARGEUR` | Largeur utile des dégagements intérieurs | ≥ 0,90 m ; viser 1,00 m, pas plus | Réglementaire (0,90) / Pratique agence |
| `RATIO_CIRCULATION` | (entrée + dégagements) / SHAB | ≤ 10 à 12 % ; signaler au-delà | Pratique agence |
| `CHAMBRE_DEPUIS_DGT` | Chambres desservies par un dégagement (T3 et plus) ; en T2 l'accès depuis le séjour est admis | — | Pratique agence |
| `WC_SEPARE` | WC séparé à partir du T3 (hors WC dans Sde de l'unité de vie si accepté par le programme) | — | Pratique agence |
| `CUISINE_TYPO` | T1 : kitchenette ; T2 : évier + emplacements RF + cuisson ; T3 et plus : + linéaire | — | Charte |

## 3. Accessibilité (logement accessible, unité de vie)

| ID | Règle | Valeur | Source |
|---|---|---|---|
| `CERCLE_150_SDE` | Cercle libre dans la salle d'eau de l'unité de vie, hors débattement de porte | Ø 1,50 m | Réglementaire |
| `CERCLE_150_SEJOUR` | Cercle libre dans le séjour, hors mobilier | Ø 1,50 m | Réglementaire |
| `CHAMBRE_PMR_LIT` | Chambre de l'unité de vie : lit 1,40 × 1,90 avec passages libres | 0,90 m sur les 2 grands côtés, 1,20 m au pied — c'est le type `MOB_Lit · 140 x 190 cm_Gabarit PMR 90 x 120 x 90 cm` (emprise 3,20 × 3,10 m) | Réglementaire / Charte |
| `WC_PMR` | WC de l'unité de vie avec espace d'usage latéral | Type `SAN_WC · Suspendu PMR` | Réglementaire / Charte |
| `DOUCHE_PLAIN_PIED` | Douche sans ressaut dans l'unité de vie | Type `SAN_Douche sans ressaut · 90 x 120` | Charte |
| `PORTES_PASSAGE` | Portes intérieures et palières | Intérieures `PP 93x204`, palières `PP93x220 16` | Charte |

Conséquence de dimensionnement : une chambre adaptée fait au moins 3,20 × 3,10 m **utiles** ; une Sde adaptée avec douche 90 × 120, lavabo et cercle Ø 1,50 m tient rarement sous ~5,5 m².

## 4. Surfaces minimales de pièces

| ID | Pièce | Minimum | Source |
|---|---|---|---|
| `SURF_CHAMBRE` | Chambre | 9 m² (chambre adaptée : dimensionnée par `CHAMBRE_PMR_LIT`) | Pratique agence — à confirmer |
| `SURF_SEJOUR_T2` | Séjour / cuisine T2 | ≈ 20 m² (constat : 16,9 m² jugé insuffisant) | Pratique agence — à confirmer |
| `SURF_PIECE_PRINCIPALE` | Pièce principale | ≥ 9 m² et hauteur ≥ 2,20 m, ou volume ≥ 20 m³ | Décret décence (2002) — à vérifier |

## 5. Parties communes

| ID | Règle | Valeur | Source |
|---|---|---|---|
| `CIRC_COMMUNE_LARGEUR` | Circulation horizontale commune, largeur utile | ≥ 1,20 m ; viser 1,30 m, pas plus (1,82 m jugé excessif) | Réglementaire (1,20) / Pratique agence |
| `GAINES_PALIERES` | Gaines en encoche de 37 cm de profondeur sur le palier, ≤ 5 logements par niveau | AEP 60, Élec 73, SG 50, Télécom 50 cm | Charte |
| `ASCENSEUR_UNIQUE` | Une seule famille cabine par gaine (`EQS_Ascenseur porte` contient la cabine) | — | Retex |
| `ESCALIER` | `ESC_LGT`, hauteur de marche ≤ 17 cm, giron 28 cm, emmarchement 1,10 m | — | Charte / Gabarit |
| `LOCAL_VELOS` | Au RDC, accès direct depuis l'extérieur et depuis le hall | — | Pratique agence |

## 6. Façade et extérieurs

| ID | Règle | Source |
|---|---|---|
| `PF_ESPACE_EXT` | Toute porte-fenêtre en étage ouvre sur un balcon, une loggia ou une terrasse | Retex (balcons oubliés) |
| `PF_RDC` | En RDC, porte-fenêtre sur jardin privatif ou terrasse clôturée | Retex |
| `MENUISERIE_TETE` | Tête des menuiseries à −30 cm du niveau supérieur ; alignement des linteaux d'une même façade | Charte |
| `BALCON` | Dalle `DAL_Béton18`, dalles sur plots +100 mm, garde-corps `101ht IiiI RAL7039` | Charte / Gabarit |

## 7. Paramètres de pièce à renseigner

| Paramètre | Contenu |
|---|---|
| Nom (`ROOM_NAME`) | Vocabulaire charte : Séjour / Cuisine, Entrée, Dgt, Chambre 1…, Sde, Sdb, WC, Rgt, Cellier, Palier, Circulation, Hall, Local vélos, Gaine … |
| `ARC_PAR_NUMERO_LOGEMENT` | Identifiant du logement (ex. A2) |
| `ARC_PAR_TYPOLOGIE` | T1 à T5 |
| `ARC_PAR_BATIMENT` | Lettre du plot |
| `ARC_PAR_SURFACE_LOGEMENT` | Somme des pièces du logement ; **non dynamique**, à recalculer après modification |

Une étiquette `ETQ_Pièce typo · 2` par logement, sur le séjour, dans la vue en plan au nom du niveau (voir charte § 11). Pour une SHAB opposable, utiliser `manage_area_plans`.

## 8. Contrôle avant livraison (checklist)

Pour chaque logement :
- [ ] Entrée ouverte sur séjour, gabarit 1,20 × 2,20 contenu dans l'entrée
- [ ] ETEL présent, sur cloison de 10, tourné vers l'entrée
- [ ] Dégagements 0,90 à 1,00 m ; ratio circulation ≤ 12 %
- [ ] Cercle Ø 1,50 m libre en Sde et séjour ; lit PMR et passages contenus dans la chambre adaptée
- [ ] Surfaces minimales respectées ; surface totale conforme au programme
- [ ] Mobilier et sanitaires à l'intérieur de leur pièce, au niveau, sans collision
- [ ] Toutes les pièces fermées, nommées, paramètres ARC_PAR_* renseignés

Pour chaque niveau :
- [ ] Circulation commune 1,20 à 1,30 m ; gaines palières présentes
- [ ] Une seule cabine d'ascenseur ; escalier continu, paliers présents
- [ ] Portes-fenêtres sur espace extérieur
- [ ] `list_warnings` relu ; capture du plan contrôlée

## 9. Plans de la session de test (références, non validées par l'agence)

Plot A 18 × 12 m (murs enduit 38 cm), circulation centrale 1,30 m ; plot B 14 × 11 m (bardage 45 cm).

| Logement | Typo | Surface | Distribution |
|---|---|---|---|
| A1 | T3 | 87,9 m² | Nuit au sud (2 ch.), bande humide centrale (Sde, WC, Rgt), dgt 1,00 m, entrée ouverte, séjour au nord — surfaces un peu élevées |
| A2 / A3 | T2 | 47,6 m² | Entrée + Sde côté palier, séjour central traversant la largeur, chambre PMR en bout |
| B1 | T4 | 89,1 m² | 3 ch. + séjour au sud, bande de services au nord (cellier, Sde, WC, Sdb, rgt), dgt 1,00 m — ratio circulation ≈ 19 %, à améliorer |
| B2 | T2 | 41,6 m² | Entrée ouverte, séjour, dgt + Sde, chambre |
| A4 | T3 | 72,5 m² | Attique, variante de A1 |
| A6 | T1 | 28,6 m² | Attique, entrée + séjour/kitchenette + Sde |

Défauts constatés par l'architecte sur les versions successives (à ne pas reproduire) : entrées fermées, ETEL et gaines absents, circulations > 1,30 m, espaces perdus dans les logements, ascenseur en double, balcons manquants devant des portes-fenêtres, mobilier hors pièce ou flottant.
