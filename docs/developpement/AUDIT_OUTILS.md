# Audit de la surface d'outils — 24 août 2026 (mis à jour après correction des 8 défauts)

Ce document est la lecture de l'inventaire, pas l'inventaire lui-même : le détail
outil par outil est dans [INVENTAIRE_OUTILS.md](../INVENTAIRE_OUTILS.md), généré par
`tools/audit-tool-surface.py`. Version auditée : connecteur **0.2.0**.

**Depuis le premier audit** : les modules Rebar et StructuralSteel (112 outils, 38 %
de la surface d'alors) ont été **retirés entièrement** du dépôt, pas seulement
filtrés. Les 19 capacités manquantes ont reçu un outil (16) ou sont confirmées
fermées côté API (3). Les 8 défauts confirmés sont corrigés.

L'audit est fait par extraction des sources — attributs `[McpServerTool]` du
serveur MCP croisés avec les classes `ICortexTool` du runtime — et non à la main.
Reproductible, et il a trouvé des choses qu'une relecture n'aurait pas vues.

Régénérer l'inventaire :

```powershell
python tools/audit-tool-surface.py
```

Le script écrivait aussi une page HTML filtrable de la même matière. Elle a été
retirée : deux formats de la même donnée générée, versionnés et régénérés ensemble,
pour un diff illisible dès qu'une description d'outil changeait. Le Markdown reste
parce qu'une modification de la surface d'outils doit se relire dans une pull request.

## Le dépôt ne compilait pas

La version précédente de ce document annonçait : « Aucune de ces additions n'a été
compilée dans cet environnement (pas de Windows/Revit ici) — à valider par un build
avant toute mise en production. » Le build a été fait. Il échouait, sur **trois
erreurs dans deux des outils ajoutés** :

| Fichier | Erreur | Cause réelle |
|---|---|---|
| `ListDesignOptionsTool.cs` | `CS0122: 'DesignOptionSet' est inaccessible` (×2) | `DesignOptionSet` **n'est pas un type public** de l'API Revit — il n'apparaît nulle part dans `RevitAPI.xml`. Les jeux d'options ne sont atteignables que comme `Element` de `OST_DesignOptionSets`. |
| `ManageCurtainGridTool.cs` | `CS1061: 'CurtainSystem' ne contient pas de définition pour 'CurtainGrid'` | Un `Wall` a **une** grille (`CurtainGrid`) ; un `CurtainSystem` en a **une par face** (`CurtainGrids`, un `CurtainGridSet` qui peut être nul). Le singulier n'existe pas sur ce type. |

Les deux sont corrigés, vérifiés contre `RevitAPI.xml` puis contre le compilateur.
`dotnet build .\RiveTT.sln -c Release` passe à **0 erreur, 0 avertissement**.

Deux gardes de test étaient également en échec, toutes deux séquelles mécaniques :

- `EveryMcpToolIsDiscoverableAndNamed` exigeait `> 250` outils — le seuil datait
  d'avant le retrait des 112 outils Rebar/Steel et faisait échouer le build sur une
  surface pourtant correcte. Ramené à `> 150` : la garde vérifie que la réflexion
  fonctionne, pas un budget d'outils.
- `Roslyn_loader_uses_the_renamed_tools_assembly` était devenu contradictoire : le
  renommage global `RevitCortex` → `RiveTT` a réécrit le littéral **à l'intérieur du
  `DoesNotContain`**, si bien que le test exigeait l'absence de la ligne même que son
  `Contains` réclamait. Aucune source ne pouvait satisfaire les deux.

## Les chiffres qui commandent la suite

| Mesure | Valeur | Ce qu'elle implique |
|---|---|---|
| Outils publiés | **196** | dont 193 classes runtime |
| Écritures | **136** (69 %) | c'est la part de la surface que le verrou d'écriture du ruban gouverne ; +1 par le reclassement de `ifc_set_family_mapping_file` |
| Ferraillage + charpente métallique | **0** | retirés du dépôt — chantier n°1 terminé |
| Écritures sans `dryRun` | **81** | était 86 ; le contrat annonce `dryRunDefault: true`, l'écart reste à combler |
| Erreurs génériques `Failed: …` | **133** | échouent sans dire quoi corriger |
| Défauts confirmés / signaux | **0** / **9** | les 8 confirmés sont corrigés et gardés par `ConfirmedDefectFixSourceTests` |

## Les 8 défauts confirmés — corrigés

1. **`workflow_sheet_set` ignorait `viewIds`** — la spec publiée annonçait
   `[{number, name, viewIds?}]`, la boucle runtime ne lisait que `number` et `name` :
   les feuilles sortaient vides, sans aucun signalement. *(critique)*
   → Les `viewIds` sont lus et placés ; la réponse réconcilie `requestedViewCount`
   et `placedViewCount`, et signale tout écart.
2. **`batch_create_sheets` plaçait les fenêtres à (0,5 ft ; 0,5 ft) en dur** — or
   l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le
   cartouche A1 français. *(critique)*
   → Le cadre est mesuré sur l'instance de cartouche. Plusieurs vues sur une même
   feuille sont **pavées** au lieu d'être empilées au même point.
3. **`workflow_clash_review` détectait en boîtes englobantes** quand
   `clash_detection` utilise l'intersection solide : l'outil composé rendait plus de
   faux positifs que l'outil simple.
   → Les deux appellent la même passe.
4. **`send_code_to_revit` sans `dryRun`** — l'outil le plus puissant du connecteur
   n'avait aucun aperçu.
   → `dryRun` par défaut : la sandbox est vérifiée, rien n'est exécuté ni écrit sur
   disque. Sa description annonçait aussi « require an in-Revit confirmation » : c'est
   faux, `CortexSession.RequestConfirmation` retourne toujours `true` et cet outil ne
   l'appelait même pas. La mention est retirée.
5. **`delete_selection`, `delete_material`, `delete_schedule` destructifs sans
   `dryRun`**, alors que `delete_element` en a un par défaut.
   → `dryRun` par défaut sur les trois, via `DeletionPreview`, qui sonde la cascade
   réelle par transaction annulée. Les trois s'appuyaient sur
   `session.RequestConfirmation`, **qui ne bloque rien** : la « confirmation » de leur
   description n'a jamais existé.
6. **`ifc_set_family_mapping_file` classé lecture seule** alors qu'il modifie un
   réglage d'export persistant : il traversait le verrou d'écriture du ruban.
   → Reclassé `[ToolSafety(false, false)]`.

### La cause commune, traitée à la racine

Les trois premiers venaient du même mécanisme : **les outils composés
réimplémentaient au lieu de déléguer**, si bien que les corrections apportées aux
outils simples ne s'y propageaient jamais. Deux helpers partagés ferment cette porte :

| Helper | Remplace | Utilisé par |
|---|---|---|
| `SheetFrame` | le calcul du cadre imprimable et le centrage des fenêtres | `place_viewport`, `batch_create_sheets`, `workflow_sheet_set` |
| `ClashFinder` | la passe de détection (pré-filtre par boîtes + `ElementIntersectsElementFilter`) | `clash_detection`, `workflow_clash_review` |

Corriger l'un corrige désormais les autres. C'est le vrai livrable de ce lot ; les
huit correctifs n'en sont que la conséquence visible.

### À arbitrer, et ce n'est pas un bug

`batch_export` et `workflow_data_roundtrip` sont classés lecture et écrivent sur le
disque. Aujourd'hui « lecture seule » signifie « ne touche pas la maquette », pas
« n'écrit rien ». À trancher et à écrire noir sur blanc dans le contrat.

## Le plus gros manque n'était pas un bug (statut : traité)

19 capacités exposées par l'API Revit n'avaient **aucun point d'entrée**. Seize ont
désormais un outil — dont les quatre manques structurels, ce qui signifie qu'une
maquette de logement peut être produite de bout en bout par le connecteur.

Le tableau complet est dans [INVENTAIRE_OUTILS.md](../INVENTAIRE_OUTILS.md), section
« Lacunes comblées ». Trois manques subsistent, tous de priorité basse : **repères de
texte** (Keynote), **lignes de raccord** (Matchline), **plateformes** (BuildingPad).

Trois autres capacités ne sont **pas des lacunes mais des frontières de l'API** —
elles étaient réinscrites comme des manques à chaque relecture, elles ont maintenant
leur propre section pour qu'on cesse de les chercher : les **légendes** (l'API ne crée
pas de vue de légende de zéro), les **options de conception** (ni jeu ni option ne se
créent par l'API) et les **zones de délimitation** (aucune méthode de création).

## Ordre de chantier — statut

| | Chantier | Statut |
|---|---|---|
| 1 | **Ferraillage et charpente** | **Fait** — retirés entièrement du dépôt, 112 outils en moins |
| 2 | **Les 8 confirmés** + arbitrage lecture/écriture pour les écrits disque | **Fait** pour les 8, gardés par `ConfirmedDefectFixSourceTests`. L'arbitrage disque reste ouvert |
| 3 | **`dryRun` sur les écritures sans aperçu** | **Partiel** — 86 → 81. Les 5 traités sont les destructifs ; les 81 restants sont des écritures ordinaires |
| 4 | **Les 4 manques « S »** | **Fait** |
| 5 | **Erreurs génériques**, par vagues de catégorie | Non traité — 133 |
| 6 | **Toitures, surfaces, rampes, trémies** | **Fait**, et désormais **compilé** |
| 7 | **Test de contrat sur les clés imbriquées** | Non traité — c'est ce qui aurait attrapé `viewIds` |

## Ce qui reste à prouver dans un vrai Revit

Le build passe et 411 tests passent, mais **13 tests ne peuvent pas s'exécuter ici** :
ils chargent `RevitAPI.dll` à l'exécution, et le paquet NuGet ne fournit qu'un
assembly de *référence*. Il faut une machine avec Revit installé.

Les comportements suivants ne sont prouvables que sur maquette réelle, et sont à
consigner dans la pull request qui porte la correction :

- le centrage des fenêtres sur un **cartouche A1 français réel** (c'est le cas qui a
  révélé le défaut) et le pavage de plusieurs vues sur une feuille ;
- l'écart de comptage entre `workflow_clash_review` et `clash_detection` sur un même
  modèle : ils doivent maintenant donner le **même nombre** ;
- la cascade rapportée par les aperçus de suppression, comparée à la suppression
  réelle ;
- `list_design_options` sur un modèle portant de vraies variantes, et
  `manage_curtain_grid` sur un `CurtainSystem` **multi-faces** — seule la première
  face est adressée, faute de sélecteur de face publié.

## Ce que l'audit ne dit pas

Les 9 **signaux** ne sont pas des défauts : la détection compare une clé
annoncée dans une description au texte du runtime, et produit des faux positifs
quand la lecture passe par un helper partagé (`ElementScopeResolver`,
`TransactionFailureHandling`), par un DTO typé, ou quand la clé annoncée n'est
qu'un exemple de documentation. Chacun demande une lecture pour être classé.

Le classement d'**intérêt** est un jugement d'usage pour cette agence, pas une
propriété du code. Il vit dans les listes `TIER5` / `TIER4` / `TIER2` de
`tools/audit-tool-surface.py` et se corrige en les éditant, puis en relançant le
script.
