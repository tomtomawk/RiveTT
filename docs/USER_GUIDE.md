# Guide MCPRVTT27

MCPRVTT27 connecte un client MCP à Autodesk Revit 2027. Il démarre avec Revit
et ne nécessite ni bouton de ruban ni port réseau.

## Installation

Prérequis : Revit 2027 x64 et le SDK/runtime .NET 10.

    .\build.ps1
    .\distribution\install.ps1
    codex mcp add MCPRVTT27 -- "%LOCALAPPDATA%\MCPRVTT27\server\MCPRVTT27.Server.exe"

Fermer Revit avant l'installation. Après redémarrage, ouvrir un projet et
attendre quelques secondes que sa session soit publiée.

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

`open_document` et `edit_family` ne sont pas exposés dans le dispatcher
`ExternalEvent`. L'API Autodesk interdit l'activation d'un document depuis un
gestionnaire d'événement API, et l'édition modale d'une famille nécessite un
orchestrateur distinct pour ne pas bloquer Revit.

Ne sont pas non plus disponibles, et `get_server_capabilities` le déclare :

- création d'un document vierge à partir d'un gabarit (même contrainte que
  `open_document`) — `save_as_document` duplique le document ouvert ;
- création d'escalier : l'escalier standard Revit passe par un éditeur
  d'esquisse modal (`StairsEditScope`), impossible depuis un `ExternalEvent` ;
- propagation d'armatures : absente de l'API Revit sur toutes les versions
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
