# Protocole de recette sur maquette réelle

Ce que `dotnet test` ne peut pas prouver : la géométrie, les transactions, les messages
d'erreur de Revit, et le simple fait qu'un outil rende ce que sa description annonce.
Le paquet NuGet `Nice3point.Revit.Api.*` ne fournit qu'un assembly de référence — 486
tests verts signifient que le **contrat** tient, pas que les 198 outils font leur
travail.

Ce protocole comble cet écart. Il s'exécute **après le build**, sur une maquette de
recette, par deux agents : l'un opère, l'autre vérifie. Les deux écrivent dans le même
rapport.

## Pourquoi deux agents

Un agent qui appelle un outil et juge sa propre réponse a un angle mort structurel : il
lit le message de succès de l'outil, pas la maquette. `create_floor` peut répondre
`created: 1` avec un plancher au mauvais niveau ; `batch_create_sheets` a longtemps
répondu succès en plaçant les vues hors cadre. L'agent qui a formulé l'appel est le plus
mal placé pour voir que le résultat ne correspond pas à l'intention.

L'**opérateur** appelle et consigne ce qu'il a obtenu. L'**auditeur** relit chaque ligne
sans avoir vu l'appel se dérouler, revérifie par des outils de **lecture** indépendants,
et tranche. Un désaccord n'est pas un incident : c'est le résultat le plus utile que ce
protocole produise.

## Préalables

1. `.\builder\build.ps1` est passé, `dist\RiveTT-Setup-<version>.exe` installé, Revit
   redémarré. Vérifier `execution.pluginVersion` et `execution.mcpServerVersion` :
   s'ils diffèrent, arrêter — la recette porterait sur deux moitiés dépareillées.
2. **Une seule instance de Revit ouverte.** Le serveur se connecte à la session la plus
   récemment démarrée sans le dire ; deux instances rendent la recette illisible.
3. La maquette de recette est une **copie**, sur un disque local, jamais un projet en
   cours. Elle sera modifiée, y compris de travers — c'est le but.
4. Purger `%LOCALAPPDATA%\RiveTT\audit.jsonl` (ou noter l'horodatage de départ) : le
   journal est la preuve d'appel indépendante des deux agents.

### La maquette de recette

Un modèle vide ne teste rien : la moitié des outils rend « aucun élément » et l'agent
lit ça comme un succès. Elle doit contenir au minimum :

| Contenu | Pourquoi |
|---|---|
| 3 niveaux nommés en **français** (`Niveau 0`, `Niveau 1`…) | `ParameterNameResolver` et `CategoryResolver` ne sont exercés que sur un document localisé |
| Murs, portes, fenêtres, un plancher, une toiture | les créations hôtées ont besoin d'un hôte |
| 5 pièces nommées et numérotées | `create_views_from_rooms`, `tag_rooms`, `export_room_data` |
| 2 nomenclatures, 3 feuilles avec cartouche A1 | `place_viewport`, `batch_create_sheets`, `export_schedule` |
| 1 fichier lié (RVT ou IFC) | les 10 outils `LinkedFiles` sont sinon intestables |
| Des worksets et des phases | ce sont des **outils dynamiques** : sans eux ils ne sont même pas publiés |
| 1 groupe de modèle, 1 option de conception | `edit_group_members`, `list_design_options` |
| Au moins un avertissement Revit non résolu | `get_warnings` sur un modèle propre ne prouve rien |

Consigner dans le rapport le chemin de la maquette, sa version Revit, et la version du
connecteur. Une recette sans ces trois lignes n'est pas rejouable.

## La liste de travail

Elle n'est pas recopiée ici : elle se dérive de
`src/resources/documentation/references/inventaire-des-outils.md`, généré depuis le code.
Une liste tenue à la main dans ce fichier serait périmée à la release suivante.

Ordre de passage, et il compte :

1. **Lecture, verrou fermé.** Les ~59 outils de lecture. Rien ne peut être cassé, et
   c'est ce qui construit l'état de référence dont la suite a besoin.
2. **Refus, verrou fermé.** Un échantillon d'outils d'écriture, pour vérifier que le
   refus est bien `PermissionDenied` avec `writesAllowed: false`. Inclure un appel avec
   `dryRun: true` : il doit être refusé **aussi**.
3. **Contrat `dryRun`, verrou ouvert.** Sur un outil sans prévisualisation
   (`execution.supportsDryRun: false`), appeler avec `dryRun: true` : attendre
   `InvalidInput`, et vérifier dans le journal d'audit que **rien n'a été exécuté**.
4. **Prévisualisations, verrou ouvert.** Les 56 outils qui prévisualisent, en `dryRun`.
   Aucun ne doit modifier la maquette : le contrôle est un `get_warnings` +
   `analyze_model_statistics` avant/après, pas la parole de l'outil.
5. **Écritures, verrou ouvert.** Le reste, dans l'ordre d'intérêt décroissant (5 → 1).
   Sauvegarder la maquette entre chaque bloc de dix, pour pouvoir revenir en arrière.

## Rôle 1 — l'opérateur

Pour chaque outil, une ligne dans le rapport, remplie **avant** de passer au suivant.

- appeler avec des paramètres réalistes, jamais des valeurs vides « pour voir » : un
  outil qui refuse une entrée vide n'a rien prouvé ;
- si l'outil accepte `dryRun`, appeler d'abord en prévisualisation, puis pour de vrai,
  et consigner **les deux** ;
- après chaque écriture, vérifier par un outil de **lecture** — jamais en relisant la
  réponse de l'outil qui vient d'écrire ;
- ne pas corriger un appel raté en silence. Un paramètre qu'il a fallu deviner est un
  défaut de description : le consigner comme tel ;
- ne rien conclure sur ce qui n'a pas été appelé. Un outil non testé se note
  `non testé` avec sa raison, jamais `OK`.

Interdits : `send_code_to_revit` pour contourner un outil qui résiste (c'est le défaut
qu'on cherche), et toute modification de la maquette hors des appels consignés.

## Rôle 2 — l'auditeur

L'auditeur **ne rejoue pas** la session : il la conteste. Il travaille verrou fermé, à
la lecture seule, et sur les mêmes sources que l'opérateur plus une qu'il ne contrôle
pas : `audit.jsonl`.

Pour chaque ligne du rapport :

1. l'appel a-t-il eu lieu ? (`audit.jsonl` : `tool`, `ts`, `duration_ms`,
   `elements_affected`) ;
2. l'effet annoncé est-il **visible dans la maquette** par une lecture indépendante ?
   Un `created: 1` sans élément retrouvable est un échec, pas un succès ;
3. l'effet correspond-il à la **description publiée** de l'outil, ou seulement à ce que
   l'opérateur espérait ?
4. la réponse respecte-t-elle le contrat : unité sur les valeurs numériques,
   `categoryBic` sur les catégories, noms non résolus **signalés** et non rendus vides,
   `execution` complet ?
5. une prévisualisation a-t-elle laissé la maquette intacte ?

Verdicts : `confirmé` · `infirmé` · `non concluant` · `défaut` (avec gravité :
critique / majeur / mineur). Un désaccord se conserve tel quel — les deux avis, sans
arbitrage — et remonte en tête du rapport.

## Le rapport

Un seul fichier, `docs/recettes/recette-<version>-<AAAA-MM-JJ>.md`, écrit par les deux
agents. L'opérateur remplit les quatre premières colonnes, l'auditeur les deux
dernières. Personne ne réécrit la colonne de l'autre.

```markdown
# Recette RiveTT <version> — <date>

Maquette : <chemin>  ·  Revit <version>  ·  plugin <x> / serveur <y>
Opérateur : <modèle/agent>  ·  Auditeur : <modèle/agent>

## Désaccords

| Outil | L'opérateur dit | L'auditeur dit |
|---|---|---|

## Défauts

| Outil | Gravité | Ce que le code fait | Ce qu'il devrait faire | Reproduction |
|---|---|---|---|---|

## Relevé

| Outil | Appel | Réponse (résumé) | dryRun | Vérification | Verdict |
|---|---|---|---|---|---|

## Non testés

| Outil | Pourquoi |
|---|---|

## Synthèse

Appelés X / 198 · confirmés X · infirmés X · non concluants X · défauts X
```

`docs/recettes/` n'est pas installé sur les postes : c'est la mémoire du dépôt. Un
rapport n'est jamais écrasé — un nouveau fichier par recette, pour que la comparaison
d'une version à l'autre reste possible.

## Ce qui remonte dans le code

- un **défaut confirmé** rejoint la table « Défauts corrigés » de l'inventaire une fois
  réparé, et gagne un test dans `ConfirmedDefectFixSourceTests` : un inventaire qui
  oublie ce qui a cassé une fois laisse le même défaut revenir sans que personne le
  reconnaisse ;
- un **paramètre qu'il a fallu deviner** est un défaut de `[Description]`, à corriger
  côté serveur MCP ;
- un **outil non concluant faute de contenu** dans la maquette enrichit la table « La
  maquette de recette » ci-dessus ;
- un comportement vérifié en direct que seul un Revit vivant peut prouver se note dans
  le commit ou la pull request qui le change, comme l'exige `AGENTS.md`.
