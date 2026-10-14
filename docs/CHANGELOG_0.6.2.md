# RiveTT 0.6.2

Deux apports : le cycle complet d'édition d'une famille **sans fichier** (`open_family` sur une
famille du projet, `load_family` action `load_from_open`), et les corrections issues du rapport
de la session ILO (E23_005, 13/10/2026, RiveTT 0.6.1.0, Revit 2026).

> **État : code écrit, NON compilé ni testé.** Le poste de développement n'a ni SDK .NET 10 ni
> Inno Setup au moment de l'écriture. `dotnet test`, `builder\build.ps1` et les sept scénarios
> du §4 restent à exécuter ; rien ci-dessous n'est vérifié sur Revit.

## 1. Familles ouvertes en mémoire

| Outil | Évolution |
|---|---|
| `load_family` `action: "load_from_open"` | Charge dans un projet ouvert une famille **déjà ouverte** (issue de `open_family`, de `EditFamily`, ou créée et jamais enregistrée), sans chemin : `Document.LoadFamily(projet, options)` sur le document famille, hors transaction. Paramètres : `sourceDocument`, `targetDocument`, `overwriteExisting`, `renameTo`, `activateTypes`, `closeSource`, `dryRun`. Réponse : `status` (`loaded` \| `reloaded` \| `skipped_existing` \| `dry_run`), `familyId`, `familyName`, `category`, `wasExisting`, `types[]`, `sourceClosed` (+ `sourceCloseReason`), `activeDocument`. `OverwritingFamilyLoadOptions` réutilisée telle quelle. |
| `open_family` sur une famille du projet | `familyId` ou `familyName` (+ `targetDocument`, `activate`) à la place de `filePath` : `Document.EditFamily`. `activate=true` (défaut) donne un chemin au document par une copie de travail dans `%TEMP%\RiveTT\OpenedFamilies\` (SaveAs puis activation, comme `open_file` pour les `.rft`) ; `activate=false` l'ouvre en arrière-plan. La famille du projet n'est touchée que par `load_from_open`. |

Points de conception à connaître :

- **`dryRun` est déclaré par outil, pas par action.** `load_family` passe en `supportsDryRun: true` ;
  `Execute` refuse lui-même `dryRun:true` sur `list`, `load` et `duplicate_type` (sinon `load` +
  `dryRun:true` chargerait pour de bon). `execution.supportsDryRun` vaut donc `true` pour tout
  `load_family`.
- **Le verrou d'écriture s'applique aussi au `dryRun`** de `load_from_open` (règle de l'AGENTS.md).
- **`closeSource` sur la source active** exige de réactiver la cible, donc une cible **enregistrée**
  (Revit n'active un document que par chemin). Sinon la famille est chargée normalement et la
  réponse porte `sourceClosed: false` avec la raison.
- La cible doit être un projet **ouvert** (au premier plan ou en arrière-plan).

## 2. Rapport ILO du 13/10/2026

| # | Constaté | Correction |
|---|---|---|
| 1.1 | `duplicate_family_type` : `{"dim_plis_horiz":1500}` appliqué en pieds (457 m), réponse sans unité, pas de `dryRun` | Un nombre nu sur un paramètre de **longueur** vaut des **mm** (aire m², volume m³, angle °) ; une chaîne porte son unité (`"900 mm"`, lue par Revit). Chaque valeur appliquée revient avec `value`, `unit`, `display`, `internalValue`, `interpretedAs` ; avertissement au-delà de 100 m. `dryRun` (sonde annulée). **Changement de comportement** : un appelant qui passait des pieds doit passer `"… ft"`. |
| 1.2 | `place_viewport` : `centreMm` rendait la consigne, pas la position réelle ; texte des avertissements supprimés introuvable | `centreMm` = centre réel (`GetBoxCenter`), `requestedCentreMm`, `centreDeviationMm`, `pinned`, avertissement au-delà de 5 mm. **Tous les outils** : `execution.suppressedWarnings[{message, count}]` expose le texte des avertissements que les transactions suppriment. |
| 1.2 (cause) | Viewports de plans 02.1 à 02.4 immobiles | **Cause relevée : la position enregistrée** — le viewport garde la position que Revit a mémorisée pour lui au lieu de celle passée à `Viewport.Create` ; ce n'est pas un épinglage. Mécanisme exact non vérifié. `place_viewport` rend la position réelle et l'écart, et accepte `enforcePosition: true` (centre redonné par `SetBoxCenter`, effet relu, `positionEnforced`). Désactivé par défaut : il ramène le centre de la boîte (étiquette exclue) sur la consigne, quelques mm plus loin que ce que laisse `Create`. **À rejouer sur E23_005** : on ne sait pas si `SetBoxCenter` suffit là où il avait échoué par script. |
| 1.3 | `manage_view_templates` `duplicate` : « View cannot be duplicated » | `action: "create_from_view"` (`viewId`, `newName`) via `View.CreateViewTemplate()` ; le message d'échec de `duplicate` y renvoie. |
| 2.1 | Aucun outil pour les jeux de feuilles de l'Explorateur de projet | `manage_sheet_sets` : `list_collections`, `create_collection`, `assign_to_collection` (`SheetCollection`, API 2025+, vérifié dans `RevitAPI.xml` 2026). Choix : **actions d'un outil existant** plutôt qu'un nouvel outil, qui aurait changé le décompte de la surface. La description distingue jeux d'impression et jeux de feuilles. `list_collections` reste autorisé verrou fermé. |
| 2.2 | Deux outils de duplication divergents (cartouche secondaire, `Dossier`, `Indice_manuel` non copiés ; pas de `newNumber` d'un côté, pas de `viewDuplicateOption` de l'autre) | Une seule implémentation (`SheetDuplicator`) pour `duplicate_sheet_with_content` et `duplicate_sheet_with_views` : mêmes paramètres (`newNumber`, `newName`, `viewDuplicateOption`, `viewNameSuffix`, `copyAnnotations`…), **tous** les cartouches, paramètres de la feuille hors numéro et nom, jeu de feuilles. Seule différence : `copyAnnotations` vaut `true` pour `_with_content`, `false` pour `_with_views`. |
| 2.3 | Gestes sans outil dédié, faits par script | Déplacer un viewport vers une autre feuille : `place_viewport` `moveIfPlaced`. Second cartouche : `place_title_block` le permettait déjà, mais à l'origine seulement : `positionX` / `positionY` ajoutés. Masquer les repères de coupe par vue : `override_graphics` `hide_category` / `show_category` (`categories`, `viewIds`). Type de vue de plan : `duplicate_system_type` avec `category: "ViewFamilyType"`. |
| 2.4 | `send_code_to_revit` | `readonly` : la note explique qu'il faut ouvrir sa propre `Transaction` pour essayer-puis-annuler. `group` : un échec annule tout le groupe, sections « committed » comprises, ce que dit désormais `sectionsNote`. **Non corrigé, par conception** : la réflexion reste interdite par le bac à sable ; pour vérifier qu'une méthode existe, lire `RevitAPI.xml` dans le dossier d'installation de Revit. **À vérifier en live** : l'écart de numéros de ligne (8 annoncé, 24 dans la trace) — le code retranche déjà le préfixe de l'enveloppe, la cause n'est pas établie. |
| 3 | `list_warnings` sans « apparus depuis la dernière lecture » ; scripts enregistrés sans que l'utilisateur le sache | `snapshot: true` puis `sinceSnapshot: true` (empreinte = gravité + texte + éléments ; la base saute au changement de document). `send_code_to_revit` renvoie `scriptNotice`, à relayer à l'utilisateur. **Non traité** : `tool_search` ne charge que 5 à 8 outils par requête, c'est le client. |

## 3. À faire avant de livrer

- Installer le SDK .NET 10, Inno Setup (et Python pour `tools/audit-tool-surface.py`, qui
  régénère l'inventaire de `SKILL.md`, **non régénéré ici**).
- `dotnet test`, `dotnet build`, `builder\build.ps1`. Les tests de `Release062SourceTests` sont
  des assertions sur le source ; les tests existants sur le décompte `dryRun`, les contrats
  serveur/runtime et `SKILL.md` peuvent demander un ajustement.
- Documenter les nouveautés dans `src/resources/documentation/SKILL.md`.

## 4. Points à vérifier sur Revit (projet de test, jamais un projet d'agence)

`load_from_open` :

1. Famille créée par `NewFamilyDocument`, jamais enregistrée → chargement + `renameTo`.
2. Famille ouverte par `open_family`, modifiée, document actif = famille, `closeSource: true` →
   chargement, retour au projet actif, famille fermée.
3. Famille déjà présente, `overwriteExisting: false` → `skipped_existing`, rien modifié.
4. Famille déjà présente, `overwriteExisting: true` → `reloaded`, types mis à jour.
5. Transaction laissée ouverte → erreur explicite, aucun effet de bord.
6. Deux projets ouverts sans `targetDocument` → erreur listant les projets.
7. `dryRun: true` → diagnostic correct, rien modifié (vérifier par `list`).

Et :

8. `open_family` sur une famille du projet avec `activate=true` : le SaveAs d'un document issu de
   `EditFamily` puis `OpenAndActivateDocument` activent bien la famille, et `load_from_open` la
   recharge dans le projet d'origine. **C'est l'hypothèse la moins sûre de la version.**
9. `place_viewport` sur un plan dont le viewport est immobile : lire `pinned`,
   `centreDeviationMm` et `execution.suppressedWarnings`.
10. `duplicate_sheet_with_*` sur une feuille à deux cartouches : les deux sont copiés, à leur place.
11. `manage_sheet_sets` `assign_to_collection` avec deux feuilles de même numéro dans le même jeu.

## 5. Politique de `send_code_to_revit` et documentation embarquée

- **Politique** : `send_code_to_revit` n'est plus « un dernier recours, à n'utiliser qu'après
  accord explicite de l'utilisateur » : c'est une **solution légitime**, à préférer à un outil
  dédié seulement quand celui-ci ne couvre pas le cas ou qu'il faut enchaîner plusieurs gestes.
  Inchangés : `dryRun` par défaut, bac à sable, verrou d'écriture, absence de boîte de
  confirmation dans Revit. Nouveau : `scriptNotice` à relayer à l'utilisateur (le script est
  enregistré dans son profil à chaque exécution).
  Réécrits : la description MCP, la description côté plugin, les `ServerInstructions`, l'`AGENTS.md`,
  les règles 7 et la section « Choisir send_code_to_revit » de `SKILL.md`, et
  `SendCodeDescriptionTests` (qui verrouillait l'ancienne formule « LAST RESORT » et le mot « consent »).
- **`SKILL.md`** (copié par l'installateur sur le poste) : cycle `open_family` /
  `load_from_open`, unités de `duplicate_family_type`, types de vue, section « Feuilles,
  viewports et gabarits de vue », avertissements supprimés et `snapshot`, table des
  commandes disponibles verrou fermé. Le blocage des viewports de plans y est consigné avec sa
  cause relevée (la position enregistrée) et la conduite à tenir (`enforcePosition`).
- **Inventaire généré de `SKILL.md` : NON régénéré** (Python absent du poste). L'étape
  « The generated tool inventory matches the code » de `.github/workflows/build.yml` échouera
  tant que `python tools/audit-tool-surface.py` n'aura pas été exécuté et son résultat commité.
- Les skills hébergés côté compte (`rivett`…) ne sont pas dans ce dépôt : ils sont à mettre à
  jour à partir de ce `SKILL.md` une fois la version publiée.
