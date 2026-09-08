---
name: rivett
description: Guide opérateur autonome pour piloter Autodesk Revit 2026.5+ ou 2027 avec RiveTT, choisir les outils MCP, lire leurs réponses, sécuriser les écritures, contrôler une maquette et exécuter les workflows IFC.
---

# RiveTT — guide opérateur unifié

RiveTT connecte une application d'IA à Autodesk **Revit 2026.5+ ou 2027** sur Windows x64. Il démarre avec Revit, communique localement sans port réseau et expose ses commandes par MCP. Le panneau **RiveTT** de l'onglet *Compléments* porte le verrou d'écriture.

Ce document est autonome : les règles, procédures, conventions, signatures et l'inventaire utile sont réunis ci-dessous. Aucun chargement de référence séparée n'est nécessaire.

## Règles impératives

1. Commencer toute nouvelle session par un `get_project_info` complet. Les appels suivants peuvent masquer les niveaux, phases, sous-projets et liens déjà connus.
2. Avant la première écriture, puis dès que la cible semble avoir changé, vérifier `execution.documentTitle` et `execution.revitProcessId`. Avec plusieurs Revit ouverts, le serveur joint l'instance la plus récemment démarrée.
3. Chaque session Revit démarre en lecture seule. `execution.writesAllowed`, et non `execution.toolReadOnly`, indique si les écritures sont autorisées. Seul un humain peut lever le verrou depuis **Compléments → RiveTT → Écriture**.
4. Sur `PermissionDenied` avec `writesAllowed: false`, s'arrêter et demander le déverrouillage. Aucun outil, pas même un `dryRun`, ne contourne ce verrou.
5. Prévisualiser avec `dryRun: true` lorsque `execution.supportsDryRun` vaut `true`. Si cette capacité vaut `false`, une demande de `dryRun` est refusée avec `InvalidInput` avant toute exécution.
6. Après une écriture, vérifier le résultat avec un outil de lecture et se fier à ce qui a réellement été appliqué dans la réponse, pas seulement à ce qui avait été demandé.
7. Préférer un outil RiveTT dédié. `send_code_to_revit` est un dernier recours, à prévisualiser et à relire intégralement avant exécution.
8. Résoudre les paramètres en anglais ou dans la langue du document. Un échec doit apparaître dans `unresolvedParameterNames` ou `skippedFields[].reason` ; une valeur vide sans ce signalement est une donnée réellement vide.
9. Préférer `categoryBic` (`OST_*`) aux libellés localisés. Revit français nomme notamment la catégorie des vues portées « Fenêtres », comme les fenêtres.
10. Lire toute valeur numérique avec son `unit` et son `internalValue`. Revit stocke les longueurs en pieds, les surfaces en pieds carrés et les volumes en pieds cubes.
11. Énumérer les types système avec `list_system_types`. Pour inclure les familles chargeables, notamment cartouches et meneaux, passer `includeLoadable: true`.
12. Si `execution.versionMismatch` apparaît, quitter complètement l'application d'IA, relancer l'installateur, puis rouvrir l'application. Redémarrer Revit seul ne corrige pas la moitié serveur restée ancienne.

## Organisation du guide

| Besoin | Section |
|---|---|
| Installer, mettre à jour ou dépanner | Installation ; Diagnostic |
| Démarrer une session, trouver des éléments et choisir un type | Conduite de session |
| Créer, modifier, supprimer ou exécuter du C# | Modifier la maquette |
| Comprendre le verrou, l'audit et les limites de confiance | Verrou d'écriture ; Sécurité |
| Contrôler la santé, les conflits, les vues et les annotations | Contrôle du modèle, vues et annotations |
| Lier, reconstruire ou exporter un IFC | IFC |
| Connaître le contrat d'un outil | Signatures des outils ; Inventaire des outils |

---

## Installation

Prérequis : Revit 2026.5 ou supérieur, ou Revit 2027, en x64.

**Avant de lancer l'installateur, fermez complètement votre application d'IA** —
Claude, ChatGPT ou celle que vous utilisez avec Revit. Quittez l'application, ne
fermez pas seulement sa fenêtre. Elle garde ouvert un fichier que l'installateur doit
remplacer ; sinon la mise à jour ne s'applique qu'à moitié, et RiveTT répond ensuite
que certaines commandes n'existent pas. L'installateur vous prévient s'il détecte le
cas.

**Revit peut rester ouvert.** Windows interdit d'écraser un fichier chargé par un
processus mais autorise à le *renommer* : l'installateur renomme le fichier verrouillé
en `<nom>.old-<horodatage>` — Revit continue d'utiliser l'ancien jusqu'à son
redémarrage — et écrit le neuf à sa place. Les copies garées sont supprimées à
l'installation suivante.

**Aucun droit administrateur n'est nécessaire** et Windows n'affiche pas d'invite
UAC : tout est installé dans votre profil utilisateur. Rien n'exige non plus le
runtime .NET — le serveur est autonome.

**Aucune désinstallation préalable** : l'installateur remplace l'installation
précédente.

L'installateur détecte les versions de Revit présentes et n'installe que pour
celles-là. S'il ne trouve qu'un Revit 2026.0 à 2026.4, il s'arrête en l'indiquant :
ces versions tournent sur .NET 8 et ne peuvent pas charger le plugin. Appliquez la
mise à jour 2026.5 depuis Autodesk Access.

### Déclarer le serveur dans votre application d'IA

L'installateur le propose, par deux cases à cocher **décochées par défaut** — cocher
revient à modifier la configuration d'un autre logiciel, c'est à vous de le demander :

- *Configurer la connexion MCP pour Claude* : connexion déclarée ; aucun ZIP ou
  skill Claude n'est nécessaire au fonctionnement du MCP ;
- *Configurer pour ChatGPT (config + skill)* : connexion et skill local installés
  ensemble pour ChatGPT Desktop.

Les emplacements exacts et les étapes figurent dans
la section **Configuration de Claude et ChatGPT Desktop** ci-dessous.

La page finale dit, pour chaque case cochée, si la déclaration a réussi. Si Claude
n'est pas détecté, elle donne aussi le chemin du script et du journal. À la main,
sinon :

    codex mcp add RiveTT -- "%LOCALAPPDATA%\RiveTT\server\RiveTT.Server.exe"

### Après l'installation, dans l'ordre

1. **redémarrer Revit**, puis ouvrir un projet et attendre quelques secondes que la
   session soit publiée ;
2. **rouvrir votre application d'IA** — celle que vous avez fermée avant d'installer.
   C'est à son démarrage qu'elle découvre les commandes de la nouvelle version.

### Désinstaller

Par *Applications installées* dans les paramètres Windows. **Fermez Revit avant** :
contrairement à une mise à jour, il n'y a pas de nouveau fichier pour prendre la place
de l'ancien.

### Si RiveTT dit qu'une commande n'existe pas

C'est le symptôme d'une mise à jour à moitié appliquée : certaines commandes répondent
normalement, d'autres sont introuvables. La cause est presque toujours une application
d'IA restée ouverte pendant l'installation.

Fermez-la complètement, relancez l'installateur, rouvrez-la. Redémarrer Revit ne suffit
pas : le morceau resté en arrière n'est pas dans Revit.

Pour le vérifier : n'importe quelle réponse de RiveTT porte les deux versions
installées, et un troisième champ qui n'apparaît que si elles diffèrent.

    execution.pluginVersion      la moitié installée dans Revit
    execution.mcpServerVersion   la moitié installée à côté, que l'IA lance
    execution.versionMismatch    présent uniquement si les deux diffèrent

Quand `versionMismatch` est là, la liste des commandes visibles est celle de la seconde
moitié : une commande renommée entre les deux répond « not found », et un réglage ajouté
entre les deux est ignoré sans le dire.

---

## Le verrou d'écriture

Le connecteur se charge avec Revit et ne demande aucune autorisation par appel. Sans
interrupteur, la seule limite entre un agent connecté et la maquette serait son propre
jugement. D'où le panneau **Compléments → RiveTT** :

| Bouton | Icône | Effet |
|---|---|---|
| **Lecture seule** | **rivet bleu**, dressé au-dessus de deux plaques encore libres | Tout outil susceptible de modifier la maquette est refusé avec `PermissionDenied`, **avant exécution** : la maquette n'est pas touchée. Les outils de lecture répondent normalement |
| **Écriture** | **rivet orange**, posé, les deux plaques assemblées | Les outils d'écriture redeviennent exécutables. Chaque appel reste transactionnel et journalisé |
| **État** | pastille bleue d'information | Mode Lecture seule ou Écriture autorisée, disponibilité de RiveTT, document actif, version et accès au journal d'activité |

Le rivet est celui du nom : froid et libre, rien n'est assemblé ; chaud et posé, la
liaison est faite. C'est le moyen le plus simple de lire l'état d'un coup d'œil.

Les deux premiers forment une **paire à bascule** : l'un des deux est toujours
enfoncé, et c'est *Lecture seule* à chaque démarrage de Revit. Pour connaître l'état
courant, il suffit donc de regarder lequel l'est — pas besoin d'interroger le
connecteur.

Trois propriétés à retenir :

1. **Chaque session Revit démarre en lecture seule.** Le mode n'est pas persisté d'une
   session à l'autre : c'est une décision explicite, prise à chaque fois.
2. **`dryRun: true` ne contourne pas le verrou.** Une prévisualisation est une promesse
   de l'outil, pas une frontière de permission ; s'y fier rendrait le verrou aussi
   solide que le plus faible des outils.
3. **Aucun outil MCP ne peut lever le verrou** — il n'existe pas d'outil pour ça, et un
   test de contrat vérifie qu'aucun fichier d'outil n'appelle la politique d'écriture.
   Seul le bouton du ruban le fait.

Le verrou survit à l'ouverture, la fermeture et l'enregistrement sous d'un document :
il décrit la session Revit, pas le fichier ouvert.

Côté agent, l'état est lisible partout : `execution.writesAllowed` sur chaque réponse,
et le bloc `readOnlyMode` de `get_server_capabilities`. Sur un refus, la réponse indique
où se trouve le bouton — il n'y a rien à réessayer.

Le classement tient compte de l'action pour les outils mixtes explicitement déclarés.
Le catalogue `get_server_capabilities.commandsAvailableWhenLocked` indique les appels
accessibles en lecture seule. Les autres actions d'écriture restent refusées ;
`execution.toolReadOnly` décrit l'appel et `execution.writesAllowed` le verrou de session.

---

## Sécurité

Le serveur MCP échange sur `stdio` ; le relais avec Revit passe par un canal nommé
Windows créé en `CurrentUserOnly`. **Aucun port TCP n'est ouvert.**

### Limites de confiance

- L'application d'IA et Revit doivent tourner sous le même compte Windows.
- Les appels Revit passent par `ExternalEvent`, puis par les transactions de l'API.
- Chaque appel est consigné dans `%LOCALAPPDATA%\RiveTT\audit.jsonl`.
- Il n'y a ni télémétrie, ni compte, ni licence, ni mise à jour automatique.

Le mode automatique permanent supprime les boîtes de confirmation, mais pas les
transactions, le journal d'audit ni la validation des entrées.

### Exécution de code C#

`send_code_to_revit` est un dernier recours. Il **prévisualise par défaut** : `dryRun`
vérifie le bac à sable et rapporte ce qui serait exécuté, sans rien exécuter ni écrire
sur disque. Il n'existe aucune boîte de confirmation dans Revit — la prévisualisation
est la seule étape de relecture.

`CodeSandbox` refuse les accès fichiers et réseau, la création de processus, le
registre, l'interop native et les détours par la réflexion.

**Ce n'est pas une frontière de sécurité**, et il ne faut pas s'en servir comme telle.
C'est un filtre par motifs sur le texte du code : il arrête l'erreur et le geste
évident, pas quelqu'un qui cherche à passer. Surtout, l'API Revit qu'il autorise par
construction écrit elle-même sur disque (`Document.SaveAs`, les exports) et supprime
des éléments (`Document.Delete`) — aucun filtre ne peut l'interdire sans interdire
l'outil.

Ce qui protège réellement, dans l'ordre : le **verrou d'écriture** du ruban, le
`dryRun` par défaut de cet outil, et le **journal d'audit**, qui conserve le code et
son empreinte SHA-256. Relire le script avant de le lancer reste l'étape que rien ne
remplace.

### Données locales

Fichiers de session, scripts temporaires et journaux sont sous `%LOCALAPPDATA%\RiveTT`.
La désinstallation **conserve le journal d'audit**, pour ne pas supprimer des données
sans le dire.

---

---

## Conduite de session

### Ouvrir la session

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

### Lire une réponse

| Champ | Ce qu'il dit |
|---|---|
| `execution.writesAllowed` | verrou d'écriture de la session — **faux au démarrage de chaque session Revit** |
| `execution.toolReadOnly` | classe l'outil qui répond, pas la session |
| `execution.cached` | réponse servie par le cache, pas une observation fraîche |
| `execution.versionMismatch` | serveur MCP et plugin de versions différentes — appliquer la règle impérative 12 |
| `unresolvedParameterNames` | noms de paramètres non résolus. **Une colonne vide sans ce champ est une vraie valeur vide** |
| `unit` / `internalValue` | Revit stocke des pieds, pi² et pi³ quelles que soient les unités du projet |
| `categoryBic` | le code `OST_*`, non ambigu |

### Connaître l'état du modèle

Par coût croissant. Ne monter d'un niveau que si le précédent ne suffit pas.

| | Outil | Coût | Quand |
|---|---|---|---|
| 1 | `check_model_health` | ~200 jetons | contrôle rapide |
| 2 | `analyze_model_statistics` (`compact: true`) | ~400 | statistiques de base |
| 3 | `workflow_model_audit` filtré | ~800 | audit ciblé |
| 4 | `workflow_model_audit` complet | ~3000 | audit complet, rare |

### Trouver des éléments

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

### Trouver ou dupliquer un type

| Cas | Outil |
|---|---|
| Type de famille chargeable (porte, fenêtre, cartouche) | `list_family_types` avec `kind: loadable` |
| Type système (mur, sol, garde-corps, escalier) | `list_system_types(category)` — sans catégorie, rend l'inventaire avec les codes `OST_*` |
| Dupliquer | `duplicate_family_type` (chargeable) / `duplicate_system_type` (système) |

`duplicate_family_type` échoue sur un type système : ce ne sont pas des familles
chargeables. C'est la confusion la plus fréquente.

### Documents et familles

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

### Groupes

| Cas | Outil | Ce qui se passe vraiment |
|---|---|---|
| Retirer un membre | `delete_element`, ou `edit_group_members` avec `removeElementIds` seul | c'est une **exclusion** : cette instance seule, le type et les autres instances intacts, instance renommée « (membre exclu) » |
| Ajouter un membre | `edit_group_members` avec `addElementIds` | impose dégrouper/regrouper : **nouveau type**, les autres instances restent sur l'ancienne définition |
| Restaurer un membre exclu | aucun outil | uniquement depuis le ruban Revit |

Des instances qui diffèrent, c'est normal : exclusions ou contraintes de niveau
propres. Lire `hasExcludedMembers` **par instance**, ne pas se fier à la première.

### Dessiner

| Cas | Outil |
|---|---|
| Ligne de détail, 2D, propre à la vue | `create_detail_line` |
| Ligne de modèle, 3D | `create_model_line` |
| Séparer une pièce sans mur | `create_room_separation_line` |
| Poser un cartouche sur une feuille existante | `place_title_block` |

### À éviter

- Partir du niveau 4 quand le niveau 1 répond.
- Appeler `filter_elements` sans l'enveloppe `data`, ou avec `maxElements: 1000` par défaut.
- Lancer `audit_families` globalement pour chercher une seule catégorie.
- Deviner le nom d'un paramètre projet (`WBS_*`, `Code_*`) au lieu de le découvrir.
- Lire un nombre sans son `unit`, ou une colonne vide comme une valeur vide.
- Chercher un type système avec `list_family_types` en espérant un `familyName`.
- Utiliser `save_as_document` pour obtenir un projet vide.

---

## Modifier la maquette

### Prévisualiser avant d'écrire

RiveTT est en mode automatique permanent et n'ouvre aucune boîte d'autorisation. La
sûreté vient donc d'ailleurs : prévisualisation explicite, entrées étroites,
transactions Revit, erreurs structurées, vérification après coup.

1. Si l'outil accepte `dryRun`, l'appeler d'abord avec `dryRun: true`. Ce que l'outil
   accepte se lit dans `execution.supportsDryRun` d'une réponse précédente, ou dans
   `get_server_capabilities`. Un `dryRun` demandé à un outil qui n'en a pas revient en
   `InvalidInput` sans avoir rien exécuté : le refus n'est pas un échec de l'appel,
   c'est la réponse « cet outil applique, il ne prévisualise pas ».
2. Résumer les comptes et les avertissements importants — ne pas déverser la liste.
3. N'exécuter avec `dryRun: false` que si la demande autorise l'écriture **et** que
   l'aperçu correspond à la portée voulue.
4. Le vrai appel reprend les mêmes entrées, à `dryRun` près.
5. Vérifier le résultat par une lecture ensuite.

**Toute prévisualisation doit porter `mutated: false`.** Son absence est une rupture
de contrat : ne pas enchaîner sur l'écriture réelle.

### Lire ce que la réponse dit vraiment

Un succès n'est pas un résultat utilisable. Plusieurs outils réussissent en
produisant quelque chose d'inexploitable, et le disent :

| Outil | Champ à lire | Ce qu'il révèle |
|---|---|---|
| `create_room` | `enclosed`, `areaM2` | une pièce non fermée a une aire nulle et ne sert à rien |
| `create_sheet` | `hasTitleBlock` | sans cartouche, c'est une feuille A4 nue sans cadre |
| `create_stair` | `reachesTopLevel` | la volée peut ne pas atteindre le niveau visé |
| tous | `warnings`, `notFoundIds`, `unresolvedParameterNames`, `skippedFields`, `cascadedElements` | ce qui a été sauté, et pourquoi |

Après une écriture, lire d'abord ce rapport, avant de relire le modèle. Et une
lecture qui revient avec `execution.cached: true` est une réponse de cache, pas une
observation fraîche.

Pour une transaction complexe, démarrer en `warningPolicy: allow_list` quand les GUID
de `FailureDefinition` acceptables sont connus : un avertissement inattendu provoque
alors un retour arrière au lieu d'être masqué. Un retour arrière expose `warnings`,
`errors`, `failedElementIds` et `repairHints`.

`save_document` et `save_as_document` se prévisualisent aussi : chemins, existence de
la cible, politique d'écrasement, droits sur le dossier, verrous, modifications non
enregistrées — sans rien écrire. Utile avant un « enregistrer sous » de plusieurs
centaines de Mo. Rappel : `save_as_document` **duplique** le document ouvert.

### Paramètres

#### Quel outil

| Cas | Outil | Remarque |
|---|---|---|
| 1 élément, 1 à 3 paramètres | `set_element_parameters` | |
| N éléments, même paramètre et même valeur | `batch_modify_parameter_values` | `dryRun` obligatoire d'abord |
| N éléments, valeurs différentes | `sync_csv_parameters` | lignes portant `elementId` ; `parameterMap` pour viser un `BuiltInParameter` |
| Recopier d'un élément à d'autres | `match_element_properties` | toujours avec `parameterNames` explicite |

#### Découvrir les noms

Ne jamais supposer le nom d'un paramètre projet. `get_element_parameters` sur **un**
élément témoin donne les noms exacts ; les paramètres de type y sont préfixés
`[Type]`.

Sur un projet localisé, mapper les en-têtes stables vers un `BuiltInParameter`
(`{"Numéro": "ROOM_NUMBER"}`) plutôt que de dépendre du texte affiché.

Pour filtrer sur un paramètre de **type** — le nom du type, par exemple —
`filter_by_parameter_value` avec `parameterType: "type"`. Le défaut `"both"` peut ne
pas résoudre une chaîne de niveau type.

#### Garder une portée stable

Entre l'aperçu, la vérification et l'écriture, la sélection ne doit pas bouger. Par
ordre de préférence :

1. `elementIds` explicites ;
2. `capture_selection` et son `selectionToken` temporaire ;
3. `savedSelectionName`, pour une sélection persistée dans le modèle ;
4. `scope: selection`, seulement pour un appel unique et immédiat.

### Escalader vers send_code_to_revit

**Jamais de son propre chef pour une opération en masse.** Toujours demander
l'accord, en proposant l'alternative native comme option A :

> « Je peux utiliser `send_code_to_revit` pour faire ça plus efficacement avec un
> script C#, ou procéder avec les outils dédiés — ce qui demandera plus d'appels.
> Que préférez-vous ? »

Les raisons de demander plutôt que de supposer :

- un script contourne les schémas dédiés, et donc leur `dryRun` ;
- des conflits de DLL avec d'autres add-ins peuvent faire échouer `send_code_to_revit`
  silencieusement ;
- l'utilisateur peut préférer la traçabilité d'appels d'outils distincts.

#### Bac à sable

Ces espaces de noms sont refusés par `CodeSandbox.Validate` avec
`RiveTTErrorCode.PermissionDenied` :

`System.IO` · `System.Net` · `System.Diagnostics.Process` · `Microsoft.Win32` ·
`System.Reflection.Emit` · `System.Runtime.InteropServices`

#### Conventions de code

- le document se nomme `document`, jamais `doc` ni `uidoc` ;
- `new UIDocument(document)` pour l'interface ;
- un `ElementId` se lit par `.Value`.

Un `ExternalEvent` est un contexte d'API valide, et moins contraint qu'on ne l'a cru :
changer de document actif et ouvrir un `StairsEditScope` y fonctionnent. Avant de
déclarer une opération impossible, vérifier que la restriction vise bien ce
contexte-là et non un gestionnaire d'événement API ou un éditeur modal.

### À éviter

- Enchaîner `set_element_parameters` en boucle sur N éléments.
- Lancer `batch_modify_parameter_values` sans `dryRun`.
- Lire la liste complète des éléments depuis un `dryRun` : n'en tirer que
  `modifiedCount` et `skippedCount`, et vérifier que `processed` correspond à la
  portée attendue.
- Utiliser `send_code_to_revit` pour contourner un outil dédié ou son `dryRun`.
- Supposer que l'utilisateur préfère un script : c'est l'option B par défaut.

---

## Conventions corrigées en 0.5.1

- Surfaces et éléments linéaires : `baseLevelId` désigne un ID de niveau ; une altitude absolue en mm se passe par `baseElevationMm`. `baseOffset` est en mm. `baseLevel` est refusé : ne pas employer une altitude comme ID. Relire le niveau et le décalage appliqués dans la réponse.
- Les nombres nus de `set_element_parameters` suivent les unités internes Revit ; préférer les chaînes avec unité, par exemple `"2500 mm"`. La convention mm des outils de géométrie ne s'applique pas implicitement aux paramètres.
- Les lots peuvent retourner des éléments ignorés : contrôler `skipped`, `warnings` et les IDs effectivement créés.
- Pour les pièces, un volume null avec calcul désactivé n'est pas un volume mesuré à zéro. Les totaux d'ouvertures distinctes diffèrent de leurs occurrences par pièce.

## Outils et conventions RiveTT

- `get_server_capabilities` : contrat effectif du serveur — mode automatique, audit,
  réponses, sélection, limitations de cycle de vie.
- `create_wall` : type et niveau de base explicites, niveau supérieur et décalages
  optionnels. Les coordonnées de `locationLine` sont des coordonnées projet absolues en
  millimètres ; `baseOffset` et `topOffset` sont relatifs à leurs niveaux. Prévisualise
  par défaut et retourne la base, le sommet et les décalages réellement appliqués.
- `create_door` / `create_window` : type, hôte et niveau explicites.
- `create_railing` : garde-corps natif depuis un chemin horizontal.
- `set_wall_host` : mur hôte — **Revit 2027 uniquement**, l'API n'existe pas en 2026.
- `capture_selection` : capture des identifiants explicites ou de la sélection Revit
  dans un jeton temporaire réutilisable. Les outils de masse acceptent aussi
  `savedSelectionName`, `last_filter` et `elementIds`.
- `duplicate_storey` : analyse puis duplication transactionnelle d'un étage, avec
  catégories, groupes, niveau haut des murs et déplacement optionnel des niveaux
  supérieurs.
- `detach_wall_constraint` : retrait d'une contrainte haute de niveau ou d'une attache,
  en conservant la hauteur non contrainte.
- `manage_model_groups` : inventaire, duplication de type et dissociation contrôlée.
- `save_document` / `save_as_document` : sauvegarde du document actif, avec `dryRun`
  (chemins, existence de la cible, écrasement, accessibilité du dossier, verrou,
  modifications non enregistrées). `save_as_document` **duplique le document ouvert** :
  ce n'est pas un nouveau projet vierge.
- `list_system_types` : types système (murs, sols, plafonds, toits, garde-corps,
  escaliers, cartouches…), inaccessibles autrement. Sans catégorie, rend l'inventaire
  par catégorie avec les codes `OST_*`.
- `create_detail_line` / `create_model_line` / `create_room_separation_line` : lignes
  2D, 3D et séparations de pièces. Couper une pièce sans mur physique se fait avec la
  troisième.
- `place_title_block` : pose un cartouche sur une feuille existante — réparation d'une
  feuille sans cadre.
- `create_document` : **nouveau projet vierge** depuis un gabarit `.rte`, enregistré au
  chemin demandé. C'est le vrai « nouveau projet » ; `save_as_document` duplique le
  modèle ouvert avec tout son historique. `activate: true` l'ouvre ensuite dans Revit.
- `open_document` : ouvre un `.rvt` et en fait le document actif. Tous les appels
  suivants le ciblent et les caches sont vidés. Enregistrer le document courant avant :
  le changement ne le sauvegarde pas.
- `create_stair` : escalier par composant entre deux niveaux, volées droites (`runs`) et
  paliers automatiques. La réponse compare `actualRiserCount` à `desiredRiserCount` et
  donne `reachesTopLevel` : une volée trop courte produit un escalier qui n'atteint pas
  l'étage.
- `edit_group_members` : ajout et retrait de membres. L'API Revit ne sait pas modifier
  un groupe en place : l'outil dégroupe, modifie, regroupe, et **crée donc un nouveau
  type**. Refuse un type à plusieurs occurrences sauf `allowMultiInstance: true`, car
  les autres occurrences gardent l'ancienne définition.

### Groupes, exclusions et occurrences divergentes

Deux occurrences d'un **même type** de groupe peuvent légitimement différer — ce n'est
pas une anomalie du modèle :

- **exclusion de membre** : un élément retiré d'une occurrence seulement. Revit suffixe
  alors le nom de cette occurrence par « (membre exclu) ». Le type, sa définition et les
  autres occurrences ne bougent pas ;
- **contraintes propres** : un mur groupé peut être plus haut dans une occurrence parce
  que ses contraintes de niveau y sont différentes.

| Opération | Ce que fait le connecteur |
|---|---|
| Retirer un membre | `delete_element` sur ce membre, ou `edit_group_members` avec `removeElementIds` seuls : c'est une **exclusion**. Le type garde son id et ses autres occurrences leurs éléments. La réponse liste `groupExclusionIds` |
| Ajouter un membre | Seule voie possible : dégrouper, modifier, regrouper — donc **nouveau type** ; les autres occurrences gardent l'ancienne définition. Refusé sur un type à plusieurs occurrences sauf `allowMultiInstance: true` |
| Rétablir un membre exclu | **Impossible par l'API.** Sélectionner l'occurrence dans Revit puis « Rétablir les éléments exclus » du ruban |
| Dissoudre | `manage_model_groups action=ungroup` sur les occurrences visées |
| Détecter les exclusions | `manage_model_groups` rend, par occurrence, `memberCount`, `excludedCount` et `hasExcludedMembers`, et lit la définition complète depuis l'occurrence la plus fournie |

Chaque occurrence possède **ses propres copies** des membres, avec ses propres
identifiants : un id relevé sur une occurrence n'a aucun sens dans une autre.

### Changer la hauteur d'un niveau sans casser les groupes

Modifier l'altimétrie d'un niveau sous des groupes contraints les déforme ou les éclate
en types divergents. Procédure qui préserve les symétries et l'identité du type :

1. créer deux niveaux temporaires au-dessus du bâtiment, à l'écart (`create_level`), en
   conservant **le même écart** que les niveaux d'origine ;
2. copier les groupes vers ces niveaux (`copy_elements` avec `offsetZ`, qui réassocie
   les contraintes haute et basse aux niveaux réels) ;
3. faire les manipulations d'altimétrie sur les niveaux d'origine
   (`create_level action=set`) ;
4. recopier les groupes depuis les niveaux temporaires vers les niveaux redéfinis, puis
   supprimer les niveaux temporaires.

Les groupes reviennent avec leurs miroirs et leur type d'origine, ce qu'aucun
dégroupage/regroupage ne sait faire.

### Conventions à connaître

- **Unités.** Entrées en millimètres. En sortie, toute valeur numérique de paramètre
  porte `value` (unités du projet), `unit`, `displayValue` et `internalValue` (unités
  internes Revit : pieds, pieds², pieds³).
- **Altimétrie.** `create_wall` ignore le `z` de `locationLine` : `baseLevelId` et
  `baseOffset` font foi. `create_door` / `create_window` attendent un `z` **absolu
  projet**, sauf avec `zMode: "relativeToLevel"` où `z` s'ajoute à l'altitude du niveau.
  Un point hors de la plage verticale de l'hôte est refusé, avec les bornes en mm.
- **Noms de paramètres.** Ils se résolvent en anglais comme dans la langue du document
  (`Mark`/`Repère`, `Level`/`Niveau`, `Width`/`Largeur`). Un nom non résolu est signalé
  dans `unresolvedParameterNames` avec des suggestions, jamais rendu par une colonne
  vide.
- **Catégories.** Les libellés sont localisés et parfois ambigus — Revit FR nomme la
  catégorie des vues portées « Fenêtres », comme les fenêtres. Préférer le code `OST_*`
  rendu dans `categoryBic`.

### Réponses et pagination

Chaque succès contient `execution.connector`, `pluginVersion`, `mcpServerVersion`,
`revitVersion`, `revitProcessId`, `documentTitle`, `mode`, `toolReadOnly`,
`toolDestructive`, `supportsDryRun`, `writesAllowed` et `cached`, plus `versionMismatch`
quand les deux versions diffèrent.

`revitProcessId` et `documentTitle` disent **dans quel Revit et dans quel fichier**
l'appel a eu lieu. Avec deux instances de Revit ouvertes, le serveur se connecte à la
plus récemment démarrée : c'est un choix implicite, et ces deux champs sont ce qui le
rend visible. Les surveiller sur une session longue — s'ils changent, la cible a
changé.
Un aperçu d'écriture contient toujours `dryRun: true` et `mutated: false`.

`supportsDryRun` dit si **cet outil-là** sait prévisualiser. Quand il vaut `false`,
passer `dryRun: true` est **refusé** avec `InvalidInput`, avant exécution : l'outil
n'est pas lancé du tout et la maquette n'est pas touchée. C'est délibéré. Le serveur
indiquait auparavant `mutated: false` sur la seule foi de la demande du client, si
bien qu'un outil sans `dryRun` écrivait dans la maquette et répondait malgré tout que
rien n'avait changé.

`toolReadOnly` classe **l'outil qui répond**, ce n'est pas un état de session. L'état de
session, c'est `writesAllowed` : il vaut **`false` au démarrage de chaque session
Revit**, et seul le bouton *Écriture* du ruban le passe à `true`. `cached: true` signale
une réponse servie par le cache ; tout cache est vidé après
`save_document` / `save_as_document`.

`filter_elements` utilise `responseMode: summary | idsOnly | details` et rend
`totalCount`, `returnedCount`, `appliedLimit` et `nextCursor`. Un curseur devient
invalide dès que le document Revit change, pour éviter de mélanger deux états du modèle.

Les erreurs de transaction fournissent `warnings`, `errors`, `rolledBack`,
`failedElementIds` et `repairHints`. Les workflows complexes acceptent
`warningPolicy: suppress_all | allow_list` ; avec `allow_list`, tout avertissement non
autorisé provoque un retour arrière.

### Limites de cycle de vie

L'interdiction d'activer un document vise les **gestionnaires d'événements API**
(`Idling`, `DocumentChanged`), pas un `ExternalEvent` — le contexte dans lequel tourne
chaque outil de ce connecteur. `open_document`, `create_document`, `open_family` et
`open_template` sont donc disponibles, conformément à la recommandation Autodesk.

`open_family` (.rfa) et `open_template` (.rte) activent le fichier dans l'interface
Revit : le document actif change, donc tout appel suivant cible ce fichier jusqu'au
retour sur le projet. `close_document` referme un document ouvert ; fermer le document
**actif** exige qu'un autre soit ouvert pour y basculer d'abord — `Document.Close(false)`
refuse le document actif (mesuré sur maquette le 27/08/2026). C'est une contrainte
réelle de l'API, pas un défaut à contourner.

`edit_family` modifie les valeurs de paramètres de type d'une famille **en arrière-plan**,
aucune fenêtre ne s'ouvre. Limité aux types existants et à leurs valeurs de paramètre
(cotes, matériaux, oui/non, texte) : ni nouveaux types, ni géométrie. Pour cela,
`open_family` puis édition visuelle dans Revit.

Restent indisponibles, et `get_server_capabilities` le déclare :

- **escaliers esquissés**, volées hélicoïdales et balancements : `create_stair` couvre
  l'escalier par composant, volées droites et paliers ;
- **édition de groupe en place** : l'API ne le permet pas ;
- **propagation d'armatures** : absente de l'API Revit sur toutes les versions prises en
  charge.

---

---

## Contrôle du modèle, vues et annotations

### Contrôle rapide du matin

Séquence canonique, 500 à 800 jetons :

1. `check_model_health` avec `compact: true` ;
2. `list_warnings` avec `maxWarnings: 10` — **jamais le défaut**, qui en rend 500 ;
3. facultatif : `detect_clashes` sur un couple de disciplines.

Puis s'arrêter. Ne pas enchaîner sur de l'authoring dans la même session : le
contrôle qualité et la production ont des rythmes et des coûts différents.

| Besoin | `maxWarnings` |
|---|---|
| Contrôle rapide | 10 |
| Analyse d'une catégorie | 50 |
| Export complet | sans limite, assumé |

### Conflits

| Besoin | Outil | Coût |
|---|---|---|
| Compte et liste d'identifiants | `detect_clashes` | 400 à 600 jetons |
| Revue visuelle 3D avec boîte de coupe | `show_clashes` | 800 et plus |

Préciser les deux catégories exactes. Sur un modèle d'architecture, les poteaux sont
`OST_Columns`, **pas** `OST_StructuralColumns` — l'erreur rend un résultat vide qui
ressemble à « aucun conflit ».

### count_lines_per_view

**Cet outil peut faire tomber le serveur sur un modèle de plus de 300 vues.**

- Ne jamais le lancer en parallèle d'un autre outil.
- Toujours avec `threshold >= 20`.
- Sur une grosse maquette, envisager de ne pas l'appeler du tout.

### Vues et annotations

#### Ce qui dépend de la vue active

`tag_rooms`, `tag_walls` et `color_elements` n'opèrent que sur la **vue active de
Revit**, et seulement si elle contient des éléments visibles de la catégorie visée.
Vérifier avec `get_current_view_info` avant, systématiquement.

`color_elements` échoue sur une feuille ou une page de garde : basculer d'abord sur
un plan ou une vue 3D. Il attend par ailleurs des noms de catégorie **localisés**,
donc dépendants de la langue de Revit.

#### Cotes

Le Z passé à `create_dimensions` doit correspondre **exactement** à l'altitude du
niveau. La prendre dans `get_project_info`, jamais à la main : une approximation ne
produit pas une cote approximative, elle produit une cote qui n'accroche rien.

#### Nommer une vue

Ces caractères sont refusés par Revit dans un nom de vue :

```
:  \  /  {  }  [  ]  |
```

Pour un horodatage, `HH-mm-ss` — jamais `HH:mm:ss`.

### À éviter

- `workflow_model_audit` pour un contrôle rapide : 3000 jetons contre 500 à 800.
- `list_warnings` sans `maxWarnings`.
- `count_lines_per_view` en parallèle, ou sans `threshold`.
- Mélanger contrôle qualité et production dans une même longue session.
- Poser des tags ou des couleurs sans avoir vérifié la vue active.
- Approximer le Z d'une cote.
- Mettre `:` ou `/` dans un nom de vue.

---

## IFC

### Ce qu'un import IFC produit

Des **DirectShape** : des volumes sans intelligence Revit. Un mur importé n'est pas un
mur, c'est une forme qui y ressemble — pas de type, pas de couches, pas d'hôte pour
les percements. Toute la « reconstruction » consiste à remplacer ces volumes par des
éléments natifs, catégorie par catégorie.

C'est aussi pourquoi lier un IFC (`ifc_link`) suffit dans la plupart des cas : pour
coordonner avec la structure ou les fluides, on n'a pas besoin d'éléments natifs. La
reconstruction ne se justifie que si l'on doit **reprendre** le modèle.

### Toujours commencer par les capacités

`ifc_get_capabilities` en premier appel IFC de la session : il dit quelles versions
IFC sont prises en charge et si le module `revit-ifc` est présent. Importer un IFC
lourd sans l'avoir demandé, c'est découvrir l'incompatibilité après l'attente.

### Lier ou importer

| Besoin | Outil |
|---|---|
| Référence de coordination | `ifc_link` |
| Recharger un lien existant | `ifc_reload_link` |
| Ouvrir en document Revit, ou importer | `ifc_open_or_import` |

Deux choses à savoir avant de lancer :

- un **fichier `.RVT` intermédiaire est créé à côté du fichier IFC d'origine**. Il
  faut donc un dossier accessible en écriture. `recreateLink: false` réutilise un
  `.RVT` déjà généré au lieu de le refaire ;
- l'option d'import `parametric` donne des éléments plus modifiables, mais l'import
  est plus lent et **la géométrie n'est pas toujours préservée**. Sans elle, tout
  arrive en DirectShape.

### Reconstruire en éléments natifs

Dans cet ordre :

1. `ifc_analyze_rebuildability` avec `compact: true` — classe chaque DirectShape
   reconstructible ou non, avec un **indice de confiance**. Compter 60 à 80 % de
   reconstructible sur un modèle ordinaire ;
2. `ifc_list_rebuild_candidates` avec `compact: true`, filtré par catégorie ;
3. la reconstruction, **une catégorie à la fois**, chacune en `dryRun` d'abord :
   `ifc_rebuild_walls` · `ifc_rebuild_floors` · `ifc_rebuild_roofs` ·
   `ifc_rebuild_structural_members` · `ifc_rebuild_openings` ·
   `ifc_rebuild_family_instances` ;
4. `ifc_compare_original_vs_rebuilt` — rend un score de fidélité ;
5. `ifc_tag_unreconstructable_elements` pour marquer ce qui n'a pas pu l'être, plutôt
   que de le laisser passer pour du modèle abouti.

`ifc_set_family_mapping_file` charge une correspondance de familles sur mesure. À
faire **avant** l'étape 3, pas après. Avant une reconstruction coûteuse,
`ifc_validate_request` valide la demande sans l'exécuter.

#### Ce que la reconstruction laisse de côté

| Outil | Comportement à connaître |
|---|---|
| `ifc_rebuild_walls` | **saute** les murs à géométrie non linéaire — courbes, inclinés. Le type est choisi d'après l'épaisseur, tolérance 50 mm |
| `ifc_rebuild_floors` | extrait le profil de la **face inférieure** |
| `ifc_rebuild_openings` | cherche le mur ou le sol hôte par **recouvrement de boîte englobante** |
| `ifc_rebuild_family_instances` | même recherche d'hôte ; **sans mur hôte à moins de 600 mm, l'instance est posée sans hôte** |

Les comptes rendus de `dryRun` disent combien d'éléments sont sautés et pourquoi. Les
lire : un « 95 murs reconstruits » qui tait 25 murs courbes laisse un modèle troué.

### Exporter

| Besoin | Outils |
|---|---|
| Export simple | `ifc_export_basic` |
| Avec configuration | `ifc_list_export_configurations`, puis `ifc_get_export_configuration` et `ifc_export_with_configuration` |

### À éviter

- Reconstruire sans avoir lancé `ifc_analyze_rebuildability`.
- Reconstruire toutes les catégories en une fois, ou en parallèle.
- Reconstruire alors qu'un simple lien suffisait.
- Importer un IFC lourd sans avoir vérifié les capacités.
- Omettre `compact: true` sur les outils d'analyse : leurs réponses sont volumineuses.
- Lire un compte de reconstruction sans lire le compte d'éléments sautés.

---

## Signatures des outils

### Où regarder

La source canonique d'une signature est le code du serveur C# ; aucun schéma
généré n'est conservé. Pour la liste exhaustive des outils publiés, avec leur
nature, leur `dryRun` et leurs défauts connus, voir la section **Inventaire des outils**,
généré depuis le code par `tools/audit-tool-surface.py`.

### Catégories

| Préfixe | Catégorie | Exemples |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Lecture | `get_project_info`, `analyze_model_statistics` |
| `set_`, `batch_`, `sync_`, `create_`, `delete_`, `purge_`, `rename_`, `modify_`, `override_`, `change_` | Écriture | `set_element_parameters`, `batch_modify_parameter_values` |
| `ifc_*` | IFC | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `workflow_*` | Workflows composés | `workflow_model_audit`, `workflow_room_documentation` |
| `get_*` | Méta | Diagnostic, capacités du serveur |

### Signatures

Aucune liste n'est recopiée ici. Une l'a été, tenue à la main, et elle a cessé
d'être exacte au premier renommage : `filter_elements`, `list_family_types` et
`list_materials` y figuraient encore après avoir disparu en 0.3.0. Le README du
dépôt a supprimé sa propre section « Fonctions ajoutées » pour la même raison.

Pour la signature exacte, lire le schéma MCP publié par la connexion active. Aucun accès aux sources locales n’est nécessaire. Pour savoir quels outils existent :
la section **Inventaire des outils**.

### Contrat de réponse

Tout succès porte `execution.{connector, pluginVersion, mcpServerVersion,
revitVersion, mode, toolReadOnly, toolDestructive, writesAllowed, cached}`.
`toolReadOnly` classe l’appel et son action, pas la session ; `writesAllowed` est le verrou de
session. Les anciens noms `readOnly`/`destructive` n'existent plus, et
`serverVersion` non plus : il était lu sur le plugin, ce qui rendait invisible une
mise à jour appliquée à moitié.

Les deux moitiés sont installées séparément. Quand leurs versions diffèrent, la
réponse porte en plus `execution.versionMismatch` — et sur un outil introuvable, la
même information arrive dans `error.context`.

### Mise à jour

Les descriptions se corrigent directement sur les attributs `[McpServerTool]`,
puis `python tools/audit-tool-surface.py` régénère l'inventaire.

---

## Diagnostic

| Symptôme | À vérifier |
|---|---|
| « No RiveTT Revit session is available » | Revit 2026.5+ ou 2027 démarré, avec un projet ouvert, quelques secondes après l'ouverture |
| Une commande répond « not found » | Mise à jour à moitié appliquée — suivre « Si RiveTT dit qu'une commande n'existe pas » dans la section Installation |
| Le panneau RiveTT n'apparaît pas dans le ruban | `%APPDATA%\Autodesk\Revit\Addins\<2026\|2027>\RiveTT.addin` présent |
| Écritures refusées | Bouton *Écriture* du panneau RiveTT : chaque session démarre en lecture seule |
| Journal des appels | `%LOCALAPPDATA%\RiveTT\audit.jsonl` |

---

## Configuration de Claude et ChatGPT Desktop


Dans l'installateur RiveTT, cochez l'application que vous utilisez. Vous pouvez
cocher les deux. Les cases sont décochées au départ ; la documentation est toujours
installée. Quittez complètement votre application d'IA avant l'installation, puis
rouvrez-la après. Aucun droit administrateur n'est nécessaire.

## Configurer la connexion MCP pour Claude

L'installateur déclare la connexion Revit dans la configuration de Claude Desktop,
en conservant les autres réglages et serveurs. La connexion MCP ne dépend pas d'un
skill Claude : l'installateur ne crée ni ZIP ni skill de compte.

| Élément | Emplacement Windows |
|---|---|
| Configuration Claude classique | %APPDATA%\Claude\claude_desktop_config.json |
| Configuration Claude MSIX (si la vue classique n'existe pas) | %LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude_desktop_config.json (`Claude_*` est détecté automatiquement) |
| Script de configuration | %LOCALAPPDATA%\RiveTT\register-mcp.ps1 |
| Journal | %LOCALAPPDATA%\RiveTT\register-mcp-Claude.log |

Si Claude n'est pas détecté, l'installateur n'écrit rien : il affiche le chemin du
script et du journal. Lancez Claude une fois, fermez-le, puis relancez l'installateur.
Le guide installé dans
%LOCALAPPDATA%\RiveTT\documentation\skills_RiveTT.md reste disponible pour une
intégration de skill décidée séparément par votre organisation. Il provient de
l'unique source `src\resources\documentation\SKILL.md`, renommée seulement lors de
l'installation générale.

Après une connexion Claude réussie, la page finale rappelle d'ajouter la compétence
RiveTT dans **Personnaliser** si elle n'existe pas encore, et indique le chemin de
`skills_RiveTT.md`. Ce rappel n'affirme pas qu'elle a été activée dans Claude.

## Configurer pour ChatGPT (config + skill)

Cette option vise **l'application ChatGPT Desktop avec son moteur local**, partagé
avec Codex. Elle ne configure pas le site ChatGPT dans un navigateur.
L'installateur déclare la connexion Revit et installe le fichier personnel SKILL.md, autonome. Rouvrez ChatGPT ; si un skill a été désactivé dans vos réglages, réactivez-le.

| Élément | Emplacement Windows |
|---|---|
| Configuration ChatGPT Desktop | %USERPROFILE%\.codex\config.toml |
| Skill personnel, nouvelle installation | %USERPROFILE%\.agents\skills\rivett\SKILL.md |

Si CODEX_HOME est défini, la configuration est dans CODEX_HOME\config.toml.
Le skill est toujours copié dans `%USERPROFILE%\.agents\skills\rivett`, le dossier
personnel lu automatiquement par ChatGPT et Codex. Son nom reste `SKILL.md`.

Sur le poste de Thomas, les chemins par défaut sont :

- Claude : C:\Users\theba\AppData\Roaming\Claude\claude_desktop_config.json
- Script Claude : C:\Users\theba\AppData\Local\RiveTT\register-mcp.ps1
- Journal Claude : C:\Users\theba\AppData\Local\RiveTT\register-mcp-Claude.log
- ChatGPT : C:\Users\theba\.codex\config.toml
- Skill ChatGPT : C:\Users\theba\.agents\skills\rivett\SKILL.md

## Vérifier la connexion

La page finale indique le résultat de la configuration et les chemins du script et
du journal. Si l'application n'est pas détectée, lancez-la une première fois puis
relancez la configuration avec l'installateur. Un skill présent ne prouve pas que la
connexion Revit est configurée.

Ouvrez Revit et demandez à votre assistant de vérifier les capacités RiveTT.
Vérifiez les versions du plugin et du serveur, le document actif, puis le verrou.
Seul le bouton Écriture du ruban Revit autorise les modifications.

Les configurations existantes sont sauvegardées avec le suffixe .bak-rivett avant
modification. Les journaux de configuration sont dans %LOCALAPPDATA%\RiveTT\.
Ne modifiez pas les copies internes MSIX de Claude à la main : le helper préserve
la configuration existante et son éventuel lien avec la copie utilisée par l'application.

Sources vérifiées le 7 septembre 2026 :
[configuration MCP OpenAI](https://learn.chatgpt.com/docs/extend/mcp?surface=cli),
[skills locaux OpenAI](https://learn.chatgpt.com/docs/build-skills),
[import des skills Claude](https://support.claude.com/en/articles/12512180-use-skills-in-claude),
[configuration locale Claude](https://modelcontextprotocol.io/docs/2026-07-28/develop/connect-local-servers).


<!-- BEGIN GENERATED TOOL INVENTORY -->

## Inventaire des outils RiveTT

> Document **généré** par `tools/audit-tool-surface.py`. Ne pas éditer à la main :
> relancer le script après toute modification de la surface d'outils.

Relevé du 2026-09-08 — connecteur 0.5.3 — **200 outils publiés**, 197 classes runtime.

### Comment lire ce document

Deux surfaces sont croisées : les attributs `[McpServerTool]` du serveur MCP et les
classes `IRiveTTTool` du runtime. La question posée à chaque outil est celle qui a
coûté le plus cher jusqu'ici : **un paramètre publié est-il vraiment lu**.

| Colonne | Ce qu'elle dit |
|---|---|
| Nature | `lecture` ou `écriture` selon `[ToolSafety]`. Depuis le verrou du ruban, ce classement est une frontière de permission, plus une simple étiquette |
| dryRun | l'outil accepte une prévisualisation |
| Int. | intérêt pour une agence d'architecture : **5** geste quotidien, **4** utile régulier, **3** ponctuel, **2** marginal, **1** hors périmètre. Jugement d'usage, pas une propriété du code : il vit dans les listes `TIER5`/`TIER4`/`TIER2` du script et se corrige en les éditant |
| Défaut probable | **critique** et **majeur** vérifiés dans le code ; **signal** détecté automatiquement, avec des faux positifs quand la lecture passe par un helper partagé ou un DTO typé ; **mineur** systémique |

Une flèche `→` signale une **façade** : un nom MCP qui appelle un autre outil runtime.

### Synthèse

| Mesure | Valeur |
|---|---|
| Outils publiés | **200** |
| Dont écriture | **137** (68 %) — c'est la part que le verrou du ruban gouverne |
| Écritures sans `dryRun` | **37** sur 137 — `execution.supportsDryRun` le dit par outil, et le routeur refuse `dryRun: true` sur les autres au lieu de les exécuter |
| Défauts critiques et majeurs corrigés | **8**, gardés par `ConfirmedDefectFixSourceTests` |
| Lacunes API comblées depuis le relevé précédent | **16** sur 19 |
| Erreurs génériques `Failed: …` sans suggestion | **0** |
| Géométrie par boîte englobante | **14** |
| Classement `[ToolSafety]` en désaccord avec le nom | **14** |
| Défauts confirmés / signaux à vérifier | **0** / **12** |

### Répartition par catégorie

| Catégorie | Outils | Part |
|---|---:|---:|
| Elements | 65 | 32 % |
| Project | 49 | 24 % |
| IFC | 20 | 10 % |
| Views | 14 | 7 % |
| LinkedFiles | 10 | 5 % |
| Annotations | 9 | 4 % |
| Parameters | 8 | 4 % |
| Documents | 8 | 4 % |
| Sheets | 5 | 2 % |
| Meta | 4 | 2 % |
| Workflows | 4 | 2 % |
| Architecture | 2 | 1 % |
| Code | 1 | 0 % |
| Interop | 1 | 0 % |

Le ferraillage et la charpente métallique — 112 outils, 38 % de la surface — ont été
retirés du dépôt, pas filtrés. Ce qui reste est le catalogue que l'agent lit à chaque
session : 200 outils dont 68 % d'écriture, tous dans le périmètre logement,
équipement, tertiaire et santé.

### Défauts corrigés

Les huit défauts critiques et majeurs du relevé précédent. Ils restent listés :
un inventaire qui oublie ce qui a cassé une fois laisse le même défaut revenir sans
que personne le reconnaisse. `ConfirmedDefectFixSourceTests` échoue si l'un revient.

| Outil | Gravité | Ce que le code faisait | Ce qu'il fait maintenant |
|---|---|---|---|
| `batch_create_sheets` | critique | fenêtres placées à (0,5 ft ; 0,5 ft) en dur, alors que l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le cartouche A1 français. | Le cadre est mesuré sur l'instance de cartouche via `SheetFrame`, partagé avec `place_viewport` ; plusieurs vues sont pavées au lieu d'être empilées. |
| `workflow_sheet_set` | critique | `viewIds` était publié dans la spec et jamais lu : les feuilles sortaient vides, sans signalement. | Les `viewIds` sont lus et placés ; la réponse réconcilie `requestedViewCount` et `placedViewCount`. Outil retiré depuis (chantier de consolidation 27/08) : ce comportement vit maintenant dans `batch_create_sheets`. |
| `delete_material` | majeur | destructif sans dryRun. | `dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle. |
| `delete_schedule` | majeur | destructif sans dryRun. | `dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle. |
| `delete_selection` | majeur | destructif sans dryRun, alors que `delete_element` en a un par défaut. | `dryRun` par défaut via `DeletionPreview` ; la réponse précise que seule la liste enregistrée est supprimée, pas les éléments. Fusionné dans `manage_selection` (action=delete) le 27/08, avec save_selection et load_selection — même comportement. |
| `ifc_set_family_mapping_file` | majeur | classé lecture seule alors qu'il modifie un réglage d'export persistant : il traversait le verrou d'écriture du ruban. | Reclassé `[ToolSafety(false, false)]` : il passe désormais par le verrou. |
| `send_code_to_revit` | majeur | aucun dryRun sur l'outil le plus puissant, et la description annonçait une confirmation dans Revit qui n'existe pas. | `dryRun` par défaut : la sandbox est vérifiée, rien n'est exécuté ni écrit sur disque. La description ne promet plus de dialogue. |
| `workflow_clash_review` | majeur | détection par boîtes englobantes alors que `detect_clashes` utilise l'intersection solide : l'outil composé rendait plus de faux positifs que le simple. | Les deux outils appellent la même passe `ClashFinder` (pré-filtre par boîtes, puis `ElementIntersectsElementFilter`). Gardé distinct de `detect_clashes` lors du chantier de consolidation du 27/08 : il crée une vue (écriture), l'autre reste lecture seule — les fusionner aurait cassé le verrou d'écriture. Renommé `show_clashes` le 27/08 (convention `show_`, cohérent avec `show_cross_model_elements`). |

### Défauts confirmés

Aucun défaut critique ou majeur ouvert.


#### Arbitrage ouvert

`workflow_data_roundtrip` est classé lecture seule mais écrit un fichier .xlsx.
Le modèle n'est pas touché, mais le verrou du ruban ne l'arrête pas : la politique
de cet export reste à décider.

### Signaux à vérifier

Détection automatique. Un signal n'est pas un défaut : la lecture passe peut-être
par un helper partagé ou un DTO typé, ou la clé annoncée n'est qu'un exemple de
documentation.

| Outil | Signal |
|---|---|
| `batch_rename_affix` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `color_elements` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `create_wall` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : baseLevelId, baseOffset |
| `detach_wall_constraint` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `duplicate_family_type` | clé imbriquée annoncée, absente du runtime : paramName |
| `duplicate_storey` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `filter_by_parameter_value` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `open_document` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : filePath |
| `sync_csv_parameters` | clé imbriquée annoncée, absente du runtime : paramName1 |
| `sync_navisworks_selection` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |
| `tag_rooms` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |

### Inventaire complet

#### Elements — 65 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_wall` → `create_line_based_element` | écriture | oui | 5 | Create one native Revit wall. wallTypeId and baseLevelId are required. Set topLevelId to constrain the wall to a level; topOffset is in mm and may be… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : baseLevelId, baseOffset |
| `filter_by_parameter_value` | lecture | — | 5 | Filter elements by one parameter condition, or several combined with AND/OR via the conditions array. Conditions: equals, not_equals, contains, not_co… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `copy_elements` | écriture | — | 5 | Copy elements with optional mm offset. Can target a different view (sourceViewId+targetViewId) or another OPEN document (targetDocumentTitle). | **mineur** — pas de dryRun |
| `create_door` → `create_point_based_element` | écriture | oui | 5 | Place a door family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z… | **mineur** — géométrie par boîte englobante |
| `create_window` → `create_point_based_element` | écriture | oui | 5 | Place a window family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give… | **mineur** — géométrie par boîte englobante |
| `filter_elements` | lecture | — | 5 | Paginated element query by category, level, group status or wall constraint status. Returns totalCount, returnedCount, appliedLimit and nextCursor. re… | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; géométrie par boîte englobante |
| `manage_area_plans` | écriture | — | 5 | Builds regulatory area surfaces (SHAB/SU/SDP): area schemes, area plan views, area boundary lines, and Area elements. action=list_schemes\|duplicate_sc… | **mineur** — pas de dryRun |
| `batch_rename` | écriture destructif | oui | 5 | Batch rename elements or system types in the Revit project. Supports both loadable-family elements and system types (wall/floor/ceiling/roof types). | — |
| `create_floor` | écriture | oui | 5 | Create an architectural floor from a boundary (or a room), optionally with holes. Provide boundaryPoints OR roomId. Previews by default: the dry run r… | — |
| `create_grid` | écriture destructif | oui | 5 | Create a grid system (X and/or Y grids by count + spacing), or rename/delete an existing grid. action=create\|rename\|delete. Spacing/extent values are… | — |
| `create_level` | écriture destructif | oui | 5 | Create, edit, rename, or delete a level. action=create\|set\|rename\|delete. For set/rename/delete identify the level by levelId or name. | — |
| `create_room` | écriture | oui | 5 | Create a room at a point on a level. x/y are plan coordinates in mm; the level sets the elevation. A point that is not inside a closed loop of room-bo… | — |
| `create_room_separation_line` | écriture | oui | 5 | Draw room separation lines in a plan view to split or bound a room without building a physical wall. path is a JSON array [{x,y,z}, ...] in mm. This i… | — |
| `create_stair` | écriture | oui | 5 | Create a native component stair between two levels. runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the levels drive the ele… | — |
| `delete_element` | écriture destructif | oui | 5 | Delete elements. The dryRun preview reports the real cascade (dependent tags, sketches, railings...) and any group membership. Deleting a group MEMBER… | — |
| `edit_group_members` | écriture destructif | oui | 5 | Add or remove members of a model group. The Revit API cannot edit group members in place, so this ungroups the instance, changes the member set and cr… | — |
| `export_elements_data` | lecture | — | 5 | Export element data as JSON or CSV, by category and/or by explicit elementIds. Parameter names may be given in English or in the document language (Ma… | — |
| `export_room_data` | lecture | — | 5 | Export room data (area in m2, perimeter, level, department). Filter inside Revit with levelName/levelId and nameFilter instead of returning every room… | — |
| `export_to_excel` | lecture | — | 5 | Export element data from a Revit category to an Excel file. | — |
| `get_current_view_elements` | lecture | — | 5 | List elements visible in the currently active view. categoryFilter is a single-category shortcut (OST code, English name or localized label); modelCat… | — |
| `get_element_parameters` | lecture | — | 5 | Get parameters of elements by Revit element ID. Numeric values come back in PROJECT display units with an explicit unit plus the Revit internal value… | — |
| `get_linked_elements` | lecture | — | 5 | Query elements from linked Revit models with optional filtering. parameterNames is additive — without it only basic fields are returned. | — |
| `get_selected_elements` | lecture | — | 5 | Get currently selected elements in Revit. | — |
| `import_from_excel` | écriture destructif | oui | 5 | Import parameter values from an Excel file into Revit elements. | — |
| `manage_model_groups` | écriture destructif | oui | 5 | Inventory model groups, duplicate a group type and optionally swap selected instances, or ungroup selected model groups. Write actions preview by defa… | — |
| `modify_element` | écriture | oui | 5 | Move, rotate, mirror, or copy elements. Vectors are {"x":mm,"y":mm,"z":mm} JSON objects. move needs translation; rotate needs rotationCenter + rotatio… | — |
| `renumber_elements` | écriture destructif | oui | 5 | Renumber rooms/doors/windows by location or name. Writes into the specified parameter; supports prefix/suffix and start/increment. | — |
| `set_element_parameters` | écriture destructif | oui | 5 | Set parameter values on one or more elements. Pass requests as a JSON-encoded array string. Supports parameterName by display name and builtInParamete… | — |
| `color_elements` | écriture | oui | 4 | Color a view's elements of a category by grouping them on a parameter value, or reset (clear) those color overrides. action=color\|reset. Pass viewId t… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `duplicate_family_type` | écriture | — | 4 | Duplicate a loadable family type with a new name and optional parameter overrides. | **signal** — clé imbriquée annoncée, absente du runtime : paramName |
| `add_curtain_grid_line` | écriture | — | 4 | Adds a grid line to an existing curtain wall/system's grid (create the wall itself with create_line_based_element and a curtain wall type). hostElemen… | **mineur** — pas de dryRun |
| `add_curtain_mullions` | écriture | — | 4 | Adds mullions to an existing curtain wall/system's grid lines. hostElementId and mullionTypeId are required; applies to every ungridded segment unless… | **mineur** — pas de dryRun |
| `capture_selection` | lecture | — | 4 | Capture explicit element IDs or the current Revit selection as a reusable temporary token. Tokens expire and are scoped to the active document session… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `create_array` | écriture | — | 4 | Create a linear or radial array. Default builds a real associative Revit ArrayElement (editable count); set associative=false for loose copies. linear… | **mineur** — pas de dryRun |
| `create_opening` | écriture | — | 4 | Cuts an opening or a vertical shaft. openingType=shaft\|host\|wall. shaft: baseLevelId+topLevelId+curves (closed loop, mm) — a vertical shaft through ev… | **mineur** — pas de dryRun |
| `create_point_based_element` | écriture | oui | 4 | Create point-based elements. Pass [{category, locationPoint:{x,y,z}, typeId?, levelId?, baseLevel?, hostWallId?, facingFlipped?, handFlipped?, rotatio… | **mineur** — géométrie par boîte englobante |
| `create_structural_framing_system` | écriture | — | 4 | Create a beam system on a level over a rectangular area. Default builds a real associative Revit BeamSystem (editable layout); set associative=false f… | **mineur** — pas de dryRun |
| `create_surface_based_element` | écriture | — | 4 | Create surface-based elements: floors, ceilings, or roofs (OST_Floors, OST_Ceilings, OST_Roofs — a roof is a real FootPrintRoof, Document.Create.NewFo… | **mineur** — pas de dryRun |
| `create_toposolid` | écriture | — | 4 | Creates a Toposolid (site/ground surface) from a closed boundary loop (Toposolid.Create). toposolidTypeId and levelId are required — list types with l… | **mineur** — pas de dryRun |
| `load_family` | écriture | — | 4 | Load a family into the Revit project, or reload one already there (e.g. after editing its .rfa outside Revit). Also lists loaded families or duplicate… | **mineur** — pas de dryRun |
| `manage_view_display` | écriture | — | 4 | Select, highlight, isolate, hide, or zoom to elements in the active view. Actions: select, selectionbox, setcolor, settransparency, hide, temphide, is… | **mineur** — pas de dryRun |
| `measure_between_elements` | lecture | — | 4 | Measure distance between two elements or two points in mm. Provide either elementId1/elementId2, or point1/point2 (as JSON arrays [x,y,z]). | **mineur** — géométrie par boîte englobante |
| `change_element_type` | écriture destructif | oui | 4 | Change the type of one or more elements to a target type specified by ID or name. | — |
| `create_detail_line` | écriture | oui | 4 | Draw 2D detail lines in a view (view-owned, not visible in other views). path is a JSON array [{x,y,z}, ...] in mm; consecutive points become segments… | — |
| `create_filled_region` | écriture | oui | 4 | Create a filled region in a view from a closed boundary, optionally with holes (inner loops). | — |
| `create_line_based_element` | écriture | oui | 4 | Create line-based elements (walls, beams). Pass a JSON array of specs: [{category, locationLine:{p0:{x,y,z}, p1:{x,y,z}, pMid?:{x,y,z}}, typeId?, heig… | — |
| `create_model_line` | écriture | oui | 4 | Draw 3D model lines on a horizontal sketch plane. path is a JSON array [{x,y,z}, ...] in mm; all points must share the same z, which sets the plane el… | — |
| `create_ramp` | écriture | oui | 4 | Create a native component ramp between two levels (accessibility/PMR). runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the l… | — |
| `export_families` | lecture | — | 4 | Export loaded families as .rfa files into a target directory. | — |
| `find_undimensioned_elements` | lecture | — | 4 | Find elements not referenced by dimensions | — |
| `find_untagged_elements` | lecture | — | 4 | Find elements without tags in a view | — |
| `get_curtain_grid_info` | lecture | — | 4 | Reads an existing curtain wall/system grid: U/V grid line ids, panel ids, mullion ids. hostElementId is the curtain wall or curtain system element. | — |
| `get_element_solid_geometry` | lecture | — | 4 | Get an element's REAL solid geometry (bounding box, centroid, volume m3, face/edge counts AND inferred cross-section shape: circular/rectangular/compl… | — |
| `get_elements_in_spatial_volume` | lecture | — | 4 | Find elements within a 3D bounding box or room volume. volumeType=room uses volumeIds; volumeType=custom uses customMinX..customMaxZ. | — |
| `get_room_openings` | lecture | — | 4 | Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName. | — |
| `manage_selection` | écriture destructif | oui | 4 | CRUD on named saved selections (SelectionFilterElement). action=save\|load\|list\|delete. name is required for save/load/delete (ignored for list). save:… | — |
| `match_element_properties` | écriture destructif | oui | 4 | Copy parameter values from one source element to one or more target elements. | — |
| `set_element_phase` | écriture | oui | 4 | Assign created/demolished phase to elements. Pass a JSON array of requests: [{elementId, createdPhaseId?, demolishedPhaseId?}]. The older names phaseC… | — |
| `set_element_workset` | écriture | oui | 4 | Move elements to a different workset. Pass a JSON array of requests: [{elementId, worksetName}]. Worksets are resolved by name only. | — |
| `set_material_properties` | écriture destructif | oui | 4 | Set identity, appearance, product info, and asset assignments on Revit materials. Each request is a FLAT object keyed by materialId plus any of: name,… | — |
| `edit_family` | écriture destructif | oui | 3 | Edits a loaded family's type parameters in the background - no window opens. Pass familyId or familyName, and changes as JSON: [{typeName, parameters:… | — |
| `rename_families` | écriture destructif | oui | 3 | Rename loaded families (and optionally their types) with find/replace, prefix, or suffix operations. | — |
| `detach_wall_constraint` | écriture destructif | oui | 2 | Preview or detach wall top-level constraints or Revit 2027 top/base attachments. Grouped walls are reported and skipped instead of rolling back unrela… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `create_assembly` | écriture | — | 2 | Groups elements into an AssemblyInstance (prefabrication/shop drawings), or splits them into Parts (demolition/phasing sequencing). action=create_asse… | **mineur** — pas de dryRun |
| `get_elements_by_unique_id` | lecture | — | 2 | Resolve Revit UniqueId strings to ElementId records for cross-app workflows. | — |

#### Project — 49 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `duplicate_storey` | écriture destructif | oui | 5 | Preview or transactionally duplicate model elements from one level to a target elevation. Reports view-specific, grouped, and constrained dependencies… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `batch_export` | écriture | — | 5 | Export views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats. This writes files and requires the RiveTT ribbon write lock to be open. | **mineur** — pas de dryRun |
| `create_revision` | écriture | — | 5 | List, create, update, or assign revisions to sheets, and draw revision clouds. action=list\|create\|set\|add_to_sheets\|create_cloud. 'set' updates an exi… | **mineur** — pas de dryRun |
| `export_schedule` | écriture | — | 5 | Export a schedule as JSON, or write it to a CSV/TSV file. Without exportPath the data comes back inline; with exportPath the file is written using del… | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `check_model_health` | lecture | — | 5 | Run a model health check and return a health score. | — |
| `create_schedule` | écriture | oui | 5 | Create a new schedule view in Revit. | — |
| `create_sheet` | écriture | oui | 5 | Create a sheet, with a title block. Pass titleBlockId (an OST_TitleBlocks family type id, from list_system_types or list_family_types) or a family/typ… | — |
| `get_current_view_info` | lecture | — | 5 | Get information about the currently active view in Revit. | — |
| `get_project_info` | lecture | — | 5 | Get project name, address, levels, phases, worksets, and links from the active Revit document. | — |
| `get_schedule_data` | lecture | — | 5 | Export schedule data as JSON from an existing schedule view. availableFields is omitted unless includeAvailableFields=true: it lists every schedulable… | — |
| `list_family_types` | lecture | — | 5 | List available family types in the Revit project. | — |
| `list_materials` | lecture | — | 5 | List materials in the active Revit document. nameFilter and materialClass narrow the list inside Revit - a real project carries 200+ materials. | — |
| `list_schedulable_fields` | lecture | — | 5 | Discover available schedulable fields for a category. | — |
| `list_system_types` | lecture | — | 5 | List the system types of a category: walls, floors, ceilings, roofs, railings, stairs, ramps, viewports, text, dimensions. To include title blocks and… | — |
| `list_warnings` | lecture | — | 5 | Get model warnings from the active Revit document. | — |
| `list_worksets` | lecture | — | 5 | List all worksets in the active Revit document. | — |
| `manage_links` | écriture destructif | oui | 5 | List, reload, reload-from-path, unload, or remove linked files. To add a NEW link use add_linked_file instead. | — |
| `place_title_block` | écriture | oui | 5 | Place a title block instance on an existing sheet. Use it to repair a sheet that has no frame. Call it without titleBlockId to get the list of title b… | — |
| `purge_unused` | écriture destructif | oui | 5 | Purge unused families/types and materials, and optionally unreferenced view templates and view filters, from the project. | — |
| `synchronize_with_central` | écriture destructif | oui | 5 | Synchronizes the local model with the workshared central file. AFFECTS THE WHOLE TEAM, not just this session, and cannot be undone from here. Requires… | — |
| `create_key_schedule` | écriture | — | 4 | Creates a key schedule (ViewSchedule.CreateKeySchedule) — a reusable finish/typology key table (room finish keys, dwelling-unit typologies), different… | **mineur** — pas de dryRun |
| `create_material` | écriture | — | 4 | Create a new material in the Revit project. | **mineur** — pas de dryRun |
| `duplicate_material` | écriture | — | 4 | Duplicate an existing material with a new name. | **mineur** — pas de dryRun |
| `manage_additional_settings` | écriture | — | 4 | Manage Additional Settings (Manage tab): line styles, line weights, line patterns, fill patterns, halftone/underlay. | **mineur** — pas de dryRun |
| `manage_phase_filters` | écriture | — | 4 | List, set, or create Revit Phase Filters. Actions: list \| set \| create. The 'set' action changes one presentation (New \| Demolished \| Existing \| Tempo… | **mineur** — pas de dryRun |
| `manage_sheet_sets` | écriture | — | 4 | List, create, or delete named view/sheet sets (ViewSheetSet), so batch_export/printing can reuse a saved list instead of one passed on every call. act… | **mineur** — pas de dryRun |
| `analyze_model_statistics` | lecture | — | 4 | Analyze element counts by category in the active Revit document. | — |
| `audit_families` | lecture | — | 4 | Audit families in the Revit project. Lists loadable (.rfa) families by default; set includeSystemFamilies=true to also list system-family types (wall/… | — |
| `clean_cad_links` | écriture destructif | oui | 4 | Analyze and clean up imported/linked CAD files. action=list\|delete. | — |
| `count_lines_per_view` | lecture | — | 4 | Count detail lines per view (single document pass, safe on any model size) plus a project-wide model line count. Model lines have no owner view, so th… | — |
| `create_preset_schedule` | écriture | oui | 4 | Create a schedule from a predefined template. preset = door_by_room \| window_by_room \| room_finish \| material_takeoff \| sheet_list \| view_list. materi… | — |
| `delete_material` | écriture destructif | oui | 4 | Delete a material from the project by ID or name. Previews by default: the dry run names the material and reports the deletion cascade. Set dryRun=fal… | — |
| `delete_schedule` | écriture destructif | oui | 4 | Delete a schedule by ID or name. Previews by default: the dry run names the schedule and reports the cascade, including the viewports that placed it o… | — |
| `detect_clashes` | lecture | — | 4 | Detect clashes between two element categories. Uses true solid-geometry intersection by default (fewer false positives than bounding boxes). | — |
| `duplicate_schedule` | écriture | oui | 4 | Duplicate a schedule with a new name | — |
| `duplicate_system_type` | écriture destructif | oui | 4 | Duplicate, rename, or delete a system type (wall, floor, roof, ceiling). action=duplicate\|rename\|delete. | — |
| `get_compound_structure` | lecture | — | 4 | Get wall/floor/roof/ceiling layer structure by type ID or name. | — |
| `get_material_quantities` | lecture | — | 4 | Calculate material area and volume across elements, optionally filtered by category or restricted to the current selection. | — |
| `list_design_options` | lecture | — | 4 | Lists existing design option sets and their options, and (with elementId) reports which option an element belongs to. Creating a design option set/opt… | — |
| `list_phases` | lecture | — | 4 | List all project phases in the active Revit document. | — |
| `list_shared_parameters` | lecture | — | 4 | List all project parameters with their bindings and categories, optionally filtered by category. | — |
| `manage_project_units` | écriture | oui | 4 | Get or set project units (length, area, volume, angle, etc.). Actions: get, set, list_valid_units. | — |
| `manage_worksets` | écriture destructif | oui | 4 | Create, rename, delete, or set the active workset (workshared models only). To LIST worksets use list_worksets. | — |
| `modify_schedule` | écriture destructif | oui | 4 | Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter,… | — |
| `set_compound_structure` | écriture destructif | oui | 4 | Modify compound structure on a wall/floor/roof/ceiling type. action=replace\|add\|remove\|modify\|set_wrapping. set_wrapping sets openingWrapping (none\|ex… | — |
| `set_project_info` | écriture | oui | 4 | Set editable Project Information fields. Only the fields you pass are changed; others are left untouched. | — |
| `export_shared_parameter_file` | lecture | — | 3 | Export shared parameter file contents | — |
| `get_material_properties` | lecture | — | 3 | Get detailed material properties (physical, thermal, appearance) by material ID or name. | — |
| `list_family_sizes` | lecture | — | 2 | List loaded families with type/instance counts and, when includeSize=true, the family file size in KB measured by exporting each family to a temp file… | — |

#### IFC — 20 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `ifc_export_basic` | écriture | — | 4 | Export the active document to IFC. First-class flags cover the common options; use overrides for any other IFCExportOptions key. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_link` | écriture | — | 4 | Link an IFC file into the active document (creates a .ifc.RVT sidecar file managed by Revit). | **mineur** — pas de dryRun |
| `ifc_compare_original_vs_rebuilt` | lecture | — | 3 | Compare volume/geometry between the original DirectShape and its native rebuild. | **mineur** — géométrie par boîte englobante |
| `ifc_export_with_configuration` | écriture | — | 3 | Export using a named configuration (built-in or custom) with optional key/value overrides. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_rebuild_family_instances` | écriture | oui | 3 | Place family instances (doors, windows, furniture) from IFC DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_rebuild_openings` | écriture | oui | 3 | Cut openings in rebuilt walls/floors based on IFC opening DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_set_family_mapping_file` | écriture | — | 3 | Set the family mapping file used by subsequent IFC exports. | **mineur** — pas de dryRun |
| `ifc_analyze_rebuildability` | lecture | — | 3 | Analyze IFC DirectShapes and score feasibility of rebuilding them as native Revit elements. | — |
| `ifc_get_capabilities` | lecture | — | 3 | Detect IFC version support and revit-ifc add-in presence | — |
| `ifc_get_export_configuration` | lecture | — | 3 | Get full details of a specific export configuration by name. | — |
| `ifc_list_export_configurations` | lecture | — | 3 | List available built-in export configurations | — |
| `ifc_list_rebuild_candidates` | lecture | — | 3 | List elements above a rebuild confidence threshold. | — |
| `ifc_open_or_import` | écriture destructif | oui | 3 | Open/import an IFC into a background Revit document with advanced options. Use open_file for opening AND activating an IFC with the write lock closed.… | — |
| `ifc_rebuild_floors` | écriture | oui | 3 | Rebuild native floors from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_roofs` | écriture | oui | 3 | Rebuild native roofs from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_structural_members` | écriture | oui | 3 | Rebuild columns and beams from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_walls` | écriture | oui | 3 | Rebuild native walls from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_reload_link` | écriture destructif | oui | 3 | Reload an existing IFC link, optionally from a new file. | — |
| `ifc_tag_unreconstructable_elements` | écriture destructif | oui | 3 | Tag IFC DirectShapes that cannot be rebuilt by writing a marker parameter. | — |
| `ifc_validate_request` | lecture | — | 3 | Validate IFC file path, extension, and schema version. | — |

#### Views — 14 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `apply_view_template` | écriture | — | 5 | List, apply, or remove view templates from views. action=list\|apply\|remove. | **mineur** — pas de dryRun |
| `create_view_filter` | écriture | — | 5 | Create, apply, or list parameter-based view filters. action=create\|apply\|list. A filter carries one rule (parameterName/filterRule/filterValue) or sev… | **mineur** — pas de dryRun |
| `create_view` | écriture | oui | 5 | Create a new view in Revit: floor plan, ceiling plan, section, elevation, drafting, callout, or 3D view. | — |
| `duplicate_view` | écriture | oui | 5 | Duplicate an existing view in Revit. | — |
| `manage_view_templates` | écriture destructif | oui | 5 | List, duplicate, delete, or rename view templates. action=list\|duplicate\|delete\|rename. | — |
| `override_graphics` | écriture | oui | 5 | Override element graphics in a view (colors, transparency, halftone, line weight). | — |
| `place_viewport` | écriture | oui | 5 | Place a view on a sheet as a viewport. positionX/positionY are the viewport CENTRE in mm in sheet coordinates; omit both to centre it on the sheet. Th… | — |
| `create_section_box_from_selection` | écriture | oui | 4 | Create a 3D section box from selected elements | **mineur** — géométrie par boîte englobante |
| `create_views_from_rooms` | écriture | oui | 4 | Create callout, section, or elevation views from rooms with a naming pattern. | **mineur** — géométrie par boîte englobante |
| `manage_scope_boxes` | écriture | — | 4 | Inventory, rename, move, or assign-to-views existing scope boxes (OST_VolumeOfInterest). The Revit API has no method to create one from scratch — draw… | **mineur** — pas de dryRun ; géométrie par boîte englobante |
| `batch_modify_view_range` | écriture | oui | 4 | Modify view range offsets (top, cut plane, bottom, view depth) for multiple views. Offsets are in mm. | — |
| `manage_unplaced_views` | écriture destructif | oui | 4 | List or delete views that are not placed on any sheet | — |
| `activate_view` | lecture | oui | 3 | Make a view or sheet the active view in the current Revit document. Available while RiveTT is locked; no transaction or model edit. Returns the actual… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `rename_views` | écriture destructif | oui | 3 | Batch rename views using find/replace, prefix, or suffix operations. | — |

#### LinkedFiles — 10 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `add_linked_file` | écriture | — | 5 | Adds a new Revit linked file from a file path and optionally places an instance at the given position. | **mineur** — pas de dryRun |
| `get_link_transform` | lecture | — | 4 | Returns the full transform of a linked file instance. | — |
| `list_linked_file_instances` | lecture | — | 4 | Lists all linked Revit files grouped by type, with transforms and load status. | — |
| `highlight_linked_element` | écriture | — | 2 | Highlights an element inside a linked model with an optional section box. | **mineur** — pas de dryRun ; géométrie par boîte englobante |
| `show_cross_model_elements` | écriture | — | 2 | Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc… | **mineur** — pas de dryRun |
| `align_link_to_host` | écriture | oui | 2 | Aligns a link instance to the host project's internal origin, shared coordinates, or project base point. | — |
| `get_selected_linked_elements` | lecture | — | 2 | Returns info about currently selected link instances. | — |
| `list_coordination_models` | lecture | — | 2 | Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances. | — |
| `move_link_instance` | écriture | oui | 2 | Moves a linked file instance. mode=delta applies (x,y,z) as an offset; mode=absolute places the origin at (x,y,z). Values are in mm. | — |
| `pin_unpin_link_instance` | écriture | oui | 2 | Pins or unpins linked file instances. | — |

#### Annotations — 9 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `tag_rooms` | écriture | oui | 5 | Tag rooms in a view. Pass viewId to target a specific view; without it the active view is used. Nothing in this surface can activate a view, so viewId… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `create_dimensions` | écriture | oui | 5 | Create dimension annotations in a view. Pass a JSON array of dimension specs. Element mode: [{viewId, elementIds:[...], linePoint:{x,y,z}, dimensionSt… | — |
| `create_text_note` | écriture | oui | 5 | Create text notes in a view. Pass a JSON array: [{text, position:{x,y,z}, viewId?, textNoteTypeId?, width?, horizontalAlignment?, verticalAlignment?,… | — |
| `import_table` | écriture | — | 4 | Import a CSV/TSV file as a formatted table in a drafting or legend view. | **mineur** — pas de dryRun |
| `manage_images` | écriture | — | 4 | Imports a raster/PDF file as an image and places it in a view (survey scan, surveyor underlay). action=list\|place. place needs filePath (bmp/jpg/jpeg/… | **mineur** — pas de dryRun |
| `create_color_legend` | écriture | oui | 4 | Color elements by parameter value and optionally create a legend view. | — |
| `create_spot_dimension` | écriture | oui | 4 | Create a spot elevation annotation (a level/coordinate callout) at a point on an element's geometry. create_dimensions only builds linear dimensions;… | — |
| `delete_empty_tags` | écriture destructif | oui | 4 | Find and remove empty or orphaned tags | — |
| `tag_walls` | écriture | oui | 4 | Tag walls at their midpoints in the active view. Operates on the active view only. Tags all walls by default, or a subset via wallIds. | — |

#### Parameters — 8 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `batch_modify_parameter_values` | écriture destructif | oui | 5 | Bulk modify parameter values across elements by category. Supports set, find-and-replace, and other operations. | — |
| `manage_project_parameters` | écriture destructif | oui | 5 | Manage project parameters. Actions: list \| create \| delete \| modify \| set_group \| set_binding_type \| rename. 'delete' now correctly removes non-shared… | — |
| `batch_rename_affix` | écriture destructif | oui | 4 | Add a prefix and/or suffix to parameter values across the model or a selection. Runs as a dry-run preview by default; set dryRun=false to apply the ch… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | écriture destructif | oui | 4 | Clear parameter values on elements by category or scope | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `add_shared_parameter` | écriture | — | 4 | Add a shared parameter to project categories. The data type of a newly created definition is honored (a typed shared parameter, not always Text). | **mineur** — pas de dryRun |
| `manage_global_parameters` | écriture destructif | oui | 4 | Manage global parameters (project-level named values). Actions: list \| get \| create \| set \| delete \| rename \| set_formula \| move_up \| move_down \| sort… | — |
| `transfer_parameters` | écriture destructif | oui | 4 | Copy parameter values from source element to one or more target elements. | — |
| `sync_csv_parameters` | écriture destructif | oui | 2 | Synchronize parameter values from CSV data into Revit elements. | **signal** — clé imbriquée annoncée, absente du runtime : paramName1 |

#### Documents — 8 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `open_document` | lecture | oui | 5 | Compatibility alias for open_file: open RVT, RFA, RTE, RFT or IFC and make it the ACTIVE document in Revit, even while RiveTT is locked. Every later t… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : filePath |
| `create_document` | écriture | oui | 5 | Create a NEW EMPTY project from a Revit template (.rte) and save it to targetPath. This is the real 'new project': save_as_document duplicates the ope… | — |
| `save_as_document` | écriture | oui | 5 | Save the active Revit project to an absolute .rvt project path or .rfa family path (parameter name: targetPath). This DUPLICATES the open document - i… | — |
| `save_document` | écriture | oui | 5 | Save the active Revit project at its current path. dryRun reports the path, the unsaved-changes state and any predictable blocker without writing. | — |
| `open_family` | lecture | oui | 3 | Opens a .rfa family file and makes it the active document in Revit, for visual editing (type parameters, geometry). The active document CHANGES - ever… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `open_file` | lecture | oui | 3 | Open and activate a file entirely in Revit, available while the RiveTT write lock is closed. RVT/RFA/RTE open directly. RFT creates a new family; IFC… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `open_template` | lecture | oui | 3 | Opens a .rte template file and makes it the active document in Revit, to edit the TEMPLATE itself (levels, types, view templates). To start a new PROJ… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `close_document` | écriture destructif | oui | 3 | Closes an open document (project, family, or template). Defaults to the active document; pass filePath to close a different one open in the background… | — |

#### Sheets — 5 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `batch_create_sheets` | écriture | oui | 5 | Create multiple sheets with title blocks and optional view placement. sheets is a JSON array: [{number, name, titleBlockName?, viewIds?}]. Each sheet'… | — |
| `align_viewports` | écriture | oui | 4 | Align viewports across sheets. 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same region lin… | — |
| `create_placeholder_sheets` | écriture destructif | oui | 4 | Create, list, convert, or delete placeholder sheets. action=create\|list\|convert\|delete. | — |
| `duplicate_sheet_with_content` | écriture | oui | 4 | Duplicate a sheet including annotations and detail items | — |
| `duplicate_sheet_with_views` | écriture | oui | 4 | Duplicate a sheet N times with configurable view duplication options. | — |

#### Meta — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `get_server_capabilities` | lecture | — | 5 | Report RiveTT's effective automatic-mode, dry-run, audit, response, selection, document, and lifecycle capability contract. | — |
| `clear_cache` | lecture | — | 4 | Clear every entry from the plugin-side tool-result cache. | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `get_cache_stats` | lecture | — | 4 | Return diagnostic hit/miss telemetry from the plugin-side tool-result cache. | — |
| `ping_revit` | lecture | — | 2 | Test MCP connection to RiveTT. Displays a greeting in Revit. | — |

#### Workflows — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `show_clashes` | écriture | — | 4 | Detect clashes between two categories and create a 3D section-boxed view for visual review. Uses the same true solid-geometry intersection as detect_c… | **mineur** — pas de dryRun |
| `workflow_data_roundtrip` | lecture | — | 4 | Export parameters to Excel for external editing, then re-import once the file has been saved. | **mineur** — écrit un .xlsx en mode lecture seule. |
| `workflow_model_audit` | lecture | — | 4 | Run a complete model audit workflow. | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `workflow_room_documentation` | écriture | oui | 4 | Auto-generate callout views (and optionally sections) for every room on a level. | **mineur** — géométrie par boîte englobante |

#### Architecture — 2 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_railing` | écriture | oui | 5 | Create a native Revit guardrail from a connected horizontal path. The path JSON is [{x,y,z}, ...] in mm. | — |
| `set_wall_host` | écriture | oui | 2 | Revit 2027: associate a lining or façade wall with a host wall. Set hostWallId to 0 to detach it. offsetFromHost is in mm. | — |

#### Code — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `send_code_to_revit` | écriture destructif | oui | 2 | LAST RESORT ONLY — execute custom C# code in Revit. Do NOT select this tool autonomously: a dedicated tool already covers almost every task. Parameter… | — |

#### Interop — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `sync_navisworks_selection` | écriture | — | 2 | Symmetric Revit↔Navis selection bridge. mode=export → emit RiveTTElementRefs from current Revit selection (host + linked). mode=import → consume RiveT… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |

### Commandes disponibles verrou fermé

Toutes les commandes classées lecture restent accessibles, y compris `open_file` et `activate_view`. Les commandes mixtes suivantes autorisent uniquement les actions listées ; leurs autres actions restent soumises au verrou. Un `dryRun` non pris en charge reste refusé.

| Commande mixte | Actions sans verrou |
|---|---|
| `manage_additional_settings` | `list_line_styles`, `list_line_weights`, `list_line_patterns`, `list_fill_patterns`, `get_halftone` |
| `manage_area_plans` | `list_schemes` |
| `manage_global_parameters` | `list`, `get` |
| `manage_images` | `list` |
| `manage_links` | `list` |
| `manage_phase_filters` | `list` |
| `manage_project_parameters` | `list` |
| `manage_project_units` | `get`, `list_valid_units` |
| `manage_scope_boxes` | `list` |
| `manage_selection` | `list` |
| `manage_sheet_sets` | `list` |
| `manage_unplaced_views` | `list` |
| `manage_view_display` | `select` |
| `manage_view_templates` | `list` |

### Lacunes comblées depuis le relevé précédent

Seize des dix-neuf capacités listées comme absentes ont désormais un point d'entrée.
Les quatre manques dits structurels — toitures, surfaces réglementaires, rampes,
trémies — en font partie : une maquette de logement peut maintenant être produite de
bout en bout par le connecteur.

| Capacité | API utilisée | Outil |
|---|---|---|
| Assemblages et pièces | `AssemblyInstance, PartUtils` | `create_assembly` |
| Cotes de niveau | `SpotDimension.Create` | `create_spot_dimension` |
| Images et fonds de plan | `ImageType, ImageInstance` | `manage_images` |
| Jeux de feuilles | `ViewSheetSet` | `manage_sheet_sets` |
| Murs-rideaux | `CurtainGrid, Mullion` | `get_curtain_grid_info, add_curtain_grid_line, add_curtain_mullions` |
| Nomenclatures de clés | `ViewSchedule.CreateKeySchedule` | `create_key_schedule` |
| Nuages de révision | `RevisionCloud.Create` | `create_revision (action=create_cloud)` |
| Options de conception | `DesignOption` | `list_design_options (lecture seule, voir API_LIMITS)` |
| Plans de surface | `Area, AreaScheme` | `manage_area_plans (SHAB, SU, SDP)` |
| Rampes | `StairsEditScope sur un type OST_Ramps` | `create_ramp` |
| Synchronisation centrale | `Document.SynchronizeWithCentral` | `synchronize_with_central` |
| Toitures | `FootPrintRoof` | `create_surface_based_element (OST_Roofs)` |
| Toposolides | `Toposolid` | `create_toposolid` |
| Trémies et réservations | `Document.Create.NewOpening` | `create_opening (shaft \| host \| wall)` |
| Vues de détail | `ViewSection.CreateCallout` | `create_view (type=callout)` |
| Zones de délimitation | `OST_VolumeOfInterest` | `manage_scope_boxes` |

### Exposé par l'API Revit, pas encore outillé

Vérifié par recherche de l'API dans `src/RiveTT.Tools` sur les 200 outils : aucune de
ces capacités n'a de point d'entrée. Effort : **S** de l'ordre de la journée, **M** de
la semaine, **L** au-delà.

| Capacité absente | API concernée | Priorité | Ce que ça coûte aujourd'hui | Effort |
|---|---|---|---|---|
| Lignes de raccord | `Matchline, ViewBreak` | basse | Grands linéaires découpés sur plusieurs feuilles. Aucune occurrence de `Matchline`. | S |
| Plateformes de construction | `BuildingPad` | basse | `create_toposolid` couvre le terrain, pas la plateforme décaissée qui s'y inscrit. | S |
| Repères de texte | `KeynoteTag et table de repères` | basse | Annotation normalisée par référence plutôt que texte libre. Aucune occurrence de `Keynote` dans le runtime. | M |

Trois manques de priorité basse. Aucun ne bloque une production courante.

### Ce que l'API Revit ne permet pas

Ni lacune ni dette : une frontière. Ces capacités ont été réinscrites comme des
manques à chaque relecture ; elles sont ici pour qu'on cesse de les chercher.

| Capacité | API | Pourquoi c'est fermé |
|---|---|---|
| Légendes | `ViewType.Legend` | L'API ne crée pas de vue de légende de zéro : seul `View.Duplicate()` sur une légende existante fonctionne. `create_view` le signale explicitement plutôt que d'échouer. |
| Options de conception | `DesignOption, DesignOptionSet` | Ni jeu ni option ne se créent par l'API, et `DesignOptionSet` n'est même pas un type public. `list_design_options` lit ce que la boîte de dialogue Revit a créé. |
| Zones de délimitation | `OST_VolumeOfInterest` | Aucune méthode de création : `manage_scope_boxes` inventorie, renomme, déplace et affecte aux vues des boîtes dessinées dans Revit. |

<!-- END GENERATED TOOL INVENTORY -->
