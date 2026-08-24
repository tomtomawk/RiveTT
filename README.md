# RiveTT

MCP local pour **Autodesk Revit 2026.5+ et 2027**, basé sur RevitCortex (licence MIT).

## Principes

- deux cibles, un seul code source : Revit 2026.5+ ou 2027 / .NET 10 / x64,
  sélectionnées au build via `-p:RevitVersion=2026|2027` (voir « Compiler et installer » ci-dessous) ;
- transport local par **Windows Named Pipe**, jamais par port TCP ;
- démarrage automatique de l'add-in avec Revit, sans interrupteur de démarrage ;
- verrou d'écriture au ruban (*Compléments → RiveTT*) : chaque session Revit
  démarre en lecture seule, aucun outil ne peut lever le verrou ;
- mode automatique permanent : aucune boîte d'autorisation ou licence ;
- pas de Power BI, télémétrie, mise à jour automatique ni compte commercial ;
- serveur MCP standard sur `stdio`, nommé `RiveTT`.

Les appels transitent uniquement entre le client MCP, le serveur stdio et le
processus Revit de l'utilisateur courant. Les écritures Revit restent dans des
transactions et sont consignées dans `%LOCALAPPDATA%\RiveTT\audit.jsonl`.

## Fonctions ajoutées

- capacités serveur et contrat d'exécution (`get_server_capabilities`) ;
- création de murs avec aperçu et validation des niveaux/décalages réels ;
- création de portes et fenêtres sur familles réellement présentes dans le projet ;
- garde-corps natifs (`create_railing`) ;
- association d'un mur à son mur hôte, Revit 2027 uniquement (`set_wall_host`) ;
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
- `execution.toolReadOnly` classe l'outil, pas la session ;
  `execution.writesAllowed` donne l'état du verrou d'écriture du ruban — faux au
  démarrage de chaque session Revit, et aucun outil ne peut le lever ;
  `execution.cached` signale une réponse issue du cache.

- corrections issues de la campagne de tests :
  [docs/MCP_AGENT_FIXES.md](docs/MCP_AGENT_FIXES.md) ;
- lecture de l'audit de la surface d'outils et ordre de chantier :
  [docs/AUDIT_OUTILS.md](docs/AUDIT_OUTILS.md) ;
- inventaire des 196 outils, effet par effet, avec les défauts probables et les
  capacités API non outillées : [docs/INVENTAIRE_OUTILS.md](docs/INVENTAIRE_OUTILS.md),
  ou la même matière filtrable dans [docs/inventaire.html](docs/inventaire.html).
  Les deux sont générés par `tools/audit-tool-surface.py`.

## Compiler et installer

Prérequis : .NET SDK 10 et Revit 2026.5+ ou 2027.

```powershell
cd RiveTT
.\build.ps1                              # Revit 2027 par défaut
.\distribution\install.ps1

# Pour Revit 2026.5 :
.\build.ps1 -RevitVersion 2026
.\distribution\install.ps1 -RevitYear 2026
```

L'installation est par utilisateur dans
`%APPDATA%\Autodesk\Revit\Addins\<2026|2027>\RiveTT` et ne demande pas de droits
administrateur. Elle prépare le serveur dans `%LOCALAPPDATA%\RiveTT\server`.

Pour enregistrer le serveur dans Codex :

```powershell
codex mcp add RiveTT -- "%LOCALAPPDATA%\RiveTT\server\RiveTT.Server.exe"
```

Fermez Revit avant une réinstallation. Ouvrez ensuite Revit et un projet :
la session est publiée automatiquement et le serveur MCP la découvre sans
configuration de port.

## Vérifier

```powershell
dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release
dotnet build .\RiveTT.sln -c Release
```

Le build compile les DLL du plugin, les outils et le serveur, puis prépare le
paquet dans `distribution`.

## Contribuer

Règles d'architecture, contrat outils, verrou d'écriture et commandes de
vérification : voir [AGENTS.md](AGENTS.md).

## Licence

Ce dérivé conserve la licence [MIT](LICENSE) du projet source RevitCortex.
