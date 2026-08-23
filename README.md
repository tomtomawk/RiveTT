# MCPRVTT27

MCP local pour **Autodesk Revit 2027**, basé sur RevitCortex (licence MIT).

## Principes

- une seule cible : Revit 2027 / .NET 10 / x64 ;
- transport local par **Windows Named Pipe**, jamais par port TCP ;
- démarrage automatique de l'add-in avec Revit, sans interrupteur de démarrage ;
- verrou d'écriture au ruban (*Compléments → MCPRVTT27*) : chaque session Revit
  démarre en lecture seule, aucun outil ne peut lever le verrou ;
- mode automatique permanent : aucune boîte d'autorisation ou licence ;
- pas de Power BI, télémétrie, mise à jour automatique ni compte commercial ;
- serveur MCP standard sur `stdio`, nommé `MCPRVTT27`.

Les appels transitent uniquement entre le client MCP, le serveur stdio et le
processus Revit de l'utilisateur courant. Les écritures Revit restent dans des
transactions et sont consignées dans `%LOCALAPPDATA%\MCPRVTT27\audit.jsonl`.

## Fonctions ajoutées

- capacités serveur et contrat d'exécution (`get_server_capabilities`) ;
- création de murs avec aperçu et validation des niveaux/décalages réels ;
- création de portes et fenêtres sur familles réellement présentes dans le projet ;
- garde-corps natifs (`create_railing`) ;
- association d'un mur à son mur hôte en Revit 2027 (`set_wall_host`) ;
- sélection temporaire stable (`capture_selection`) et scopes bulk explicites ;
- synchronisation localisée via `BuiltInParameter` ;
- recherches paginées avec modes résumé, IDs et détails ;
- duplication transactionnelle d'étage (`duplicate_storey`) ;
- contraintes/attaches de murs et gestion contrôlée des groupes ;
- diagnostics Revit normalisés et audit entrée/sortie ;
- sauvegarde et sauvegarde sous du projet actif (`save_document`,
  `save_as_document`), avec aperçu `dryRun` ;
- énumération des types système (`list_system_types`) : murs, sols, plafonds,
  toits, garde-corps, escaliers, cartouches ;
- lignes de détail, lignes de modèle et séparations de pièces
  (`create_detail_line`, `create_model_line`, `create_room_separation_line`) ;
- pose d'un cartouche sur une feuille existante (`place_title_block`) ;
- création d'un **projet vierge** depuis un gabarit `.rte` (`create_document`) et
  ouverture/activation d'un fichier (`open_document`) ;
- **escaliers** par composant entre deux niveaux, volées droites et paliers
  automatiques (`create_stair`) ;
- édition des membres d'un groupe dans les limites de l'API
  (`edit_group_members`).

## Contrat de réponse

- noms de paramètres résolus en anglais **ou** dans la langue du document
  (`Mark`/`Repère`, `Level`/`Niveau`) ; un nom non résolu est signalé, jamais
  rendu par une valeur vide ;
- valeurs numériques accompagnées de leur unité et de la valeur interne Revit ;
- catégories accompagnées de leur code `OST_*` (`categoryBic`), les libellés
  localisés étant ambigus ;
- `execution.toolReadOnly` classe l'outil, pas la session : `writesAllowed` vaut
  toujours `true` ; `execution.cached` signale une réponse issue du cache.

Détail des corrections issues de la campagne de tests :
[docs/MCP_AGENT_FIXES.md](docs/MCP_AGENT_FIXES.md).

## Compiler et installer

Prérequis : .NET SDK 10 et Revit 2027.

```powershell
cd MCPRVTT27
.\build.ps1
.\distribution\install.ps1
```

L'installation est par utilisateur dans
`%APPDATA%\Autodesk\Revit\Addins\2027\MCPRVTT27` et ne demande pas de droits
administrateur. Elle prépare le serveur dans `%LOCALAPPDATA%\MCPRVTT27\server`.

Pour enregistrer le serveur dans Codex :

```powershell
codex mcp add MCPRVTT27 -- "%LOCALAPPDATA%\MCPRVTT27\server\MCPRVTT27.Server.exe"
```

Fermez Revit avant une réinstallation. Ouvrez ensuite Revit 2027 et un projet :
la session est publiée automatiquement et le serveur MCP la découvre sans
configuration de port.

## Vérifier

```powershell
dotnet test .\src\RevitCortex.Tests\RevitCortex.Tests.csproj -c Release
dotnet build .\RevitCortex.sln -c Release
```

Le build compile les DLL du plugin, les outils et le serveur, puis prépare le
paquet dans `distribution`.

## Licence

Ce dérivé conserve la licence [MIT](LICENSE) du projet source RevitCortex.
