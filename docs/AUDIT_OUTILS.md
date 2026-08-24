# Audit de la surface d'outils — 24 août 2026

Ce document est la lecture de l'inventaire, pas l'inventaire lui-même : le détail
outil par outil est dans [INVENTAIRE_OUTILS.md](INVENTAIRE_OUTILS.md), généré par
`tools/audit-tool-surface.py`. Version auditée : connecteur **0.2.0**.

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
| Outils publiés | **295** | dont 292 classes runtime : 3 façades (`create_wall`, `create_door`, `create_window`) appellent `create_line_based_element` et `create_point_based_element` |
| Écritures | **181** (61 %) | c'est la part de la surface que le verrou d'écriture du ruban gouverne |
| Ferraillage + charpente métallique | **112** (38 %) | inutilisables en agence d'architecture, chargés à chaque session |
| Écritures sans `dryRun` | **92**, dont **76** hors ferraillage | alors que le contrat annonce `dryRunDefault: true` |
| Erreurs génériques `Failed: …` | **167** | échouent sans dire quoi corriger |
| Défauts confirmés / signaux | **8** / **16** | les confirmés sont lus dans le code, pas déduits |

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

## Le plus gros manque n'est pas un bug

19 capacités exposées par l'API Revit n'ont **aucun point d'entrée** — vérifié par
recherche sur les 295 noms d'outils. Quatre sont **structurelles** pour les
spécialités de l'agence :

| Manque | Pourquoi c'est structurel |
|---|---|
| **Toitures** | `create_surface_based_element` ne couvre que sols et plafonds |
| **Plans de surface** (Area, AreaScheme) | surfaces réglementaires SHAB / SU / SDP : `create_room` crée des pièces, pas des surfaces |
| **Rampes** | `create_stair` existe, aucune rampe : accessibilité PMR en équipement et santé |
| **Trémies et réservations** | aucun percement de dalle, de mur, ni de gaine verticale |

Sans elles, une maquette de logement ne peut pas être produite de bout en bout
par le connecteur.

Quatre autres sont des efforts de l'ordre de la journée sur des gestes
quotidiens : **nuages de révision**, **cotes de niveau**, **vues de détail**
(déjà écrites dans `workflow_room_documentation` mais non exposées) et **zones de
délimitation**.

## Ordre de chantier recommandé

| | Chantier | Coût | Pourquoi là |
|---|---|---|---|
| 1 | **Filtre de catégories** : ferraillage et charpente désactivés par défaut, réactivables | ½ j | Retire 112 outils du catalogue sans supprimer une ligne de code. Améliore immédiatement le choix d'outil de l'agent et réduit de 38 % la surface à corriger ensuite |
| 2 | **Les 8 confirmés**, + test de contrat sur les clés imbriquées, + arbitrage lecture/écriture pour les écrits disque | 1 j | Le test est ce qui empêche la récidive : `ServerRuntimeParameterContractTests` ne voit que les paramètres de premier niveau, pas les clés à l'intérieur d'un tableau JSON. Sans lui, on recorrigera le prochain à la main |
| 3 | **`dryRun` sur les 76 écritures** hors ferraillage, destructifs d'abord | 2–3 j | Aligne le contrat sur la réalité |
| 4 | **Les 4 manques « S »** | 4 j | Gain quotidien immédiat |
| 5 | **Erreurs génériques**, par vagues de catégorie | 1–2 j | Systémique mais mécanique |
| 6 | **Toitures, surfaces, rampes, trémies** | 3–4 semaines | Le vrai périmètre manquant |

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
