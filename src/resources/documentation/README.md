# Guide RiveTT

RiveTT connecte une application d'IA à Autodesk **Revit 2026.5+ ou 2027**. Il démarre
avec Revit, sans interrupteur ni port réseau. Le seul élément d'interface est le
panneau **RiveTT** de l'onglet *Compléments*, qui porte le verrou d'écriture décrit
plus bas.

Ce dossier contient tout ce qui est installé sur le poste. À côté de ce guide :
`SKILL.md`, destiné à l'agent, et `references/`, le détail opération par opération —
les mêmes fichiers pour l'humain et pour l'agent, pour qu'ils ne racontent jamais deux
versions de la même chose.

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

- *Déclarer RiveTT dans Claude Desktop* ;
- *Déclarer RiveTT dans Codex (application de bureau OpenAI)*.

La page finale dit, pour chaque case cochée, si la déclaration a réussi. À la main,
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
| **État** | pastille bleue d'information | Versions, état du canal nommé, mode courant et son origine, document actif, nombre d'outils publiés, accès au journal d'audit |

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

Contrepartie assumée : le classement est **par outil**, pas par action. Un outil qui
peut écrire est refusé même quand on l'appelle pour lire, par exemple
`manage_model_groups` en `action=inventory`. Une permission qui dépendrait des arguments
dépendrait de 250 implémentations ; celle-ci ne dépend que du classement `toolReadOnly`
déjà publié dans chaque réponse.

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

## Utilisation sûre

- Faire les requêtes de découverte avant de choisir des identifiants de niveaux, types
  ou familles.
- Pour une écriture qui accepte `dryRun`, commencer par la prévisualisation.
- Valider les éléments créés ou modifiés avec un outil de lecture.
- Ne recourir à `send_code_to_revit` que lorsqu'aucun outil dédié ne couvre l'opération.

Tous les outils d'écriture n'exposent pas `dryRun` — 56 sur 135. Nul besoin de le
deviner ni d'ouvrir l'inventaire : `execution.supportsDryRun` le dit dans chaque
réponse, et un `dryRun` demandé à un outil qui n'en a pas est refusé, jamais exécuté
en silence. Pour ceux qui n'en ont pas, limiter la portée de l'appel et vérifier le
résultat immédiatement après.

---

## Outils propres à RiveTT

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
n'est pas lancé du tout et la maquette n'est pas touchée. C'est délibéré. Le routeur
tamponnait auparavant `mutated: false` sur la seule foi de la demande du client, si
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

## Aller plus loin

| Document | Quand |
|---|---|
| `references/index.md` | Le sommaire des références |
| `references/inventaire-des-outils.md` | **Généré depuis le code.** Tous les outils publiés, leur nature, leur `dryRun`, leurs défauts connus |
| `SKILL.md` | Destiné à l'agent : les règles permanentes et quelle référence charger selon la demande |

`SKILL.md` est **présent** sur le poste, pas **actif**. L'activer dans Codex CLI demande
de le copier dans le dossier personnel des skills, ce que l'installateur propose par une
case décochée. À la main :

```powershell
$dest = "$env:USERPROFILE\.codex\skills\rivett"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$env:LOCALAPPDATA\RiveTT\documentation\*" $dest -Recurse -Force
```

Claude Code lit `SKILL.md` directement ; `agents/openai.yaml` ne sert qu'à Codex.

---

## Diagnostic

| Symptôme | À vérifier |
|---|---|
| « No RiveTT Revit session is available » | Revit 2026.5+ ou 2027 démarré, avec un projet ouvert, quelques secondes après l'ouverture |
| Une commande répond « not found » | Mise à jour à moitié appliquée — voir plus haut |
| Le panneau RiveTT n'apparaît pas dans le ruban | `%APPDATA%\Autodesk\Revit\Addins\<2026\|2027>\RiveTT.addin` présent |
| Écritures refusées | Bouton *Écriture* du panneau RiveTT : chaque session démarre en lecture seule |
| Journal des appels | `%LOCALAPPDATA%\RiveTT\audit.jsonl` |
