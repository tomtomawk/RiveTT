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

- `create_wall` : type et niveau de base explicites, niveau supérieur et
  offsets optionnels.
- `create_door` / `create_window` : type, hôte et niveau explicites.
- `create_railing` : garde-corps natif depuis un chemin horizontal.
- `set_wall_host` : API de mur hôte de Revit 2027.
- `save_document` / `save_as_document` : sauvegarde du document actif.

Les autres outils sont exposés par les wrappers C# de
`src/RevitCortex.Server/Tools`.

## Diagnostic

- « No MCPRVTT27 Revit 2027 session » : démarrer Revit 2027 et ouvrir un
  projet.
- Plugin absent : vérifier
  `%APPDATA%\Autodesk\Revit\Addins\2027\MCPRVTT27.addin`.
- Journal : `%LOCALAPPDATA%\MCPRVTT27\audit.jsonl`.
- Réinstaller après avoir fermé Revit si une DLL est verrouillée.
