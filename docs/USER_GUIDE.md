# Guide MCPRVTT27

MCPRVTT27 connecte un client MCP à Autodesk Revit 2027. Il démarre avec Revit
et ne nécessite ni bouton de ruban ni port réseau.

## Installation

Prérequis : Revit 2027 x64 et le SDK/runtime .NET 10.

    .\build.ps1
    .\distribution\install.ps1
    codex mcp add MCPRVTT27 -- "%LOCALAPPDATA%\MCPRVTT27\server\MCPRVTT27.Server.exe"

`install.ps1` remplace l'installation précédente : **aucune désinstallation
préalable n'est nécessaire**, et il n'est **pas nécessaire de fermer Revit ni le
client MCP**. Windows interdit d'écraser un fichier chargé par un processus mais
autorise à le *renommer* : l'installateur renomme le fichier verrouillé en
`<nom>.old-<horodatage>` — le processus en cours continue de l'utiliser — et
écrit le neuf à sa place. Les copies garées sont supprimées à l'installation
suivante. Le récapitulatif liste les fichiers concernés.

En revanche le code déjà chargé reste en mémoire. Pour utiliser la nouvelle
version :

- **redémarrer Revit** si le plugin a été remplacé pendant qu'il tournait ;
- **reconnecter le serveur MCP** dans le client si `MCPRVTT27.Server` tournait.

Puis ouvrir un projet et attendre quelques secondes que sa session soit publiée.
Vérifier la version active avec `get_server_capabilities`
(`execution.serverVersion`).

## Utilisation sûre

- Faire les requêtes de découverte avant de choisir des IDs de niveaux, types
  ou familles.
- Pour une écriture qui accepte `dryRun`, commencer par la prévisualisation.
- Valider les éléments créés ou modifiés avec un outil de lecture.
- Ne recourir à `send_code_to_revit` que lorsqu'aucun outil dédié ne couvre
  l'opération.

Seuls certains outils exposent `dryRun`. Les mutateurs acier
`set_steel_connection_default_order`, `set_steel_solid_cut_face_splitting` et
`set_steel_fabrication_unique_id` n'ont pas de prévisualisation ; limiter leur
portée et vérifier leur résultat immédiatement.

## Outils spécifiques au fork

- `get_server_capabilities` : contrat effectif du serveur, mode automatique,
  audit, réponses, sélection et limitations de cycle de vie.
- `create_wall` : type et niveau de base explicites, niveau supérieur et
  offsets optionnels. Les coordonnées de `locationLine` sont des coordonnées
  projet absolues en millimètres ; `baseOffset` et `topOffset` sont relatifs à
  leurs niveaux. L'outil prévisualise par défaut et retourne la base, le sommet
  et les décalages réellement appliqués après exécution.
- `create_door` / `create_window` : type, hôte et niveau explicites.
- `create_railing` : garde-corps natif depuis un chemin horizontal.
- `set_wall_host` : API de mur hôte de Revit 2027.
- `capture_selection` : capture des IDs explicites ou de la sélection Revit
  dans un jeton temporaire réutilisable. Les outils bulk acceptent aussi
  `savedSelectionName`, `last_filter` et `elementIds`.
- `duplicate_storey` : analyse puis duplication transactionnelle d'un étage,
  avec catégories, groupes, niveau haut des murs et déplacement optionnel des
  niveaux supérieurs.
- `detach_wall_constraint` : retrait d'une contrainte haute de niveau ou d'une
  attache haute/basse en conservant la hauteur non contrainte.
- `manage_model_groups` : inventaire, duplication de type et dissociation
  contrôlée des groupes de modèle.
- `save_document` / `save_as_document` : sauvegarde du document actif, avec
  `dryRun` (chemins, existence de la cible, écrasement, accessibilité du
  dossier, verrou, modifications non enregistrées). `save_as_document`
  **duplique le document ouvert** : ce n'est pas un nouveau projet vierge.
- `list_system_types` : types système (murs, sols, plafonds, toits,
  garde-corps, escaliers, cartouches…), inaccessibles autrement. Sans
  catégorie, retourne l'inventaire par catégorie avec les codes `OST_*`.
- `create_detail_line` / `create_model_line` /
  `create_room_separation_line` : lignes 2D, 3D et séparations de pièces.
  Couper une pièce sans mur physique se fait avec la troisième.
- `place_title_block` : pose un cartouche sur une feuille existante (réparation
  d'une feuille sans cadre).
- `create_document` : **nouveau projet vierge** depuis un gabarit `.rte`,
  enregistré au chemin demandé. C'est le vrai « nouveau projet » :
  `save_as_document` duplique le modèle ouvert avec tout son historique.
  `activate: true` l'ouvre ensuite dans Revit.
- `open_document` : ouvre un `.rvt` et en fait le document actif. Tous les
  appels suivants le ciblent et les caches sont vidés. Enregistrer le document
  courant avant : le changement ne le sauvegarde pas.
- `create_stair` : escalier par composant entre deux niveaux, volées droites
  (`runs`) et paliers automatiques. La réponse compare `actualRiserCount` à
  `desiredRiserCount` et donne `reachesTopLevel` : une volée trop courte produit
  un escalier qui n'atteint pas l'étage.
- `edit_group_members` : ajout/retrait de membres d'un groupe. L'API Revit ne
  sait pas modifier un groupe en place : l'outil dégroupe, modifie, regroupe, et
  **crée donc un nouveau type de groupe**. Refuse un type à plusieurs occurrences
  sauf `allowMultiInstance: true`, car les autres occurrences gardent l'ancienne
  définition.

### Groupes, exclusions et occurrences divergentes

Deux occurrences d'un **même type** de groupe peuvent légitimement différer — ce
n'est pas une anomalie du modèle :

- **exclusion de membre** : un élément retiré d'une occurrence seulement. Revit
  suffixe alors le nom de cette occurrence par « (membre exclu) ». Le type, sa
  définition et les autres occurrences ne bougent pas ;
- **contraintes propres** : un mur groupé peut être plus haut dans une occurrence
  parce que ses contraintes de niveau y sont différentes.

Ce que cela implique pour le pilotage :

| Opération | Ce que fait le connecteur |
|---|---|
| Retirer un membre | `delete_element` sur ce membre, ou `edit_group_members` avec `removeElementIds` seuls : c'est une **exclusion**. Le type garde son id et ses autres occurrences leurs éléments. La réponse liste `groupExclusionIds` |
| Ajouter un membre | Seule voie possible : dégrouper / modifier / regrouper, donc **création d'un nouveau type** ; les autres occurrences gardent l'ancienne définition. Refusé sur un type à plusieurs occurrences sauf `allowMultiInstance: true` |
| Rétablir un membre exclu | **Impossible par l'API.** Sélectionner l'occurrence dans Revit puis « Rétablir les éléments exclus » du ruban |
| Dissoudre | `manage_model_groups action=ungroup` sur les occurrences visées |
| Détecter les exclusions | `manage_model_groups` retourne, par occurrence, `memberCount`, `excludedCount` et `hasExcludedMembers`, et lit la définition complète depuis l'occurrence la plus fournie |

Chaque occurrence possède **ses propres copies** des membres, avec ses propres
identifiants : un id relevé sur une occurrence n'a aucun sens dans une autre.

### Changer la hauteur d'un niveau sans casser les groupes

Modifier l'altimétrie d'un niveau sous des groupes contraints les déforme ou les
éclate en types divergents. Procédure recommandée, qui préserve les symétries et
l'identité du type :

1. créer deux niveaux temporaires au-dessus du bâtiment, à l'écart
   (`create_level`), en conservant **le même écart** que les niveaux d'origine ;
2. copier les groupes vers ces niveaux (`copy_elements` avec `offsetZ`, qui
   réassocie les contraintes haute et basse aux niveaux réels) ;
3. faire les manipulations d'altimétrie sur les niveaux d'origine
   (`create_level action=set`) ;
4. recopier les groupes depuis les niveaux temporaires vers les niveaux
   redéfinis, puis supprimer les niveaux temporaires.

Les groupes reviennent avec leurs miroirs et leur type d'origine, ce qu'aucun
dégroupage/regroupage ne sait faire.

### Conventions à connaître

- **Unités.** Entrées en millimètres. En sortie, toute valeur numérique de
  paramètre porte `value` (unités du projet), `unit`, `displayValue` et
  `internalValue` (unités internes Revit : pieds, pieds², pieds³).
- **Altimétrie.** `create_wall` ignore le `z` de `locationLine` : `baseLevelId`
  et `baseOffset` font foi. `create_door`/`create_window` attendent un `z`
  **absolu projet**, sauf avec `zMode: "relativeToLevel"` où `z` s'ajoute à
  l'altitude du niveau. Un point hors de la plage verticale de l'hôte est
  refusé avec les bornes en mm.
- **Noms de paramètres.** Ils se résolvent en anglais comme dans la langue du
  document (`Mark`/`Repère`, `Level`/`Niveau`, `Width`/`Largeur`). Un nom non
  résolu est signalé dans `unresolvedParameterNames` avec des suggestions,
  jamais rendu par une colonne vide.
- **Catégories.** Les libellés sont localisés et parfois ambigus (Revit FR
  nomme la catégorie des vues portées « Fenêtres », comme les fenêtres) :
  préférer le code `OST_*` retourné dans `categoryBic`.

### Réponses et pagination

Chaque succès contient `execution.connector`, `serverVersion`, `revitVersion`,
`mode`, `toolReadOnly`, `toolDestructive`, `writesAllowed` et `cached`. Un
aperçu d'écriture contient toujours `dryRun:true` et `mutated:false`.

`toolReadOnly` classe **l'outil qui répond**, ce n'est pas un état de session :
MCPRVTT27 n'a pas de mode lecture seule, `writesAllowed` vaut toujours `true`.
`cached: true` signale une réponse servie par le cache. Tout cache est vidé
après `save_document`/`save_as_document`.

`ai_element_filter` utilise `responseMode: summary | idsOnly | details` et
retourne `totalCount`, `returnedCount`, `appliedLimit` et `nextCursor`. Un
curseur devient invalide dès que le document Revit change, afin d'éviter de
mélanger deux états du modèle.

Les erreurs de transaction fournissent `warnings`, `errors`, `rolledBack`,
`failedElementIds` et `repairHints`. Les nouveaux workflows complexes acceptent
`warningPolicy: suppress_all | allow_list` ; avec `allow_list`, tout
avertissement non autorisé provoque un rollback silencieux.

### Limites de cycle de vie

L'interdiction d'activer un document vise les **gestionnaires d'événements API**
(`Idling`, `DocumentChanged`), pas un `ExternalEvent` — le contexte dans lequel
tourne chaque outil de ce connecteur. `open_document` et `create_document` sont
donc disponibles, conformément à la recommandation Autodesk (External Event =
« supported and safe » pour ouvrir/activer).

Restent indisponibles, et `get_server_capabilities` le déclare :

- **ouverture du document de famille** (`Document.EditFamily`) : a provoqué un
  interblocage depuis ce dispatcher. Pour modifier une famille : éditer le
  `.rfa` hors Revit puis `load_family` ;
- **escaliers esquissés**, volées hélicoïdales et balancements :
  `create_stair` couvre l'escalier par composant (volées droites + paliers) ;
- **édition de groupe en place** : l'API ne le permet pas,
  `edit_group_members` dégroupe/regroupe et ne propage pas aux autres
  occurrences du type ;
- **propagation d'armatures** : absente de l'API Revit sur toutes les versions
  supportées.

Les autres outils sont exposés par les wrappers C# de
`src/RevitCortex.Server/Tools`.

## Diagnostic

- « No MCPRVTT27 Revit 2027 session » : démarrer Revit 2027 et ouvrir un
  projet.
- Plugin absent : vérifier
  `%APPDATA%\Autodesk\Revit\Addins\2027\MCPRVTT27.addin`.
- Journal : `%LOCALAPPDATA%\MCPRVTT27\audit.jsonl`.
- Réinstaller après avoir fermé Revit si une DLL est verrouillée.
