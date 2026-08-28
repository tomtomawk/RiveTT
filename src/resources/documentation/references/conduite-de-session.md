# Conduite de session

**Portée :** ouvrir une session sur un modèle, et choisir à chaque demande l'outil
le moins coûteux qui la résout.
**Sources :** `inventaire-des-outils.md`, `../README.md`.
**Vérifié le :** 2026-08-28

## Ouvrir la session

1. Premier appel : `get_project_info` complet, une seule fois.
2. Ensuite, le filtrer :
   `{"includeLevels": false, "includePhases": false, "includeWorksets": false, "includeLinks": false}`.
   Le relancer complet en cours de session ne réapprend rien.
3. **La langue du document se lit, elle ne se suppose pas.** Elle apparaît dans les
   noms de paramètres retournés :

   | | Niveau | Commentaires | Nom du type |
   |---|---|---|---|
   | FR | Niveau | Commentaires | Nom du type |
   | EN | Level | Comments | Type Name |
   | DE | Ebene | Kommentare | Typname |
   | IT | Livello | Commenti | Nome del tipo |

4. Pour les catégories, préférer toujours le code `OST_*` au libellé localisé.
   Revit FR nomme la catégorie des vues portées « Fenêtres », comme les fenêtres.

À noter au passage, parce que la suite en dépend : `phases.length > 0` conditionne
`set_element_phase`, et `isWorkshared: true` conditionne `set_element_workset`. Les
deux sont indépendants l'un de l'autre.

## Lire une réponse

| Champ | Ce qu'il dit |
|---|---|
| `execution.writesAllowed` | verrou d'écriture de la session — **faux au démarrage de chaque session Revit** |
| `execution.toolReadOnly` | classe l'outil qui répond, pas la session |
| `execution.cached` | réponse servie par le cache, pas une observation fraîche |
| `execution.versionMismatch` | serveur MCP et plugin de versions différentes — voir `../SKILL.md`, règle 7 |
| `unresolvedParameterNames` | noms de paramètres non résolus. **Une colonne vide sans ce champ est une vraie valeur vide** |
| `unit` / `internalValue` | Revit stocke des pieds, pi² et pi³ quelles que soient les unités du projet |
| `categoryBic` | le code `OST_*`, non ambigu |

## Connaître l'état du modèle

Par coût croissant. Ne monter d'un niveau que si le précédent ne suffit pas.

| | Outil | Coût | Quand |
|---|---|---|---|
| 1 | `check_model_health` | ~200 jetons | contrôle rapide |
| 2 | `analyze_model_statistics` (`compact: true`) | ~400 | statistiques de base |
| 3 | `workflow_model_audit` filtré | ~800 | audit ciblé |
| 4 | `workflow_model_audit` complet | ~3000 | audit complet, rare |

## Trouver des éléments

| Cas | Outil | Remarque |
|---|---|---|
| Un paramètre, valeur exacte | `export_elements_data` avec `filterParameterName`/`filterValue` | le plus rapide |
| Plage, ET/OU, multi-paramètres | `filter_elements` | à envelopper dans `{"data": {...}}` |
| Éléments de la vue active | `get_current_view_elements` avec `fields` et `limit` | |
| Volume ou pièce | `get_elements_in_spatial_volume` avec `categoryFilter` | `containment: inside` (défaut) = contenus ; `boundary` = éléments qui **délimitent** la pièce |
| Identifiants connus | `export_elements_data` avec `elementIds` | appliqué avant la pagination |
| Pièces d'un niveau | `export_room_data` avec `levelName` ou `levelId` | filtre exécuté dans Revit |
| Paramètre personnalisé | `get_element_parameters` sur **un** élément témoin d'abord | ne jamais deviner un nom de paramètre projet |

Sur un modèle d'architecture, les poteaux sont `OST_Columns`, **pas**
`OST_StructuralColumns`.

## Trouver ou dupliquer un type

| Cas | Outil |
|---|---|
| Type de famille chargeable (porte, fenêtre, cartouche) | `list_family_types` avec `kind: loadable` |
| Type système (mur, sol, garde-corps, escalier) | `list_system_types(category)` — sans catégorie, rend l'inventaire avec les codes `OST_*` |
| Dupliquer | `duplicate_family_type` (chargeable) / `duplicate_system_type` (système) |

`duplicate_family_type` échoue sur un type système : ce ne sont pas des familles
chargeables. C'est la confusion la plus fréquente.

## Documents et familles

| Cas | Outil | Remarque |
|---|---|---|
| Nouveau projet vide | `create_document(templatePath?, targetPath)` | `save_as_document` **duplique** le modèle ouvert, il ne crée pas un projet vide |
| Ouvrir et activer un fichier | `open_document(filePath)` | change le document actif et vide les caches — enregistrer le courant avant |
| Ouvrir une famille ou un gabarit | `open_family` / `open_template` | le document actif change |
| Modifier les valeurs de type d'une famille | `edit_family` | **en arrière-plan, aucune fenêtre ne s'ouvre** |
| Fermer | `close_document` | fermer le document **actif** exige qu'un autre soit ouvert |

`Document.EditFamily` n'interbloque pas Revit depuis un `ExternalEvent`. L'affirmation
inverse a circulé longtemps et a bloqué cette famille d'outils ; elle a été mesurée
fausse (voir `../../docs/CHANGELOG_0.3.0.md`). `edit_family` s'appuie dessus.

## Groupes

| Cas | Outil | Ce qui se passe vraiment |
|---|---|---|
| Retirer un membre | `delete_element`, ou `edit_group_members` avec `removeElementIds` seul | c'est une **exclusion** : cette instance seule, le type et les autres instances intacts, instance renommée « (membre exclu) » |
| Ajouter un membre | `edit_group_members` avec `addElementIds` | impose dégrouper/regrouper : **nouveau type**, les autres instances restent sur l'ancienne définition |
| Restaurer un membre exclu | aucun outil | uniquement depuis le ruban Revit |

Des instances qui diffèrent, c'est normal : exclusions ou contraintes de niveau
propres. Lire `hasExcludedMembers` **par instance**, ne pas se fier à la première.

## Dessiner

| Cas | Outil |
|---|---|
| Ligne de détail, 2D, propre à la vue | `create_detail_line` |
| Ligne de modèle, 3D | `create_model_line` |
| Séparer une pièce sans mur | `create_room_separation_line` |
| Poser un cartouche sur une feuille existante | `place_title_block` |

## À éviter

- Partir du niveau 4 quand le niveau 1 répond.
- Appeler `filter_elements` sans l'enveloppe `data`, ou avec `maxElements: 1000` par défaut.
- Lancer `audit_families` globalement pour chercher une seule catégorie.
- Deviner le nom d'un paramètre projet (`WBS_*`, `Code_*`) au lieu de le découvrir.
- Lire un nombre sans son `unit`, ou une colonne vide comme une valeur vide.
- Chercher un type système avec `list_family_types` en espérant un `familyName`.
- Utiliser `save_as_document` pour obtenir un projet vide.
