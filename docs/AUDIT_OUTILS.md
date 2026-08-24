# Audit de la surface d'outils — 24 août 2026 (mis à jour le 24 août 2026 après retrait Rebar/Steel)

Ce document est la lecture de l'inventaire, pas l'inventaire lui-même : le détail
outil par outil est dans [INVENTAIRE_OUTILS.md](INVENTAIRE_OUTILS.md), généré par
`tools/audit-tool-surface.py`. Version auditée : connecteur **0.2.0**.

**Depuis le premier audit** : les modules Rebar et StructuralSteel (112 outils, 38 %
de la surface d'alors) ont été **retirés entièrement** du dépôt, pas seulement
filtrés — chantier n°1 ci-dessous est donc terminé, en plus radical que prévu.
Les 19 capacités manquantes listées plus bas ont depuis reçu un outil (17) ou une
réponse structurée "non supporté" documentée (2) ; voir l'historique Git pour le
détail lot par lot. Aucune de ces additions n'a été compilée dans cet environnement
(pas de Windows/Revit ici) — à valider par un build avant toute mise en production.

L'audit est fait par extraction des sources — attributs `[McpServerTool]` du
serveur MCP croisés avec les classes `ICortexTool` du runtime — et non à la main.
Reproductible, et il a trouvé des choses qu'une relecture n'aurait pas vues.

Même matière en page filtrable, dans le dépôt :
[inventaire.html](inventaire.html) — un seul fichier, polices système, aucune
requête réseau, recherche et filtres en JavaScript inline. Il s'ouvre hors ligne
depuis le dépôt et se régénère avec le Markdown :

```powershell
python tools/audit-tool-surface.py
```

Les deux sorties viennent des mêmes données et sont versionnées ; le gabarit est
`tools/inventory-template.html`.

## Les chiffres qui commandent la suite

| Mesure | Valeur | Ce qu'elle implique |
|---|---|---|
| Outils publiés | **196** | dont 193 classes runtime |
| Écritures | **135** (69 %) | c'est la part de la surface que le verrou d'écriture du ruban gouverne |
| Ferraillage + charpente métallique | **0** (0 %) | retirés du dépôt — chantier n°1 terminé |
| Écritures sans `dryRun` | **86** | alors que le contrat annonce `dryRunDefault: true` |
| Erreurs génériques `Failed: …` | **134** | échouent sans dire quoi corriger |
| Défauts confirmés / signaux | **8** / **9** | les confirmés sont lus dans le code, pas déduits — à revérifier, ce chiffre date d'avant les lots 1-8 |

## Les 8 défauts confirmés

1. **`workflow_sheet_set` ignore `viewIds`** — la spec publiée annonce
   `[{number, name, viewIds?}]`, la boucle runtime ne lit que `number` et `name` :
   les feuilles sortent vides, sans aucun signalement. *(critique)*
2. **`batch_create_sheets` place les fenêtres à (0,5 ft ; 0,5 ft) en dur** — or
   l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le
   cartouche A1 français. Même défaut que celui corrigé dans `place_viewport`,
   non propagé. *(critique)*
3. `workflow_clash_review` détecte en **boîtes englobantes** quand
   `clash_detection` utilise l'intersection solide : l'outil composé rend plus de
   faux positifs que l'outil simple.
4. `send_code_to_revit` **sans `dryRun`** — l'outil le plus puissant du
   connecteur n'a pas d'aperçu.
5. `delete_selection`, `delete_material`, `delete_schedule` — **destructifs sans
   `dryRun`**, alors que `delete_element` en a un par défaut.
6. `ifc_set_family_mapping_file` **classé lecture seule** alors qu'il modifie un
   réglage d'export persistant : **il traverse le verrou d'écriture du ruban**.

Cause commune aux trois premiers : les outils composés **réimplémentent** au lieu
de déléguer à l'outil dédié. Les corrections apportées aux outils simples ne s'y
propagent donc jamais.

À arbitrer, et ce n'est pas un bug : `batch_export` et `workflow_data_roundtrip`
sont classés lecture et écrivent sur le disque. Aujourd'hui « lecture seule »
signifie « ne touche pas la maquette », pas « n'écrit rien ». À trancher et à
écrire noir sur blanc dans le contrat.

## Le plus gros manque n'est pas un bug (statut : traité)

19 capacités exposées par l'API Revit n'avaient **aucun point d'entrée**. Toutes les
quatre structurelles ci-dessous ont depuis reçu un outil :

| Manque | Outil ajouté |
|---|---|
| **Toitures** | `create_surface_based_element` couvrait déjà les toitures côté runtime ; seule la description MCP était fausse — corrigée, + `roofSlopeDegrees` ajouté |
| **Plans de surface** (Area, AreaScheme) | `manage_area_plans` |
| **Rampes** | `create_ramp` |
| **Trémies et réservations** | `create_opening` |

Les quatre efforts « à la journée » ont aussi été traités : **nuages de révision**
(`create_revision` action `create_cloud`), **cotes de niveau**
(`create_spot_dimension`), **vues de détail** (`create_view` viewType `callout`),
**zones de délimitation** (`manage_scope_boxes`). Les 11 manques restants
(murs-rideaux, toposolides, synchronisation centrale, assemblages, images,
nomenclatures de clés, options de conception, jeux de feuilles, légendes) ont
également reçu un outil ou une réponse structurée "non supporté" — deux capacités
(lignes de raccord, repères Keynote) sont confirmées sans aucune API exploitable et
n'ont volontairement reçu aucun outil fantôme.

Aucune de ces additions n'a été compilée ni testée dans un Revit réel — vérification
Windows obligatoire avant merge.

## Ordre de chantier — statut

| | Chantier | Statut |
|---|---|---|
| 1 | **Ferraillage et charpente** | **Fait** — retirés entièrement du dépôt (pas juste désactivés), 112 outils en moins |
| 2 | **Les 8 confirmés**, + test de contrat sur les clés imbriquées, + arbitrage lecture/écriture pour les écrits disque | Non traité — à revérifier sur les 196 outils restants |
| 3 | **`dryRun` sur les écritures sans aperçu** | Non traité |
| 4 | **Les 4 manques « S »** | **Fait** |
| 5 | **Erreurs génériques**, par vagues de catégorie | Non traité |
| 6 | **Toitures, surfaces, rampes, trémies** | **Fait** (non compilé/testé) |

## Ce que l'audit ne dit pas

Les 16 **signaux** ne sont pas des défauts : la détection compare une clé
annoncée dans une description au texte du runtime, et produit des faux positifs
quand la lecture passe par un helper partagé (`ElementScopeResolver`,
`TransactionFailureHandling`), par un DTO typé, ou quand la clé annoncée n'est
qu'un exemple de documentation. Chacun demande une lecture pour être classé.

Le classement d'**intérêt** est un jugement d'usage pour cette agence, pas une
propriété du code. Il vit dans les listes `TIER5` / `TIER4` / `TIER2` de
`tools/audit-tool-surface.py` et se corrige en les éditant, puis en relançant le
script.
